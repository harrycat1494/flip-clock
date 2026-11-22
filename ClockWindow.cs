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
    private const double MinScale = 0.2, MaxScale = 5;

    // Below this size the date line, bell and pin are too small to read: the clock becomes "compact"
    // (cards only). It turns back into the full clock when it is made clearly bigger again.
    private const double CompactBelow = 0.34, FullAbove = 0.42;
    private (double scale, double left, double top)? _compactReturn;
    private readonly DispatcherTimer _guardTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
    private bool _hiddenForFullScreen;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "FlipClock";

    private readonly Settings _s;
    private readonly ClockFace _face;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _warmTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _hourOff = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private string _soundMode = "flip"; // "off", "flip" = every card, "hour" = only at the full hour
    private MenuItem _miSoundFlip = null!, _miSoundHour = null!, _miSoundOff = null!;
    private readonly DispatcherTimer _alarmWarmTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _ringTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _setIdle = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _flashOff = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _prevHour, _prevMinute;
    private MenuItem _miTaskbar = null!;
    private MenuItem _miTop = null!, _miSeconds = null!, _miAutostart = null!, _miBlack = null!, _miWhite = null!, _miAlarm = null!;
    private bool _setting, _ringing;
    private int _ringLeft;
    private string _lastRung = "";

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
        _soundMode = _s.SoundMode is "off" or "hour" ? _s.SoundMode : "flip";
        ApplySoundMode();
        _face = new ClockFace(_s.Seconds, _s.Compact) { Pinned = _s.Topmost };
        _guardTimer.Tick += (_, _) => GuardTopmost();
        if (_s.Topmost) _guardTimer.Start();
        _face.SetAlarm(_s.AlarmOn, _s.AlarmHour, _s.AlarmMinute);
        Content = new Viewbox { Child = _face, Stretch = Stretch.Uniform };

        _alarmWarmTimer.Tick += (_, _) =>
        {
            _alarmWarmTimer.Stop();
            AlarmSound.Warm();
        };
        _ringTimer.Tick += (_, _) => RingTick();
        _flashOff.Tick += (_, _) =>
        {
            _flashOff.Stop();
            _face.SetAlarmFlash(false);
        };
        _setIdle.Tick += (_, _) => CancelSet(); // nothing touched for 30 s: back to the clock, nothing changed

        ApplyScale();
        PlaceOnScreen();
        KeepAutostartCurrent();
        ContextMenu = BuildMenu();

        _warmTimer.Tick += (_, _) =>
        {
            _warmTimer.Stop();
            FlapSound.WarmAlways();
        };
        _hourOff.Tick += (_, _) =>
        {
            _hourOff.Stop();
            if (_soundMode == "hour") FlapSound.Enabled = false; // clacks already ordered still play
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
        Closing += (_, _) =>
        {
            SaveState();
            // Somebody closes the window (Alt+F4, or Windows asking politely): that ends the clock.
            // Only when the clock itself opens its window afresh, or when Windows removes the window
            // without asking (then this is never reached), does App open a new one.
            if (!_reopening) App.Quitting = true;
        };
        _onDisplay = (_, _) => Dispatcher.BeginInvoke(KeepVisible);
        // after standby, or when Windows corrects the time, show the right time at once
        _onTime = (_, _) => Dispatcher.BeginInvoke(Tick);
        _onPower = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(Tick);
        };
        SystemEvents.DisplaySettingsChanged += _onDisplay;
        SystemEvents.TimeChanged += _onTime;
        SystemEvents.PowerModeChanged += _onPower;
    }

    private readonly EventHandler _onDisplay, _onTime;
    private readonly PowerModeChangedEventHandler _onPower;
    private bool _closed, _reopening, _wasAnchored;

    /// <summary>
    /// The window is gone (the clock is closing, or it is being opened afresh, see UpdateTaskbarAnchor):
    /// stop everything that would otherwise keep ticking for a window that no longer exists.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _hwnd = IntPtr.Zero;
        foreach (var t in new[] { _timer, _guardTimer, _warmTimer, _alarmWarmTimer, _ringTimer, _flashOff, _setIdle, _hourOff })
            t.Stop();
        _taskbarWatch?.Stop();
        SystemEvents.DisplaySettingsChanged -= _onDisplay;
        SystemEvents.TimeChanged -= _onTime;
        SystemEvents.PowerModeChanged -= _onPower;
        base.OnClosed(e);
    }

    // ---------- time ----------

    private void Tick()
    {
        if (_closed) return;
        var now = DateTime.Now;
        FlapModule.FrameRate = _face.Seconds ? 30 : 60;
        if (_soundMode == "hour")
        {
            // only the cards that turn at the full hour are heard; switched off again a moment later
            bool fullHour = now.Minute == 0 && now.Second == 0;
            FlapSound.Enabled = fullHour;
            _hourOff.Stop();
            if (fullHour) _hourOff.Start();
        }
        if (!_setting) _face.SetTime(now); // while the alarm is being set the cards show the alarm time
        KeepOnTop();
        CheckAlarm(now);
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
        double toNextMinute = 60000 - (now.Second * 1000 + now.Millisecond);
        bool clackAtNextMinute = (_soundMode == "flip" && !_face.Seconds)
                                 || (_soundMode == "hour" && now.AddMinutes(1).Minute == 0);
        if (clackAtNextMinute)
        {
            _warmTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, toNextMinute - FlapSound.WarmUpMs));
            _warmTimer.Start();
        }

        // The same for the alarm, which must sound even with the flap sound off:
        // if it is due at the next full minute, wake the stream shortly before.
        _alarmWarmTimer.Stop();
        var next = now.AddMinutes(1);
        if (_s.AlarmOn && next.Hour == _s.AlarmHour && next.Minute == _s.AlarmMinute)
        {
            double toMinute = 60000 - (now.Second * 1000 + now.Millisecond);
            _alarmWarmTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, toMinute - FlapSound.WarmUpMs));
            _alarmWarmTimer.Start();
        }
    }

    // ---------- alarm ----------

    /// <summary>Ring when the alarm minute begins. A minute that passed while the PC slept is not made up for.</summary>
    private void CheckAlarm(DateTime now)
    {
        if (!_s.AlarmOn || _ringing || _setting) return;
        if (now.Hour != _s.AlarmHour || now.Minute != _s.AlarmMinute || now.Second > 2) return;
        string key = now.ToString("yyyyMMddHHmm");
        if (key == _lastRung) return;
        _lastRung = key;
        StartRinging();
    }

    private void StartRinging()
    {
        _ringing = true;
        _ringLeft = 60; // one minute, then it stops by itself: nobody may be there to switch it off
        RingOnce();
        _ringTimer.Start();
    }

    private void RingTick()
    {
        if (--_ringLeft <= 0)
        {
            StopRinging();
            return;
        }
        RingOnce();
    }

    /// <summary>One second of alarm: four beeps, and digits, dots and bell red for the first half second.</summary>
    private void RingOnce()
    {
        AlarmSound.RingOneSecond();
        _face.SetAlarmFlash(true);
        _flashOff.Stop();
        _flashOff.Start();
    }

    private void StopRinging()
    {
        _ringing = false;
        _ringTimer.Stop();
        _flashOff.Stop();
        _face.SetAlarmFlash(false);
        UpdateTaskbarAnchor(); // anything held back while it rang
    }

    /// <summary>Show the alarm time on the cards so it can be changed.</summary>
    private void EnterSetMode()
    {
        if (_ringing) StopRinging();
        if (!_setting)
        {
            // remember how it was, for Cancel
            _prevHour = _s.AlarmHour;
            _prevMinute = _s.AlarmMinute;
        }
        if (_face.Compact)
        {
            // The compact clock has no room for buttons and arrows: grow to the full form for setting,
            // and go back to exactly the same small clock afterwards.
            _compactReturn = (_scale, Left, Top);
            _scale = Math.Max(_scale, 0.7);
            SwitchLayout(compact: false);
            FitOnScreen();
        }
        _setting = true;
        _face.SetSetMode(true);
        _face.ShowAlarmDigits(_s.AlarmHour, _s.AlarmMinute, turn: true);
        _setIdle.Stop();
        _setIdle.Start();
    }

    /// <summary>After setting the alarm: if the clock was compact before, make it compact again, same size and place.</summary>
    private void LeaveSetLayout()
    {
        if (_compactReturn is not { } back) return;
        _compactReturn = null;
        _scale = back.scale;
        SwitchLayout(compact: true);
        Left = back.left;
        Top = back.top;
    }

    /// <summary>Back to the clock, alarm as it was before.</summary>
    private void CancelSet()
    {
        if (!_setting) return;
        _setIdle.Stop();
        _setting = false;
        _s.AlarmHour = _prevHour;
        _s.AlarmMinute = _prevMinute;
        _face.SetSetMode(false);
        LeaveSetLayout();
        _face.SetAlarm(_s.AlarmOn, _s.AlarmHour, _s.AlarmMinute);
        Tick();
        UpdateTaskbarAnchor();
    }

    /// <summary>Done setting: the alarm is on, the cards turn back to the time.</summary>
    private void ConfirmSet()
    {
        if (!_setting) return;
        _setIdle.Stop();
        _setting = false;
        _s.AlarmOn = true;
        _face.SetSetMode(false);
        LeaveSetLayout();
        _face.SetAlarm(true, _s.AlarmHour, _s.AlarmMinute);
        SaveState();
        Tick();
        UpdateTaskbarAnchor();
    }

    private void SetAlarmOn(bool on)
    {
        if (_setting)
        {
            _setIdle.Stop();
            _setting = false;
            _face.SetSetMode(false);
            LeaveSetLayout();
        }
        if (!on && _ringing) StopRinging();
        _s.AlarmOn = on;
        _face.SetAlarm(on, _s.AlarmHour, _s.AlarmMinute);
        SaveState();
        Tick();
    }

    /// <summary>Which digit of the alarm time is under the mouse: 0 and 1 hours, 2 and 3 minutes, -1 none.</summary>
    private int AlarmDigitAt(Point onFace)
    {
        for (int i = 0; i < 4; i++)
        {
            var r = _face.DigitRect(i);
            r.Inflate(4, 0);
            if (r.Contains(onFace)) return i;
        }
        return -1;
    }

    /// <summary>Change the alarm time: hours by one, minutes by one, the tens-of-minutes digit by ten.</summary>
    private void AdjustAlarm(int digit, int direction)
    {
        if (digit < 2) _s.AlarmHour = (_s.AlarmHour + direction + 24) % 24;
        else
        {
            int step = digit == 2 ? 10 : 1;
            _s.AlarmMinute = (_s.AlarmMinute + direction * step + 60) % 60;
        }
        _face.ShowAlarmDigits(_s.AlarmHour, _s.AlarmMinute, turn: direction > 0);
        _setIdle.Stop();
        _setIdle.Start();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_setting) return;
        int digit = AlarmDigitAt(e.GetPosition(_face));
        if (digit >= 0) AdjustAlarm(digit, e.Delta > 0 ? 1 : -1);
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
        if (_compactReturn == null) _s.Compact = _face.Compact; // not while it is only enlarged for setting the alarm
        _s.SoundMode = _soundMode;
        _s.Sound = _soundMode != "off"; // read by versions before 1.2
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
        Dispatcher.BeginInvoke(UpdateTaskbarAnchor); // it may have been left lying on the taskbar

        // Windows can take "always on top" away from a window (for example when the desktop is shown).
        // Watch for that and take it back at once.
        HwndSource.FromHwnd(_hwnd)?.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_WINDOWPOSCHANGED && Topmost && !_fixingTop && !HasTopmostFlag())
            Dispatcher.BeginInvoke(KeepOnTop);

        if (msg == Native.WM_MOVING && SnapToCentre(lParam))
        {
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// While the clock is dragged: when its middle comes close to the middle of the screen it is on,
    /// it catches there. Left-right and up-down are caught separately, so it can sit centred horizontally
    /// and still be slid freely up and down. Holding Ctrl places it freely.
    /// </summary>
    private bool SnapToCentre(IntPtr movingRect)
    {
        if ((Native.GetKeyState(Native.VK_CONTROL) & 0x8000) != 0) return false;

        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.RECT>(movingRect);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int cx = r.Left + w / 2, cy = r.Top + h / 2;
        int screenX = (info.rcMonitor.Left + info.rcMonitor.Right) / 2, screenY = (info.rcMonitor.Top + info.rcMonitor.Bottom) / 2;
        int reach = (int)(14 * VisualTreeHelper.GetDpi(this).DpiScaleX);

        bool caught = false;
        if (Math.Abs(cx - screenX) <= reach)
        {
            r.Left = screenX - w / 2;
            r.Right = r.Left + w;
            caught = true;
        }
        if (Math.Abs(cy - screenY) <= reach)
        {
            r.Top = screenY - h / 2;
            r.Bottom = r.Top + h;
            caught = true;
        }
        if (caught) System.Runtime.InteropServices.Marshal.StructureToPtr(r, movingRect, false);
        return caught;
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
            const uint keep = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
            // Only ever ask for "topmost". Never take the clock out of the topmost group, not even for a
            // moment: Windows would take a window's owner out with it, and on the taskbar that owner is
            // the taskbar itself.
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, keep);
        }
        finally { _fixingTop = false; }
    }

    // ---------- sitting on the taskbar ----------

    private IntPtr _anchoredTo = IntPtr.Zero;
    private DispatcherTimer? _taskbarWatch;

    /// <summary>
    /// When the taskbar is clicked, Windows lifts it into a layer above every normal program, and a clock
    /// lying on it would vanish until another window is clicked. The one thing Windows always keeps in front
    /// of a window is a window "owned" by it. So while the clock lies on a taskbar (and always-on-top is on),
    /// it is made that taskbar's owned window; as soon as it is moved away, it is released again.
    /// </summary>
    private void UpdateTaskbarAnchor()
    {
        if (_closed || _reopening || _hwnd == IntPtr.Zero) return;
        IntPtr want = Topmost ? TaskbarUnderClock() : IntPtr.Zero;
        _anchoredTo = want;

        // Left the taskbar after having been attached to it. When Start or Task View opens, Windows lifts the
        // taskbar into a layer above all normal programs and takes its attached windows along; the clock then
        // stays in that layer (measured on Windows 11, build 26300), and a program cannot leave it by itself.
        // That is what keeps it visible on the taskbar. Away from the taskbar it must be a normal window
        // again, so the window is opened afresh. Not while the alarm rings or is being set: done right after.
        if (want == IntPtr.Zero && _wasAnchored && !_ringing && !_setting)
        {
            _reopening = true;
            _taskbarWatch?.Stop();
            Dispatcher.BeginInvoke(Close); // App opens a new window with the saved place and size
            return;
        }
        // Only while it lies on a taskbar: look three times a second whether the taskbar has pushed in front
        // (Windows gives no signal for that), and come back. Anywhere else this watch does not run.
        if (want != IntPtr.Zero)
        {
            if (_taskbarWatch == null)
            {
                _taskbarWatch = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
                _taskbarWatch.Tick += (_, _) =>
                {
                    if (!_hiddenForFullScreen && CoveredAtCentre()) KeepOnTop();
                };
            }
            _taskbarWatch.Start();
        }
        else _taskbarWatch?.Stop();

        // Ask Windows who really owns the window, every time. Windows can refuse the request (seen while
        // Task View was open), and then only what was asked for would be remembered, not what is true.
        IntPtr real = Native.GetWindow(_hwnd, Native.GW_OWNER);
        if (real == want) return;
        if (want == IntPtr.Zero && real != _ownerWeSet) return; // an owner this clock did not set: not ours to remove
        var helper = new WindowInteropHelper(this);
        if (helper.Owner != want) helper.Owner = want; // through WPF, so that WPF does not put its own idea back
        if (Native.GetWindow(_hwnd, Native.GW_OWNER) != want)
            Native.SetWindowLongPtr(_hwnd, Native.GWLP_HWNDPARENT, want);
        _ownerWeSet = want;
        if (want != IntPtr.Zero && Native.GetWindow(_hwnd, Native.GW_OWNER) == want) _wasAnchored = true;
        if (Topmost) KeepOnTop();
    }

    private IntPtr _ownerWeSet = IntPtr.Zero;

    // ---------- "Put on the taskbar" in the menu ----------

    private const double BarFill = 0.75; // share of the taskbar's thickness that the clock takes
    private const double BarEdge = 16;   // distance from the end of the taskbar

    private bool OnTaskbar => _hwnd != IntPtr.Zero && TaskbarUnderClock() != IntPtr.Zero;

    /// <summary>
    /// Make the clock small (cards only) and lay it on the taskbar of the screen it is on, at the end where
    /// Windows shows its own clock. Where it was before is remembered for the way back.
    /// </summary>
    private void PutOnTaskbar()
    {
        if (_hwnd == IntPtr.Zero) return;
        IntPtr bar = TaskbarOfMyScreen();
        if (bar == IntPtr.Zero || !BarIsShowing(bar)) return; // no taskbar, or one that hides itself
        if (_setting) CancelSet();

        if (!OnTaskbar)
        {
            _s.BackLeft = Left;
            _s.BackTop = Top;
            _s.BackScale = _scale;
            _s.BackCompact = _face.Compact;
        }
        PlaceOnBar(bar);
        if (!Topmost) SetTopmost(true); // otherwise the taskbar would cover it at once
        // The taskbar may be on a screen with another scaling: measure once more after the move.
        Dispatcher.BeginInvoke(() =>
        {
            if (_closed) return;
            PlaceOnBar(bar);
            SaveState();
            UpdateTaskbarAnchor();
            TrimSoon(1500);
        }, DispatcherPriority.Background);
    }

    private void PlaceOnBar(IntPtr bar)
    {
        if (!Native.GetWindowRect(bar, out var b)) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double l = b.Left / dpi, t = b.Top / dpi, r = b.Right / dpi, bottom = b.Bottom / dpi;
        bool flat = r - l >= bottom - t; // the usual taskbar along the bottom (or top) edge
        if (!_face.Compact) SwitchLayout(compact: true);
        _scale = Math.Clamp(flat ? (bottom - t) * BarFill / _face.DesignH : (r - l) * 0.86 / _face.DesignW, MinScale, MaxScale);
        ApplyScale();
        if (flat)
        {
            Left = r - Width - BarEdge;
            Top = (t + bottom) / 2 - Height / 2;
        }
        else
        {
            Left = (l + r) / 2 - Width / 2;
            Top = bottom - Height - BarEdge;
        }
    }

    /// <summary>Back to where the clock was before "Put on the taskbar"; if that is not known, the normal clock top right.</summary>
    private void TakeOffTaskbar()
    {
        if (_setting) CancelSet();
        bool compact = _s.BackCompact ?? false;
        if (_face.Compact != compact) SwitchLayout(compact);
        _scale = _s.BackScale is double s ? Math.Clamp(s, MinScale, MaxScale) : Settings.DefaultScale;
        ApplyScale();
        if (_s.BackLeft is double left && _s.BackTop is double top)
        {
            Left = left;
            Top = top;
            KeepVisible();
        }
        else
        {
            FitOnScreen(); // stay on the screen it is on
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetWindowRect(_hwnd, out var c)
                && Native.GetMonitorInfo(Native.MonitorFromRect(ref c, Native.MONITOR_DEFAULTTONEAREST), ref info))
            {
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Left = info.rcWork.Right / dpi - Width - 8;
                Top = info.rcWork.Top / dpi + 8;
            }
        }
        _s.BackLeft = _s.BackTop = _s.BackScale = null;
        _s.BackCompact = null;
        SaveState();
        UpdateTaskbarAnchor();
        TrimSoon(1500);
    }

    /// <summary>The taskbar on the screen the clock is on; if that screen has none, the main one.</summary>
    private IntPtr TaskbarOfMyScreen()
    {
        if (!Native.GetWindowRect(_hwnd, out var c)) return IntPtr.Zero;
        IntPtr mine = Native.MonitorFromRect(ref c, Native.MONITOR_DEFAULTTONEAREST);
        IntPtr main = Native.FindWindowByClass("Shell_TrayWnd", IntPtr.Zero);
        if (OnMine(main)) return main;
        IntPtr other = IntPtr.Zero;
        for (int i = 0; i < 8; i++)
        {
            other = Native.FindWindowEx(IntPtr.Zero, other, "Shell_SecondaryTrayWnd", IntPtr.Zero);
            if (other == IntPtr.Zero) break;
            if (OnMine(other)) return other;
        }
        return main;

        bool OnMine(IntPtr bar) =>
            bar != IntPtr.Zero && Native.GetWindowRect(bar, out var b)
            && Native.MonitorFromRect(ref b, Native.MONITOR_DEFAULTTONEAREST) == mine;
    }

    /// <summary>False for a taskbar that is slid out of the screen (the "hide automatically" setting).</summary>
    private static bool BarIsShowing(IntPtr bar)
    {
        if (!Native.GetWindowRect(bar, out var b)) return false;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref b, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        const int slack = 4;
        return b.Left >= m.Left - slack && b.Top >= m.Top - slack && b.Right <= m.Right + slack && b.Bottom <= m.Bottom + slack;
    }

    /// <summary>The taskbar (main or on a second screen) that the middle of the clock lies on, if any.</summary>
    private IntPtr TaskbarUnderClock()
    {
        if (!Native.GetWindowRect(_hwnd, out var c)) return IntPtr.Zero;
        int cx = (c.Left + c.Right) / 2, cy = (c.Top + c.Bottom) / 2;

        IntPtr main = Native.FindWindowByClass("Shell_TrayWnd", IntPtr.Zero);
        if (Contains(main)) return main;
        IntPtr other = IntPtr.Zero;
        for (int i = 0; i < 8; i++)
        {
            other = Native.FindWindowEx(IntPtr.Zero, other, "Shell_SecondaryTrayWnd", IntPtr.Zero);
            if (other == IntPtr.Zero) break;
            if (Contains(other)) return other;
        }
        return IntPtr.Zero;

        bool Contains(IntPtr bar) =>
            bar != IntPtr.Zero && Native.GetWindowRect(bar, out var b)
            && cx >= b.Left && cx < b.Right && cy >= b.Top && cy < b.Bottom;
    }

    /// <summary>Is some other window in front of the middle of the clock?</summary>
    private bool CoveredAtCentre()
    {
        if (!Native.GetWindowRect(_hwnd, out var r)) return false;
        var p = new Native.POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        IntPtr top = Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT);
        return top != IntPtr.Zero && top != _hwnd;
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
        // a very small clock must keep a middle that can be grabbed for moving
        edge = Math.Min(edge, (b - t) * 0.16);
        corner = Math.Min(corner, (b - t) * 0.32);

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
    private bool OverBell(MouseEventArgs e) => _face.BellRect.Contains(e.GetPosition(_face));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_ringing)
        {
            StopRinging(); // any click on the clock silences the alarm; it stays set for tomorrow
            return;
        }

        if (!_setting && OverPin(e))
        {
            SetTopmost(!Topmost);
            return;
        }

        if (!_setting && OverBell(e))
        {
            // bell: off -> set the time; on -> off
            if (_s.AlarmOn) SetAlarmOn(false);
            else EnterSetMode();
            return;
        }

        if (_setting)
        {
            var p = e.GetPosition(_face);
            if (_face.DoneRect.Contains(p))
            {
                ConfirmSet();
                return;
            }
            if (_face.CancelRect.Contains(p))
            {
                CancelSet();
                return;
            }
            int digit = AlarmDigitAt(p);
            if (digit >= 0)
            {
                // upper half of a card: up, lower half: down
                var r = _face.DigitRect(digit);
                AdjustAlarm(digit, p.Y < r.Top + r.Height / 2 ? 1 : -1);
                return;
            }
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
        UpdateTaskbarAnchor(); // dropped onto a taskbar, or taken off one
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizing)
        {
            ResizeToCursor();
            return;
        }

        var onFace = e.GetPosition(_face);
        bool pin = !_setting && OverPin(e), bell = !_setting && OverBell(e);
        bool done = _setting && _face.DoneRect.Contains(onFace), cancel = _setting && _face.CancelRect.Contains(onFace);
        _face.SetPinHover(pin);
        _face.SetBellHover(bell);
        _face.SetSetButtonsHover(done, cancel);
        if (pin || bell || done || cancel || (_setting && AlarmDigitAt(onFace) >= 0))
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
        // made very small: cards only. Made clearly bigger again: the full clock.
        if (!_setting && !_face.Compact && _scale < CompactBelow) SwitchLayout(compact: true);
        else if (!_setting && _face.Compact && _scale > FullAbove) SwitchLayout(compact: false);
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
        _face.SetBellHover(false);
    }

    /// <summary>After the face was rebuilt (seconds, colour): put the alarm display back.</summary>
    private void RestoreAlarmDisplay()
    {
        _setIdle.Stop();
        _setting = false;
        _face.SetAlarm(_s.AlarmOn, _s.AlarmHour, _s.AlarmMinute);
    }

    // ---------- options ----------

    /// <summary>Flap sound: "flip" = every card, "hour" = only the cards that turn at the full hour, "off".</summary>
    private void ApplySoundMode()
    {
        if (_soundMode == "flip") FlapClips.Enable(true);
        else
        {
            FlapSound.Enabled = false;                   // "hour" switches it on for a moment at each full hour
            if (_soundMode == "hour") FlapClips.Prepare(); // have the clacks loaded by then
        }
    }

    private void SetSoundMode(string mode)
    {
        _soundMode = mode;
        ApplySoundMode();
        ShowSoundMode();
        SaveState();
        Tick(); // the wake-up of the sound stream depends on the mode
    }

    private void ShowSoundMode()
    {
        _miSoundFlip.IsChecked = _soundMode == "flip";
        _miSoundHour.IsChecked = _soundMode == "hour";
        _miSoundOff.IsChecked = _soundMode == "off";
    }

    private void SetTopmost(bool on)
    {
        // On the taskbar the clock only shows while it is "always on top": the taskbar would cover it.
        // So switching that off there first takes the clock back to where it was before.
        if (!on && OnTaskbar) TakeOffTaskbar();
        Topmost = on;
        _face.Pinned = on;
        _miTop.IsChecked = on;
        if (on) _guardTimer.Start();
        else
        {
            _guardTimer.Stop();
            ShowAfterFullScreen();
        }
        UpdateTaskbarAnchor();
        SaveState();
    }

    /// <summary>
    /// Only while "always on top" is chosen, every two seconds:
    /// - a full-screen program (film, game, presentation) is running: get out of its way;
    /// - otherwise make sure the clock is really in front. The taskbar is itself "always on top"
    ///   and pushes in front when it is clicked, which matters for a clock sitting on it.
    /// </summary>
    private void GuardTopmost()
    {
        if (!Topmost)
        {
            _guardTimer.Stop();
            return;
        }
        bool fullScreen = Native.SHQueryUserNotificationState(out int state) == 0
                          && state is Native.QUNS_BUSY or Native.QUNS_RUNNING_D3D_FULL_SCREEN or Native.QUNS_PRESENTATION_MODE;
        // A clock dragged as big as its screen is itself what Windows calls a full-screen program.
        // It must not hide from itself (it would never come back).
        if (fullScreen && !_ringing && !CoversItsScreen())
        {
            if (!_hiddenForFullScreen)
            {
                _hiddenForFullScreen = true;
                Opacity = 0; // fully see-through: clicks go through it too
            }
            return;
        }
        ShowAfterFullScreen();
        UpdateTaskbarAnchor();
        KeepOnTop();
    }

    private bool CoversItsScreen()
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return false;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private void ShowAfterFullScreen()
    {
        if (!_hiddenForFullScreen) return;
        _hiddenForFullScreen = false;
        Opacity = 1;
    }

    /// <summary>Change between the full clock and the compact one (cards only). The cards keep their size and their middle stays put.</summary>
    private void SwitchLayout(bool compact)
    {
        double cx = Left + Width / 2, cy = Top + Height / 2;
        _face.Build(_face.Seconds, compact);
        _face.Pinned = Topmost;
        _face.SetAlarm(_s.AlarmOn, _s.AlarmHour, _s.AlarmMinute);
        _face.SetTimeInstant(DateTime.Now);
        ApplyScale();
        Left = cx - Width / 2;
        Top = cy - Height / 2;
    }

    /// <summary>Move the window so that all of it is on the screen it is on (used when it grows for setting the alarm).</summary>
    private void FitOnScreen()
    {
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST), ref info)) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double l = info.rcWork.Left / dpi, t = info.rcWork.Top / dpi, rr = info.rcWork.Right / dpi, b = info.rcWork.Bottom / dpi;
        if (Left + Width > rr) Left = rr - Width;
        if (Top + Height > b) Top = b - Height;
        if (Left < l) Left = l;
        if (Top < t) Top = t;
    }

    private void SetSeconds(bool on)
    {
        _face.Build(on, _face.Compact);
        _face.Pinned = Topmost;
        RestoreAlarmDisplay();
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
        _face.Build(_face.Seconds, _face.Compact);
        _face.Pinned = Topmost;
        RestoreAlarmDisplay();
        _face.SetTimeInstant(DateTime.Now);
        _miBlack.IsChecked = theme == Theme.Black;
        _miWhite.IsChecked = theme == Theme.White;
        SaveState();
    }

    private void ResetSize()
    {
        _scale = Settings.DefaultScale;
        if (_face.Compact && !_setting) SwitchLayout(compact: false); // the normal size is the full clock
        else ApplyScale();
        FitOnScreen(); // it may have been lying on the taskbar
        KeepVisible();
        SaveState();
        UpdateTaskbarAnchor();
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

        _miSoundFlip = new MenuItem { Header = "Flap sound: every flip" };
        _miSoundFlip.Click += (_, _) => SetSoundMode("flip");
        _miSoundHour = new MenuItem { Header = "Flap sound: on the hour" };
        _miSoundHour.Click += (_, _) => SetSoundMode("hour");
        _miSoundOff = new MenuItem { Header = "Flap sound: off" };
        _miSoundOff.Click += (_, _) => SetSoundMode("off");
        ShowSoundMode();

        _miAlarm = new MenuItem { Header = "Alarm", IsChecked = _s.AlarmOn };
        _miAlarm.Click += (_, _) => SetAlarmOn(!_s.AlarmOn);
        var setAlarm = new MenuItem { Header = "Set alarm time" };
        setAlarm.Click += (_, _) => EnterSetMode();

        _miBlack = new MenuItem { Header = "Black", IsChecked = Look.T == Theme.Black };
        _miBlack.Click += (_, _) => SetStyle(Theme.Black);
        _miWhite = new MenuItem { Header = "White", IsChecked = Look.T == Theme.White };
        _miWhite.Click += (_, _) => SetStyle(Theme.White);

        _miTaskbar = new MenuItem { Header = "Put on the taskbar" };
        _miTaskbar.Click += (_, _) =>
        {
            if (OnTaskbar) TakeOffTaskbar();
            else PutOnTaskbar();
        };

        var reset = new MenuItem { Header = "Reset size" };
        reset.Click += (_, _) => ResetSize();

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) =>
        {
            SaveState();
            App.Quitting = true;
            Application.Current.Shutdown();
        };

        var menu = new ContextMenu();
        menu.Items.Add(_miTop);
        menu.Items.Add(_miTaskbar);
        menu.Items.Add(_miSeconds);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miSoundFlip);
        menu.Items.Add(_miSoundHour);
        menu.Items.Add(_miSoundOff);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miAlarm);
        menu.Items.Add(setAlarm);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miBlack);
        menu.Items.Add(_miWhite);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miAutostart);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Opened += (_, _) =>
        {
            _miAutostart.IsChecked = AutostartOn();
            _miTaskbar.IsChecked = OnTaskbar;
            _miAlarm.Header = $"Alarm at {_s.AlarmHour:00}:{_s.AlarmMinute:00}";
            _miAlarm.IsChecked = _s.AlarmOn;
        };
        return menu;
    }
}
