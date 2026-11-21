using System.IO;
using System.Text.Json;

namespace FlipClock;

/// <summary>What the widget remembers between starts. Stored in %APPDATA%\FlipClock\settings.json.</summary>
internal sealed class Settings
{
    public const double DefaultScale = 0.75;

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = DefaultScale;
    public bool Topmost { get; set; } = true;
    public bool Seconds { get; set; }

    /// <summary>"black" or "white".</summary>
    public string Style { get; set; } = "black";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipClock", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (double.IsNaN(s.Scale) || s.Scale < 0.2 || s.Scale > 6) s.Scale = DefaultScale;
                    return s;
                }
            }
        }
        catch { /* unreadable file: start with defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not being able to save must never crash the clock */ }
    }
}
