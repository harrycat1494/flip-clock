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

    /// <summary>Cards only (no date line, bell, pin): chosen automatically when the clock is made very small.</summary>
    public bool Compact { get; set; }
    public bool Sound { get; set; } = true;

    /// <summary>Where and how big the clock was before "Put on the taskbar", so that it can go back there.</summary>
    public double? BackLeft { get; set; }
    public double? BackTop { get; set; }
    public double? BackScale { get; set; }
    public bool? BackCompact { get; set; }

    /// <summary>"flip" (every card), "hour" (only at the full hour) or "off". Missing in files from before 1.2.</summary>
    public string? SoundMode { get; set; }

    public bool AlarmOn { get; set; }
    public int AlarmHour { get; set; } = 7;
    public int AlarmMinute { get; set; }

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
                    s.SoundMode ??= s.Sound ? "flip" : "off"; // settings from 1.1 only had on/off
                    s.AlarmHour = Math.Clamp(s.AlarmHour, 0, 23);
                    s.AlarmMinute = Math.Clamp(s.AlarmMinute, 0, 59);
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
