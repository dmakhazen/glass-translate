using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GlassTranslate.Translation;

public sealed record Translation(string Text, string? DetectedLanguage);

public interface ITranslator
{
    string Id { get; }
    string DisplayName { get; }
    /// <param name="from">ISO code, or null to auto-detect.</param>
    Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string? from, string to, CancellationToken ct);
}

internal static class Http
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public static HttpClient Create(CookieContainer? cookies = null)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies ?? new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
        };
        // Google answers 429 to requests that don't look like a plain HTTP/1.1 browser call.
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }
}

/// <summary>The public endpoint used by Google's browser extensions; no key required.</summary>
public sealed class GoogleTranslator : ITranslator
{
    private readonly HttpClient _http = Http.Create();
    public string Id => "google";
    public string DisplayName => "Google";

    private static string Code(string c) => c switch { "zh" => "zh-CN", _ => c };

    public async Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string? from, string to, CancellationToken ct)
    {
        // Pack blocks into URL-sized batches (one q= per block). Google throttles these keyless endpoints
        // now and then with 429, so fall back to the second endpoint and retry once after a pause.
        var batches = new List<List<int>> { new() };
        int size = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            int len = Uri.EscapeDataString(texts[i]).Length + 3;
            if (size + len > 6000 && batches[^1].Count > 0) { batches.Add([]); size = 0; }
            batches[^1].Add(i);
            size += len;
        }
        var result = new Translation[texts.Count];
        foreach (var batch in batches)
        {
            var part = batch.Select(i => texts[i]).ToList();
            Translation[] done;
            try { done = await TranslateBatchAsync(part, from, to, ct); }
            catch (HttpRequestException)
            {
                try { done = await Task.WhenAll(part.Select(t => TranslateOneAsync(t, from, to, ct))); }
                catch (HttpRequestException)
                {
                    await Task.Delay(800, ct);
                    done = await TranslateBatchAsync(part, from, to, ct);
                }
            }
            for (int k = 0; k < batch.Count; k++) result[batch[k]] = done[k];
        }
        return result;
    }

    /// <summary>The Chrome dictionary extension endpoint: accepts many q= at once.</summary>
    private async Task<Translation[]> TranslateBatchAsync(List<string> texts, string? from, string to, CancellationToken ct)
    {
        string url = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={(from == null ? "auto" : Code(from))}" +
                     $"&tl={Code(to)}" + string.Concat(texts.Select(t => "&q=" + Uri.EscapeDataString(t)));
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var items = doc.RootElement.EnumerateArray().ToList();
        if (items.Count != texts.Count) throw new HttpRequestException("Google: unexpected response");
        // With auto-detect each item is [translation, detectedLang]; with a fixed source it's just the string.
        return items.Select(e => e.ValueKind == JsonValueKind.Array
            ? new Translation(e[0].GetString() ?? "", e.GetArrayLength() > 1 ? e[1].GetString() : from)
            : new Translation(e.GetString() ?? "", from)).ToArray();
    }

    private async Task<Translation> TranslateOneAsync(string text, string? from, string to, CancellationToken ct)
    {
        string url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t" +
                     $"&sl={(from == null ? "auto" : Code(from))}&tl={Code(to)}&q={Uri.EscapeDataString(text)}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var sb = new StringBuilder();
        foreach (var seg in doc.RootElement[0].EnumerateArray())
            if (seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
        string? detected = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String
            ? doc.RootElement[2].GetString() : null;
        return new Translation(sb.ToString(), detected);
    }
}

