using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlipClock;

public partial class App : Application
{
    private const string MutexName = "FlipClock.SingleInstance.b61f";
    private const string EventName = "FlipClock.Activate.b61f";
    private const string ExitEventName = "FlipClock.MakeRoom.b61f";

    private Mutex? _mutex;
    private ClockWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if TOOLS
        // Picture-making helpers for the maker. They are not compiled into the program people download.
        int iconArg = Array.IndexOf(e.Args, "--icon");
        if (iconArg >= 0 && iconArg + 1 < e.Args.Length)
        {
            int code = 0;
            try { IconArt.Write(e.Args[iconArg + 1]); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            Shutdown(code);
            return;
        }

        if (Array.IndexOf(e.Args, "--frames") >= 0)
        {
            int code = 0;
            try { Frames(e.Args); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            Shutdown(code);
            return;
        }

        if (Array.IndexOf(e.Args, "--shot") >= 0)
        {
            int code = 0;
            try { Shot(e.Args); }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            Shutdown(code);
            return;
        }
#endif

        // Only one clock, ever.
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            if (!AnotherCopyIsRunning())
            {
                // the same program was started twice: just bring the running clock to the front
                try { EventWaitHandle.OpenExisting(EventName).Set(); } catch { }
                Shutdown();
                return;
            }

            // A different copy is running, usually the old version during an update.
            // Ask it to close and take its place, so there are never two.
            if (!TakeOver())
            {
                MessageBox.Show(
                    "An older Flip Clock is still running.\n\nRight-click it, choose Close, and start this one again.",
                    "Flip Clock", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        var wake = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        new Thread(() =>
        {
            while (true)
            {
                wake.WaitOne();
                Dispatcher.BeginInvoke(() => _window?.BringToFront());
            }
        }) { IsBackground = true }.Start();

        // a newer copy that is started later may ask this one to make room
        var leave = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        new Thread(() =>
        {
            leave.WaitOne();
            Dispatcher.BeginInvoke(() =>
            {
                Quitting = true;
                _window?.Close(); // saves place, size and settings
                Shutdown();
            });
        }) { IsBackground = true }.Start();

        OpenClock();
    }

    /// <summary>True once the clock is meant to end (Close in the menu, or a newer copy taking over).</summary>
    public static bool Quitting;

    private void OpenClock()
    {
        _window = new ClockWindow(Settings.Load());
        // If the window disappears although nobody closed the clock, open it again. This happens when the
        // clock lay on the taskbar and Windows Explorer restarted: Windows removes a taskbar's attached
        // windows together with it. The clock also does it on purpose when it leaves the taskbar
        // (see ClockWindow.UpdateTaskbarAnchor).
        _window.Closed += (_, _) =>
        {
            if (Quitting) Shutdown();
            else Dispatcher.BeginInvoke(OpenClock);
        };
        _window.Show();
    }

    /// <summary>Is a Flip Clock running from another file than this one?</summary>
    private static bool AnotherCopyIsRunning()
    {
        string? me = Environment.ProcessPath;
        if (me == null) return false;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(me)))
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                string? other = p.MainModule?.FileName;
                if (other != null && !string.Equals(other, me, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { /* cannot look at it: treat it as the same program */ }
            finally { p.Dispose(); }
        }
        return false;
    }

    /// <summary>Ask the running copy to close and wait until it has. False if it does not answer (version 1.0.0 cannot).</summary>
    private bool TakeOver()
    {
        try
        {
            EventWaitHandle.OpenExisting(ExitEventName).Set();
            try { return _mutex!.WaitOne(4000); }
            catch (AbandonedMutexException) { return true; } // it closed without tidying up: fine, it is gone
        }
        catch { return false; }
    }

#if TOOLS
    /// <summary>
    /// Test picture without opening a window:
    /// --shot file.png [--seconds] [--time HH:mm:ss] [--scale 2] [--hover] [--unpinned]
    /// [--pose index,from,to,angle] [--bg RRGGBB] [--font "Family;Weight;Stretch"]
    /// </summary>
    private static void Shot(string[] args)
    {
        string Arg(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        bool Has(string name) => Array.IndexOf(args, name) >= 0;

        string font = Arg("--font", "");
        if (font.Length > 0)
        {
            var p = font.Split(';');
            var weight = p.Length > 1 ? (FontWeight)new FontWeightConverter().ConvertFromInvariantString(p[1])! : FontWeights.SemiBold;
            var stretch = p.Length > 2 ? (FontStretch)new FontStretchConverter().ConvertFromInvariantString(p[2])! : FontStretches.Normal;
            Look.DigitFace = new Typeface(new FontFamily(p[0]), FontStyles.Normal, weight, stretch);
            Glyphs.Reset();
        }

        if (Has("--white")) Look.T = Theme.White;
        var face = new ClockFace(Has("--seconds")) { Pinned = !Has("--unpinned") };
        var time = DateTime.Today + TimeSpan.Parse(Arg("--time", "10:28:47"), CultureInfo.InvariantCulture);
        face.SetTimeInstant(time);
        if (Has("--hover")) face.SetHover(true, animate: false);

        string pose = Arg("--pose", "");
        if (pose.Length > 0)
        {
            var p = pose.Split(',');
            face.DebugPose(int.Parse(p[0]), p[1][0], p[2][0], double.Parse(p[3], CultureInfo.InvariantCulture));
        }

        double scale = double.Parse(Arg("--scale", "2"), CultureInfo.InvariantCulture);
        SavePicture(face, scale, Arg("--bg", "7C8794"), Arg("--shot", "shot.png"));
    }

    /// <summary>Paint the clock into a PNG. Background "none" keeps it see-through.</summary>
    private static void SavePicture(ClockFace face, double scale, string bgHex, string file)
    {
        var size = new Size(face.DesignW, face.DesignH);
        face.Measure(size);
        face.Arrange(new Rect(size));
        face.UpdateLayout();

        int pw = (int)Math.Ceiling(size.Width * scale), ph = (int)Math.Ceiling(size.Height * scale);
        pw += pw % 2; ph += ph % 2; // even sizes, so the frames can become a video
        var clock = new RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        clock.Render(face);

        BitmapSource final = clock;
        if (!string.Equals(bgHex, "none", StringComparison.OrdinalIgnoreCase))
        {
            var bg = (Color)ColorConverter.ConvertFromString("#" + bgHex);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, pw, ph));
                dc.DrawImage(clock, new Rect(0, 0, pw, ph));
            }
            var flat = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            flat.Render(dv);
            final = flat;
        }

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(final));
        string path = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// Picture sequence of the clock in motion, calculated frame by frame (no screen recording):
    /// the start-up shuffle to 10:28, then the minute turning to 10:29.
    /// --frames folder [--white] [--scale 2] [--bg RRGGBB] [--fps 30]
    /// </summary>
    private static void Frames(string[] args)
    {
        string Arg(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        if (Array.IndexOf(args, "--white") >= 0) Look.T = Theme.White;
        double scale = double.Parse(Arg("--scale", "2"), CultureInfo.InvariantCulture);
        int fps = int.Parse(Arg("--fps", "30"), CultureInfo.InvariantCulture);
        string bg = Arg("--bg", "D9D4CC"), dir = Arg("--frames", "frames");

        const string deck = " 0123456789";
        const double fastMs = 82, finalMs = 330, minuteAt = 2900, endAt = 4400;
        string first = "1028", second = "1029";
        var settle = new FallEase { Settle = true };
        var fast = new FallEase { Settle = false };

        var face = new ClockFace(false) { Pinned = true };
        face.SetTimeInstant(DateTime.Today.AddHours(10).AddMinutes(28));

        int total = (int)(endAt / 1000 * fps);
        for (int f = 0; f < total; f++)
        {
            double t = f * 1000.0 / fps;
            for (int i = 0; i < face.DigitCount; i++)
            {
                // start-up shuffle: blank -> ... -> first digit, one card after the other
                int steps = deck.IndexOf(first[i]);
                double start = 420 + i * 95, at = t - start;
                char shown = ' ';
                bool posed = false;
                if (at >= 0)
                {
                    for (int k = 1; k <= steps; k++)
                    {
                        double len = k == steps ? finalMs : fastMs;
                        if (at < len)
                        {
                            face.DebugPose(i, deck[k - 1], deck[k], 180 * (k == steps ? settle : fast).Ease(at / len));
                            posed = true;
                            break;
                        }
                        at -= len;
                        shown = deck[k];
                    }
                }

                // the minute turns
                if (!posed && first[i] != second[i] && t >= minuteAt)
                {
                    double m = t - minuteAt;
                    if (m < finalMs)
                    {
                        face.DebugPose(i, first[i], second[i], 180 * settle.Ease(m / finalMs));
                        posed = true;
                    }
                    else shown = second[i];
                }

                if (!posed) face.DebugInstant(i, shown);
            }
            SavePicture(face, scale, bg, Path.Combine(dir, $"f{f:000}.png"));
        }
    }
#endif
}
