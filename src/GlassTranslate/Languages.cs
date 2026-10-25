namespace GlassTranslate;

/// <param name="Code">ISO 639-1 code used internally and by most engines.</param>
/// <param name="OcrGroup">PaddleOCR recognizer that reads this language's script.</param>
public sealed record Language(string Code, string NativeName, string EnglishName, string OcrGroup)
{
    public override string ToString() => NativeName;
}

public static class Languages
{
    public const string Auto = "auto";

    public static readonly IReadOnlyList<Language> All =
    [
        new("en", "English", "English", "english"),
        new("ru", "Русский", "Russian", "eslav"),
        new("th", "ไทย", "Thai", "thai"),
        new("zh", "中文", "Chinese", "chinese"),
        new("ja", "日本語", "Japanese", "chinese"),
        new("ko", "한국어", "Korean", "korean"),
        new("vi", "Tiếng Việt", "Vietnamese", "latin"),
        new("id", "Bahasa Indonesia", "Indonesian", "latin"),
        new("ms", "Bahasa Melayu", "Malay", "latin"),
        new("tl", "Filipino", "Filipino", "latin"),
        new("hi", "हिन्दी", "Hindi", "hindi"),
        new("ar", "العربية", "Arabic", "arabic"),
        new("tr", "Türkçe", "Turkish", "latin"),
        new("de", "Deutsch", "German", "latin"),
        new("fr", "Français", "French", "latin"),
        new("es", "Español", "Spanish", "latin"),
        new("pt", "Português", "Portuguese", "latin"),
        new("it", "Italiano", "Italian", "latin"),
        new("pl", "Polski", "Polish", "latin"),
        new("nl", "Nederlands", "Dutch", "latin"),
        new("uk", "Українська", "Ukrainian", "eslav"),
        new("el", "Ελληνικά", "Greek", "greek"),
    ];

    public static Language? Find(string? code) => All.FirstOrDefault(l => l.Code == code);

    /// <summary>Best-effort guess of the language from its script, for engines without auto-detect.</summary>
    public static string GuessFromScript(string text)
    {
        int thai = 0, cyr = 0, cjk = 0, kana = 0, hangul = 0, arabic = 0, deva = 0, greek = 0, latin = 0;
        foreach (char c in text)
        {
            if (c >= 0x0E00 && c <= 0x0E7F) thai++;
            else if (c >= 0x0400 && c <= 0x04FF) cyr++;
            else if (c >= 0x3040 && c <= 0x30FF) kana++;
            else if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
            else if (c >= 0xAC00 && c <= 0xD7AF) hangul++;
            else if (c >= 0x0600 && c <= 0x06FF) arabic++;
            else if (c >= 0x0900 && c <= 0x097F) deva++;
            else if (c >= 0x0370 && c <= 0x03FF) greek++;
            else if (char.IsAsciiLetter(c)) latin++;
        }
        var scores = new (int n, string code)[]
        {
            (thai, "th"), (cyr, "ru"), (kana, "ja"), (cjk, "zh"), (hangul, "ko"),
            (arabic, "ar"), (deva, "hi"), (greek, "el"), (latin, "en")
        };
        if (kana > 0) return "ja";
        var best = scores.MaxBy(s => s.n);
        return best.n == 0 ? "en" : best.code;
    }
}
