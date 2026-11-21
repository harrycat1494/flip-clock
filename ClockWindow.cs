using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FlipClock;

/// <summary>
/// The widget window: no frame, see-through around the housing.
/// Drag the clock to move it, drag an edge or corner to scale it, click the pin for always on top.
/// </summary>
internal sealed class ClockWindow : Window
{
    private const double MinScale = 0.3, MaxScale = 5;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "FlipClock";

    private readonly Settings _s;
    private readonly ClockFace _face;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _warmTimer = new(DispatcherPriority.Background);
    private MenuItem _miTop = null!, _miSeconds = null!, _miAutostart = null!, _miBlack = null!, _miWhite = null!;

    private IntPtr _hwnd;
    private double _scale;
    private bool _resizing, _fixingTop;
    private int _dirX, _dirY;
    private Native.POINT _startCursor;
    private Native.RECT _startRect;

    public ClockWindow(Settings settings)
    {
        _s = settings;
        _scale = Math.Clamp(_s.Scale, MinScale, MaxScale);

        Title = "Flip Clock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = _s.Topmost;

        Look.T = _s.Style == "white" ? Theme.White : Theme.Black;
        FlapClips.Enable(_s.Sound);
        _face = new ClockFace(_s.Seconds) { Pinned = _s.Topmost };
        Content = new Viewbox { Child = _face, Stretch = Stretch.Uniform };

        ApplyScale();
        PlaceOnScreen();
        KeepAutostartCurrent();
        ContextMenu = BuildMenu();

        _warmTimer.Tick += (_, _) =>
        {
            _warmTimer.Stop();
            FlapSound.Warm();
        };
        Loaded += (_, _) =>
        {
            TrimSoon(4000);
            if (!FlapSound.Enabled)
            {
                Tick();
                return;
            }
            // With sound: hold the start-up shuffle until the sound stream runs (at most 2 s),
            // so the first cards are heard and not swallowed.
            FlapSound.Warm();
            int waited = 0;
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            wait.Tick += (_, _) =>
            {
                waited += 30;
                if (!FlapSound.IsOpen && waited < 2000) return;
                wait.Stop();
                Tick();
            };
            wait.Start();
        };
        _timer.Tick += (_, _) => Tick();
        Closing += (_, _) => SaveState();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(KeepVisible);
        // after standby, or when Windows corrects the time, show the right time at once
        SystemEvents.TimeChanged += (_, _) => Dispatcher.BeginInvoke(Tick);
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(Tick);
        };
    }

    // ---------- time ----------

    private void Tick()
    {
        var now = DateTime.Now;
        FlapModule.FrameRate = _face.Seconds ? 30 : 60;
        _face.SetTime(now);
        KeepOnTop();
        // Sleep until the next change: the next full second with seconds shown, otherwise the next full minute.
        // Between changes the clock does nothing at all.
        double ms = _face.Seconds
            ? 1000 - now.Millisecond
            : 60000 - (now.Second * 1000 + now.Millisecond);
        _timer.Interval = TimeSpan.FromMilliseconds(ms + 12);
        _timer.Start();

        // With the sound on, wake the sound stream a moment before the next change, so the first clack
        // is on time. (With seconds shown the stream simply stays open between the seconds.)
        _warmTimer.Stop();
        if (FlapSound.Enabled && !_face.Seconds && ms > FlapSound.WarmUpMs + 200)
        {
            _warmTimer.Interval = TimeSpan.FromMilliseconds(ms + 12 - FlapSound.WarmUpMs);
            _warmTimer.Start();
        }
    }

    private void TrimSoon(int ms)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (!_resizing) Native.TrimMemory();
        };
        t.Start();
    }

    // ---------- size and place ----------

    private void ApplyScale()
    {
        Width = _face.DesignW * _scale;
        Height = _face.DesignH * _scale;
        SharpenCache();
    }

    /// <summary>Repaint the clock's stored pictures for the size it has now (see Look.Cache).</summary>
    private void SharpenCache()
    {
        double dpi = _hwnd != IntPtr.Zero ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1.5;
        Look.SetCacheScale(_scale * dpi);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Look.SetCacheScale(_scale * newDpi.DpiScaleX);
    }

    private void PlaceOnScreen()
    {
        if (_s.Left is double l && _s.Top is double t)
        {
            Left = l;
            Top = t;
            KeepVisible();
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 8;
            Top = wa.Top + 8;
        }
    }

    /// <summary>Never let the clock end up outside every screen (monitor unplugged, resolution changed).</summary>
    private void KeepVisible()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        const double keep = 80;
        if (Left + Width < vl + keep) Left = vl;
        if (Left > vr - keep) Left = vr - Width;
        if (Top + Height < vt + keep) Top = vt;
        if (Top > vb - keep) Top = vb - Height;
    }

    private void SaveState()
    {
        _s.Left = Left;
        _s.Top = Top;
        _s.Scale = _scale;
        _s.Topmost = Topmost;
        _s.Seconds = _face.Seconds;
        _s.Sound = FlapSound.Enabled;
        _s.Style = Look.T.Name;
        _s.Save();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // a widget does not belong in Alt+Tab
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));

        SharpenCache(); // now the real screen scaling is known

        // Windows can take "always on top" away from a window (for example when the desktop is shown).
        // Watch for that and take it back at once.
        HwndSource.FromHwnd(_hwnd)?.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGED && Topmost && !_fixingTop && !HasTopmostFlag())
            Dispatcher.BeginInvoke(KeepOnTop);
        return IntPtr.Zero;
    }

    private bool HasTopmostFlag() =>
        (Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST) != 0;

    /// <summary>
    /// If "always on top" is chosen, make sure it is really so: the flag is set and the clock is in front.
    /// Runs at every change of the display. Left alone while the menu is open or the mouse is on the clock,
    /// so it never jumps in front of its own menu or tooltip.
    /// </summary>
    private void KeepOnTop()
    {
        if (!Topmost || _hwnd == IntPtr.Zero || _resizing) return;
        if (ContextMenu is { IsOpen: true } || IsMouseOver) return;
        _fixingTop = true;
        try
        {
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        finally { _fixingTop = false; }
    }

    public void BringToFront()
    {
        Show();
        Activate();
        if (!Topmost)
        {
            Topmost = true;
            Topmost = false;
        }
    }

    // ---------- mouse: move, scale, pin ----------

    /// <summary>Which edge or corner of the housing the mouse is on: -1 / 0 / +1 per axis.</summary>
    private (int x, int y) Zone(Point p)
    {
        double k = ActualWidth / _face.DesignW;
        var h = _face.Housing;
        double l = h.Left * k, t = h.Top * k, r = h.Right * k, b = h.Bottom * k;
        double edge = Math.Clamp(7 * k + 3, 6, 12), corner = Math.Clamp(16 * k + 4, 14, 26);

        bool nearL = p.X < l + edge, nearR = p.X > r - edge, nearT = p.Y < t + edge, nearB = p.Y > b - edge;
        bool cornL = p.X < l + corner, cornR = p.X > r - corner, cornT = p.Y < t + corner, cornB = p.Y > b - corner;

        if (cornL && cornT) return (-1, -1);
        if (cornR && cornT) return (1, -1);
        if (cornL && cornB) return (-1, 1);
        if (cornR && cornB) return (1, 1);
        if (nearL) return (-1, 0);
        if (nearR) return (1, 0);
        if (nearT) return (0, -1);
        if (nearB) return (0, 1);
        return (0, 0);
    }

    private bool OverPin(MouseEventArgs e) => _face.PinRect.Contains(e.GetPosition(_face));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (OverPin(e))
        {
            SetTopmost(!Topmost);
            return;
        }

        var (zx, zy) = Zone(e.GetPosition(this));
        if (zx != 0 || zy != 0)
        {
            _dirX = zx;
            _dirY = zy;
            Native.GetCursorPos(out _startCursor);
            Native.GetWindowRect(_hwnd, out _startRect);
            _resizing = true;
            CaptureMouse();
            return;
        }

        try { DragMove(); } catch (InvalidOperationException) { }
        SaveState();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizing)
        {
            ResizeToCursor();
            return;
        }

        bool pin = OverPin(e);
        _face.SetPinHover(pin);
        if (pin)
        {
            Cursor = Cursors.Hand;
            return;
        }

        var (zx, zy) = Zone(e.GetPosition(this));
        Cursor = (zx, zy) switch
        {
            (0, 0) => Cursors.Arrow,
            (_, 0) => Cursors.SizeWE,
            (0, _) => Cursors.SizeNS,
            _ => zx * zy > 0 ? Cursors.SizeNWSE : Cursors.SizeNESW,
        };
    }

    /// <summary>Scale the whole clock while an edge or corner is dragged. The opposite side stays where it is.</summary>
    private void ResizeToCursor()
    {
        Native.GetCursorPos(out var c);
        double mx = c.X - _startCursor.X, my = c.Y - _startCursor.Y;
        double w0 = _startRect.Right - _startRect.Left, h0 = _startRect.Bottom - _startRect.Top;
        double aspect = w0 / h0;

        double byX = w0 + _dirX * mx, byY = (h0 + _dirY * my) * aspect, w;
        if (_dirX != 0 && _dirY != 0) w = Math.Abs(byX - w0) >= Math.Abs(byY - w0) ? byX : byY;
        else if (_dirX != 0) w = byX;
        else w = byY;

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        w = Math.Clamp(w, _face.DesignW * MinScale * dpi, _face.DesignW * MaxScale * dpi);
        double h = w / aspect;

        double x = _dirX < 0 ? _startRect.Right - w : _dirX == 0 ? _startRect.Left + (w0 - w) / 2 : _startRect.Left;
        double y = _dirY < 0 ? _startRect.Bottom - h : _dirY == 0 ? _startRect.Top + (h0 - h) / 2 : _startRect.Top;

        Native.SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y),
            (int)Math.Round(w), (int)Math.Round(h), Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_resizing) return;
        _resizing = false;
        ReleaseMouseCapture();
        _scale = Math.Clamp(ActualWidth / _face.DesignW, MinScale, MaxScale);
        SharpenCache(); // while dragging the stored pictures are only stretched; now make them sharp again
        SaveState();
        TrimSoon(1500);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_resizing) return;
        _resizing = false;
        _scale = Math.Clamp(ActualWidth / _face.DesignW, MinScale, MaxScale);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _face.SetHover(true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_resizing) return;
        _face.SetHover(false);
        _face.SetPinHover(false);
    }

    // ---------- options ----------

    private void SetTopmost(bool on)
    {
        Topmost = on;
        _face.Pinned = on;
        _miTop.IsChecked = on;
        SaveState();
    }

    private void SetSeconds(bool on)
    {
        _face.Build(on);
        _face.Pinned = Topmost;
        _face.SetTimeInstant(DateTime.Now);
        ApplyScale();
        KeepVisible();
        _miSeconds.IsChecked = on;
        SaveState();
        Tick(); // the wake-up rhythm changes between seconds and minutes
    }

    private void SetStyle(Theme theme)
    {
        Look.T = theme;
        _face.Build(_face.Seconds);
        _face.Pinned = Topmost;
        _face.SetTimeInstant(DateTime.Now);
        _miBlack.IsChecked = theme == Theme.Black;
        _miWhite.IsChecked = theme == Theme.White;
        SaveState();
    }

    private void ResetSize()
    {
        _scale = Settings.DefaultScale;
        ApplyScale();
        KeepVisible();
        SaveState();
    }

    private static bool AutostartOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        catch { return false; }
    }

    /// <summary>
    /// If "Start with Windows" is ticked but still names another file (the old copy, after an update
    /// into a different folder), point it at the program that is running now. Otherwise the old version
    /// would come back at the next sign-in. Does nothing if the option is not ticked.
    /// </summary>
    private static void KeepAutostartCurrent()
    {
        try
        {
            if (Environment.ProcessPath is not string exe) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(RunName) is not string entry) return;
            string wanted = $"\"{exe}\"";
            if (!string.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase)) key.SetValue(RunName, wanted);
        }
        catch { /* no access: leave it as it is */ }
    }

    private static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (on && Environment.ProcessPath is string exe) key.SetValue(RunName, $"\"{exe}\"");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
        catch { /* no access: leave it as it is */ }
    }

    private ContextMenu BuildMenu()
    {
        _miTop = new MenuItem { Header = "Always on top", IsChecked = Topmost };
        _miTop.Click += (_, _) => SetTopmost(!Topmost);

        _miSeconds = new MenuItem { Header = "Show seconds", IsChecked = _face.Seconds };
        _miSeconds.Click += (_, _) => SetSeconds(!_face.Seconds);

        _miAutostart = new MenuItem { Header = "Start with Windows", IsChecked = AutostartOn() };
        _miAutostart.Click += (_, _) =>
        {
            SetAutostart(!AutostartOn());
            _miAutostart.IsChecked = AutostartOn();
        };

        var sound = new MenuItem { Header = "Flap sound", IsChecked = FlapSound.Enabled };
        sound.Click += (_, _) =>
        {
            FlapClips.Enable(!FlapSound.Enabled);
            sound.IsChecked = FlapSound.Enabled;
            SaveState();
        };

        _miBlack = new MenuItem { Header = "Black", IsChecked = Look.T == Theme.Black };
        _miBlack.Click += (_, _) => SetStyle(Theme.Black);
        _miWhite = new MenuItem { Header = "White", IsChecked = Look.T == Theme.White };
        _miWhite.Click += (_, _) => SetStyle(Theme.White);

        var reset = new MenuItem { Header = "Reset size" };
        reset.Click += (_, _) => ResetSize();

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) =>
        {
            SaveState();
            Application.Current.Shutdown();
        };

        var menu = new ContextMenu();
        menu.Items.Add(_miTop);
        menu.Items.Add(_miSeconds);
        menu.Items.Add(sound);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miBlack);
        menu.Items.Add(_miWhite);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miAutostart);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Opened += (_, _) => _miAutostart.IsChecked = AutostartOn();
        return menu;
    }
}