/// <summary>Bing Translator's web endpoint. Needs a session token scraped from the translator page.</summary>
public sealed class BingTranslator : ITranslator
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private (string ig, string iid, string key, string token, DateTime expires)? _session;

    public BingTranslator() => _http = Http.Create(_cookies);
    public string Id => "bing";
    public string DisplayName => "Bing (Microsoft)";

    private static string Code(string c) => c switch { "zh" => "zh-Hans", "tl" => "fil", _ => c };
    private static string FromBing(string c) => c switch { "zh-Hans" or "zh-Hant" => "zh", "fil" => "tl", _ => c };

    public async Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string? from, string to, CancellationToken ct)
    {
        var result = new Translation[texts.Count];
        // Bing accepts one text per call; keep a few in flight.
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(texts.Select(async (t, i) =>
        {
            await gate.WaitAsync(ct);
            try { result[i] = await TranslateOneAsync(t, from, to, ct, retry: true); }
            finally { gate.Release(); }
        }));
        return result;
    }

    private async Task<Translation> TranslateOneAsync(string text, string? from, string to, CancellationToken ct, bool retry)
    {
        var s = await GetSessionAsync(ct);
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fromLang"] = from == null ? "auto-detect" : Code(from),
            ["to"] = Code(to),
            ["text"] = text.Length > 1000 ? text[..1000] : text,
            ["token"] = s.token,
            ["key"] = s.key,
        });
        using var resp = await _http.PostAsync($"https://www.bing.com/ttranslatev3?isVertical=1&IG={s.ig}&IID={s.iid}", body, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            if (!retry) throw new HttpRequestException("Bing: " + json);
            _session = null;
            return await TranslateOneAsync(text, from, to, ct, retry: false);
        }
        var item = doc.RootElement[0];
        string? detected = item.TryGetProperty("detectedLanguage", out var d) ? FromBing(d.GetProperty("language").GetString()!) : null;
        return new Translation(item.GetProperty("translations")[0].GetProperty("text").GetString() ?? "", detected);
    }

    private async Task<(string ig, string iid, string key, string token, DateTime expires)> GetSessionAsync(CancellationToken ct)
    {
        if (_session is { } s && s.expires > DateTime.UtcNow) return s;
        string page = await _http.GetStringAsync("https://www.bing.com/translator", ct);
        var ig = Regex.Match(page, "IG:\"([A-F0-9]+)\"").Groups[1].Value;
        var iid = Regex.Match(page, "data-iid=\"(translator[^\"]*)\"").Groups[1].Value;
        var ab = Regex.Match(page, @"params_AbusePreventionHelper\s*=\s*\[(\d+),""([^""]+)"",(\d+)");
        if (ig == "" || !ab.Success) throw new HttpRequestException("Bing: couldn't start a session");
        var ttl = TimeSpan.FromMilliseconds(Math.Min(double.Parse(ab.Groups[3].Value), 3_000_000));
        _session = (ig, iid == "" ? "translator.5023" : iid, ab.Groups[1].Value, ab.Groups[2].Value, DateTime.UtcNow + ttl);
        return _session.Value;
    }
}

/// <summary>MyMemory: free without a key (about 5000 characters a day per IP). No auto-detect.</summary>
public sealed class MyMemoryTranslator : ITranslator
{
    private readonly HttpClient _http = Http.Create();
    public string Id => "mymemory";
    public string DisplayName => "MyMemory";

    private static string Code(string c) => c switch { "zh" => "zh-CN", _ => c };

    public async Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string? from, string to, CancellationToken ct)
    {
        var tasks = texts.Select(async t =>
        {
            string src = from ?? Languages.GuessFromScript(t);
            if (src == to) return new Translation(t, src);
            string q = t.Length > 450 ? t[..450] : t;
            string url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(q)}&langpair={Code(src)}|{Code(to)}";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            var status = doc.RootElement.GetProperty("responseStatus");
            if (status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
                throw new HttpRequestException("MyMemory: " + doc.RootElement.GetProperty("responseDetails").GetString());
            return new Translation(WebUtility.HtmlDecode(
                doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? ""), src);
        });
        return await Task.WhenAll(tasks);
    }
}

/// <summary>A local LLM served by Ollama (http://localhost:11434). Fully offline; uses the first installed model.</summary>
public sealed class OllamaTranslator : ITranslator
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(2) };
    private string? _model;
    public string Id => "ollama";
    public string DisplayName => "Ollama (local AI)";

    public async Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string? from, string to, CancellationToken ct)
    {
        _model ??= await PickModelAsync(ct);
        string target = Languages.Find(to)?.EnglishName ?? to;
        string source = from == null ? "" : $" from {Languages.Find(from)?.EnglishName ?? from}";
        var result = new List<Translation>();
        foreach (var text in texts)
        {
            var req = new
            {
                model = _model,
                stream = false,
                options = new { temperature = 0 },
                messages = new object[]
                {
                    new { role = "system", content = $"You are a translation engine. Translate the user's text{source} into {target}. " +
                        "The text was read by OCR from a screen and may contain small errors. Output only the translation, nothing else." },
                    new { role = "user", content = text },
                },
            };
            using var resp = await _http.PostAsync("/api/chat",
                new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json"), ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            string answer = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            answer = Regex.Replace(answer, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
            result.Add(new Translation(answer, from ?? Languages.GuessFromScript(text)));
        }
        return result.ToArray();
    }

    private async Task<string> PickModelAsync(CancellationToken ct)
    {
        string json;
        try { json = await _http.GetStringAsync("/api/tags", ct); }
        catch (HttpRequestException) { throw new InvalidOperationException(Strings.Get("OllamaMissing")); }
        using var doc = JsonDocument.Parse(json);
        var models = doc.RootElement.GetProperty("models");
        if (models.GetArrayLength() == 0) throw new InvalidOperationException(Strings.Get("OllamaNoModel"));
        return models[0].GetProperty("name").GetString()!;
    }
}

public static class TranslatorRegistry
{
    public static readonly IReadOnlyList<ITranslator> All =
        [new GoogleTranslator(), new BingTranslator(), new MyMemoryTranslator(), new OllamaTranslator()];

    public static ITranslator Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
