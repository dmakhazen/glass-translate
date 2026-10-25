using System.Windows.Media;

namespace GlassTranslate.Ocr;

/// <summary>A paragraph-like group of OCR lines translated as one piece of text.</summary>
public sealed record TextRegion(PixelRect Box, string Text, int LineCount, double LineHeight);

public static class TextLayout
{
    public static List<TextRegion> Group(Frame frame, List<OcrLine> lines)
    {
        var colors = new Dictionary<PixelRect, (Color bg, Color fg)>();
        (Color bg, Color fg) ColorsOf(PixelRect r) =>
            colors.TryGetValue(r, out var c) ? c : colors[r] = TextColors.Pick(frame, r);
        // Lines of one paragraph share text and background colour; a price, badge or label next to it usually doesn't.
        bool SameBackground(OcrLine a, OcrLine b) => TextColors.Distance(ColorsOf(a.Box).bg, ColorsOf(b.Box).bg) < 60;
        bool SameStyle(OcrLine a, OcrLine b) =>
            SameBackground(a, b) && TextColors.Distance(ColorsOf(a.Box).fg, ColorsOf(b.Box).fg) < 90;

        var rows = MergeRows(lines, SameBackground);
        var blocks = new List<List<OcrLine>>();
        foreach (var row in rows.OrderBy(r => r.Box.Y))
        {
            var target = blocks.FirstOrDefault(b => Continues(b[^1], row) && SameStyle(b[^1], row));
            if (target != null) target.Add(row);
            else blocks.Add([row]);
        }
        return blocks.Select(b =>
        {
            var box = Union(b.Select(l => l.Box));
            return new TextRegion(box, string.Join(" ", b.Select(l => l.Text)), b.Count,
                b.Average(l => l.Box.H));
        }).ToList();
    }

    /// <summary>The detector often splits one visual line into pieces; join neighbours on the same row.</summary>
    private static List<OcrLine> MergeRows(List<OcrLine> lines, Func<OcrLine, OcrLine, bool> sameBackground)
    {
        var result = new List<OcrLine>();
        // Remember the last original piece of each merged row, so colours are compared piece to piece.
        var lastPiece = new List<OcrLine>();
        foreach (var line in lines.OrderBy(l => l.Box.X))
        {
            int idx = result.FindIndex(r =>
            {
                int overlap = Math.Min(r.Box.Bottom, line.Box.Bottom) - Math.Max(r.Box.Y, line.Box.Y);
                int minH = Math.Min(r.Box.H, line.Box.H);
                int gap = line.Box.X - r.Box.Right;
                double avgH = (r.Box.H + line.Box.H) / 2.0;
                return overlap >= minH * 0.6 && gap < avgH * 0.8 && gap > -avgH
                       && Math.Max(r.Box.H, line.Box.H) < minH * 1.6;
            });
            if (idx >= 0 && !sameBackground(lastPiece[idx], line)) idx = -1;
            if (idx < 0) { result.Add(line); lastPiece.Add(line); continue; }
            var r = result[idx];
            result[idx] = new OcrLine(Union([r.Box, line.Box]), r.Text + " " + line.Text,
                Math.Min(r.Confidence, line.Confidence));
            lastPiece[idx] = line;
        }
        return result;
    }

    private static bool Continues(OcrLine prev, OcrLine next)
    {
        double h = Math.Max(prev.Box.H, next.Box.H);
        double gap = next.Box.Y - prev.Box.Bottom;
        bool similarSize = Math.Max(prev.Box.H, next.Box.H) < Math.Min(prev.Box.H, next.Box.H) * 1.5;
        bool overlapX = Math.Min(prev.Box.Right, next.Box.Right) > Math.Max(prev.Box.X, next.Box.X);
        bool alignedLeft = Math.Abs(prev.Box.X - next.Box.X) < h;
        // A wrapped line only starts once the previous one has run (nearly) as far right.
        bool wrapped = prev.Box.Right >= next.Box.Right - 2 * h;
        return gap > -h * 0.3 && gap < h * 0.6 && similarSize && overlapX && alignedLeft && wrapped;
    }

    private static PixelRect Union(IEnumerable<PixelRect> rects)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var r in rects)
        {
            x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y);
            x1 = Math.Max(x1, r.Right); y1 = Math.Max(y1, r.Bottom);
        }
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
