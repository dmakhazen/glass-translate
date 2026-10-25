using System.IO;

namespace GlassTranslate;

/// <summary>Diagnostics written to glass.log next to the exe when GLASS_LOG is set.</summary>
public static class Log
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("GLASS_LOG") != null;
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled) return;
        lock (Gate)
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "glass.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
    }
}
