using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GlassTranslate.Ocr;

public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
}

public sealed record OcrLine(PixelRect Box, string Text, float Confidence);

/// <summary>PaddleOCR (PP-OCRv5) pipeline on ONNX Runtime: DB text detector + CTC line recognizers.</summary>
public sealed class OcrEngine : IDisposable
{
    private static readonly float[] DetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] DetStd = [0.229f, 0.224f, 0.225f];
    private const float BinThreshold = 0.3f, BoxThreshold = 0.6f, UnclipRatio = 1.5f;
    private const int MaxDetSide = 1600;

    private readonly SessionOptions _options;
    private InferenceSession? _det;
    private readonly Dictionary<string, Recognizer> _recs = new();

    public OcrEngine()
    {
        _options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
        };
        // By default worker threads busy-wait between runs, burning CPU while the app is idle.
        _options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        _options.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
    }

    public long LastDetectMs { get; private set; }
    public long LastRecognizeMs { get; private set; }

    public List<OcrLine> Recognize(Frame frame, IReadOnlyList<string> groups)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var boxes = Detect(frame);
        LastDetectMs = sw.ElapsedMilliseconds;
        sw.Restart();
        boxes.RemoveAll(b => b.H > b.W * 2.5); // vertical text isn't supported
        var best = new OcrLine?[boxes.Count];
        // Run recognizers in priority order; later ones only see the lines earlier ones weren't sure about.
        foreach (var rec in groups.Select(GetRecognizer))
        {
            var pending = Enumerable.Range(0, boxes.Count).Where(i => best[i] is not { Confidence: >= SureConfidence }).ToList();
            if (pending.Count == 0) break;
            var results = rec.Run(frame, pending.Select(i => boxes[i]).ToList());
            for (int k = 0; k < pending.Count; k++)
            {
                int i = pending[k];
                if (best[i] == null || results[k].confidence > best[i]!.Confidence)
                    best[i] = new OcrLine(boxes[i], results[k].text.Trim(), results[k].confidence);
            }
        }
        LastRecognizeMs = sw.ElapsedMilliseconds;
        return best.OfType<OcrLine>()
            .Where(l => l.Confidence >= MinConfidence && l.Text.Count(char.IsLetter) >= 2).ToList();
    }

    private const float SureConfidence = 0.85f, MinConfidence = 0.7f;

    private List<PixelRect> Detect(Frame frame)
    {
        _det ??= new InferenceSession(ModelStore.DetectorPath, _options);

        // Screen text is small compared to the photos the detector was trained on, so upscale.
        double s = Math.Min(2.0, (double)MaxDetSide / Math.Max(frame.Width, frame.Height));
        int rw = Math.Max(32, (int)Math.Round(frame.Width * s / 32) * 32);
        int rh = Math.Max(32, (int)Math.Round(frame.Height * s / 32) * 32);
        float sx = (float)frame.Width / rw, sy = (float)frame.Height / rh;

        var input = new DenseTensor<float>([1, 3, rh, rw]);
        var buf = input.Buffer.Span;
        int plane = rw * rh;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
                for (int c = 0; c < 3; c++)
                {
                    // Paddle feeds BGR straight into the normalizer; our frame is BGRA, so channel order matches.
                    float v = frame.Sample((x + 0.5f) * sx - 0.5f, (y + 0.5f) * sy - 0.5f, c) / 255f;
                    buf[c * plane + y * rw + x] = (v - DetMean[c]) / DetStd[c];
                }

        string inName = _det.InputMetadata.Keys.First();
        using var results = _det.Run([NamedOnnxValue.CreateFromTensor(inName, input)]);
        var prob = results.First().AsTensor<float>().ToArray(); // [1,1,rh,rw]

        var boxes = new List<PixelRect>();
        var label = new int[plane];
        var stack = new Stack<int>();
        int next = 0;
        for (int start = 0; start < plane; start++)
        {
            if (label[start] != 0 || prob[start] < BinThreshold) continue;
            next++;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            double sum = 0;
            stack.Push(start);
            label[start] = next;
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % rw, py = p / rw;
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                sum += prob[p];
                count++;
                for (int k = 0; k < 4; k++)
                {
                    int nx = px + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = py + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= rw || ny >= rh) continue;
                    int q = ny * rw + nx;
                    if (label[q] != 0 || prob[q] < BinThreshold) continue;
                    label[q] = next;
                    stack.Push(q);
                }
            }
            int w = maxX - minX + 1, h = maxY - minY + 1;
            if (w < 4 || h < 4 || sum / count < BoxThreshold) continue;

            // DB "unclip": the network predicts shrunken text kernels, grow them back.
            double d = w * h * UnclipRatio / (2.0 * (w + h));
            double x0 = (minX - d) * sx, y0 = (minY - d) * sy, x1 = (maxX + 1 + d) * sx, y1 = (maxY + 1 + d) * sy;
            int ix0 = Math.Clamp((int)x0, 0, frame.Width - 1), iy0 = Math.Clamp((int)y0, 0, frame.Height - 1);
            int ix1 = Math.Clamp((int)Math.Ceiling(x1), 1, frame.Width), iy1 = Math.Clamp((int)Math.Ceiling(y1), 1, frame.Height);
            if (ix1 - ix0 >= 3 && iy1 - iy0 >= 3)
                boxes.Add(new PixelRect(ix0, iy0, ix1 - ix0, iy1 - iy0));
        }
        return boxes;
    }

    private Recognizer GetRecognizer(string group)
    {
        if (!_recs.TryGetValue(group, out var rec))
            _recs[group] = rec = new Recognizer(group, _options);
        return rec;
    }

    public void Dispose()
    {
        _det?.Dispose();
        foreach (var r in _recs.Values) r.Dispose();
        _options.Dispose();
    }

    private sealed class Recognizer : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string[] _chars;
        private readonly int _height;
        private readonly string _input;

        public Recognizer(string group, SessionOptions options)
        {
            _session = new InferenceSession(ModelStore.RecognizerPath(group), options);
            _input = _session.InputMetadata.Keys.First();
            int h = _session.InputMetadata[_input].Dimensions[2];
            _height = h > 0 ? h : 48;
            // CTC classes: 0 = blank, then the dictionary, then a trailing space.
            var dict = File.ReadAllLines(ModelStore.DictPath(group)).Select(l => l.TrimEnd('\r'));
            _chars = ["", .. dict, " "];
        }

        private const int BatchSize = 8;

        /// <summary>Recognizes many lines, batching crops of similar aspect ratio (padded to a common width).</summary>
        public (string text, float confidence)[] Run(Frame frame, List<PixelRect> boxes)
        {
            var results = new (string, float)[boxes.Count];
            int Width(PixelRect b) => Math.Clamp(((int)Math.Ceiling(_height * (double)b.W / b.H) + 7) / 8 * 8, 16, 3200);
            var order = Enumerable.Range(0, boxes.Count).OrderBy(i => Width(boxes[i])).ToList();
            for (int start = 0; start < order.Count; start += BatchSize)
            {
                var batch = order.Skip(start).Take(BatchSize).ToList();
                int h = _height, w = batch.Max(i => Width(boxes[i]));
                var input = new DenseTensor<float>([batch.Count, 3, h, w]); // zero = mid-grey padding
                var buf = input.Buffer.Span;
                int plane = h * w;
                for (int n = 0; n < batch.Count; n++)
                {
                    var box = boxes[batch[n]];
                    int bw = Width(box);
                    float fx = (float)box.W / bw, fy = (float)box.H / h;
                    int baseOff = n * 3 * plane;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < bw; x++)
                            for (int c = 0; c < 3; c++)
                            {
                                float v = frame.Sample(box.X + (x + 0.5f) * fx - 0.5f, box.Y + (y + 0.5f) * fy - 0.5f, c);
                                buf[baseOff + c * plane + y * w + x] = (v / 255f - 0.5f) / 0.5f;
                            }
                }

                using var output = _session.Run([NamedOnnxValue.CreateFromTensor(_input, input)]);
                var tensor = output.First().AsTensor<float>();
                int steps = tensor.Dimensions[1], classes = tensor.Dimensions[2];
                var data = tensor.ToArray();
                for (int n = 0; n < batch.Count; n++)
                    results[batch[n]] = Decode(data, n * steps * classes, steps, classes);
            }
            return results;
        }

        /// <summary>Greedy CTC decoding: best class per step, collapse repeats, drop blanks.</summary>
        private (string, float) Decode(float[] data, int offset, int steps, int classes)
        {
            var sb = new System.Text.StringBuilder();
            int prev = -1;
            double confSum = 0;
            int kept = 0;
            for (int t = 0; t < steps; t++)
            {
                int bestIdx = 0;
                float bestVal = float.MinValue;
                int off = offset + t * classes;
                for (int k = 0; k < classes; k++)
                    if (data[off + k] > bestVal) { bestVal = data[off + k]; bestIdx = k; }
                if (bestIdx != 0 && bestIdx != prev && bestIdx < _chars.Length)
                {
                    sb.Append(_chars[bestIdx]);
                    confSum += bestVal;
                    kept++;
                }
                prev = bestIdx;
            }
            return (sb.ToString(), kept == 0 ? 0 : (float)(confSum / kept));
        }

        public void Dispose() => _session.Dispose();
    }
}
