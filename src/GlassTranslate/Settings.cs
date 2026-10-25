using System.IO;
using System.Text.Json;

namespace GlassTranslate;

/// <summary>Stored as settings.json next to the exe so the portable folder carries its own configuration.</summary>
public sealed class Settings
{
    public string UiLanguage { get; set; } = "en";
    public string SourceLanguage { get; set; } = Languages.Auto;
    public string TargetLanguage { get; set; } = "en";
    public string Engine { get; set; } = "google";
    /// <summary>Seconds between automatic re-reads; 0 means only on button press.</summary>
    public int IntervalSeconds { get; set; } = 2;
    public string FontFamily { get; set; } = "Segoe UI";
    /// <summary>0 means fit the text into the original block.</summary>
    public double FontSize { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>The onboarding tour runs once on first start.</summary>
    public bool TourShown { get; set; }
    public double Left { get; set; } = 200;
    public double Top { get; set; } = 150;
    public double Width { get; set; } = 900;
    public double Height { get; set; } = 480;

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { /* a broken file just resets to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception) { /* read-only location: settings simply won't persist */ }
    }
}
