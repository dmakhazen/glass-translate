using System.IO;
using System.Net.Http;

namespace GlassTranslate.Ocr;

/// <summary>OCR models live next to the exe in "models\", so the app stays portable.
/// Recognizers for other scripts are downloaded there on first use.</summary>
public static class ModelStore
{
    private const string BaseUrl = "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/";

    public static string Root => Path.Combine(AppContext.BaseDirectory, "models");
    public static string DetectorPath => Path.Combine(Root, "det", "det.onnx");
    public static string RecognizerPath(string group) => Path.Combine(Root, group, "rec.onnx");
    public static string DictPath(string group) => Path.Combine(Root, group, "dict.txt");

    public static bool IsInstalled(string group) =>
        File.Exists(RecognizerPath(group)) && File.Exists(DictPath(group));

    public static IEnumerable<string> InstalledGroups() =>
        Directory.Exists(Root)
            ? Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Where(IsInstalled)
            : [];

    /// <summary>Recognizers for "Auto" source: every recognizer also reads basic Latin, so the specialised
    /// ones go first and the plain English one last.</summary>
    public static List<string> AutoGroups()
    {
        string[] order = ["thai", "eslav", "latin"];
        return InstalledGroups()
            .OrderBy(g => g == "english" ? 99 : Array.IndexOf(order, g) is var i and >= 0 ? i : 50).ToList();
    }

    public static async Task DownloadAsync(string group, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        Directory.CreateDirectory(Path.Combine(Root, group));
        // The dictionary is tiny; fetch it first so progress reflects the model download.
        await DownloadFileAsync(http, $"languages/{group}/dict.txt", DictPath(group), null, ct);
        await DownloadFileAsync(http, $"languages/{group}/rec.onnx", RecognizerPath(group), progress, ct);
    }

    private static async Task DownloadFileAsync(HttpClient http, string remote, string target,
        IProgress<double>? progress, CancellationToken ct)
    {
        string tmp = target + ".part";
        using (var resp = await http.GetAsync(BaseUrl + remote, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(tmp, target, overwrite: true);
    }
}
