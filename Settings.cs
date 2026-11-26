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

    /// <summary>12-hour clock with an AM/PM card. Off = 24-hour clock.</summary>
    public bool Hour12 { get; set; }

    /// <summary>
    /// Colours chosen for the digits, the flaps and the frame, as "#RRGGBB": one set for the black clock,
    /// one for the white clock. Missing = the colour that belongs to the look.
    /// </summary>
    public string? DigitColor { get; set; }
    public string? DigitColorWhite { get; set; }
    public string? FlapColor { get; set; }
    public string? FlapColorWhite { get; set; }
    public string? FrameColor { get; set; }
    public string? FrameColorWhite { get; set; }

    /// <summary>The last colours chosen, newest first, at most five.</summary>
    public List<string> RecentColors { get; set; } = new();
    public const int RecentColorsKept = 5;

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
                    s.RecentColors ??= new();
                    s.RecentColors.RemoveAll(c => !Hsv.TryHex(c, out _));
                    if (s.RecentColors.Count > RecentColorsKept)
                        s.RecentColors.RemoveRange(RecentColorsKept, s.RecentColors.Count - RecentColorsKept);
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
