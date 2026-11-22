using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlipClock;

/// <summary>Small caps label with letter spacing (WPF has no letter-spacing for text blocks).</summary>
internal sealed class SpacedText : FrameworkElement
{
    private readonly double _size, _spacing;
    private readonly Brush _brush;
    private string _text = "";
    private Geometry _geo = Geometry.Empty;

    public SpacedText(double size, double spacing, Brush brush)
    {
        _size = size;
        _spacing = spacing;
        _brush = brush;
        IsHitTestVisible = false;
        CacheMode = Look.Cache;
        CapHeight = Make("H").BuildGeometry(new Point(0, 0)).Bounds.Height;
    }

    public double TextWidth { get; private set; }
    public double CapHeight { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            Rebuild();
            InvalidateVisual();
        }
    }

    private FormattedText Make(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        Look.LabelFace, _size, Brushes.White, 1.0);

    private void Rebuild()
    {
        double capTop = Make("H").BuildGeometry(new Point(0, 0)).Bounds.Top;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        double x = 0;
        foreach (char c in _text)
        {
            var ft = Make(c.ToString());
            if (c != ' ') group.Children.Add(ft.BuildGeometry(new Point(x, -capTop)));
            x += ft.WidthIncludingTrailingWhitespace + _spacing;
        }
        TextWidth = Math.Max(0, x - _spacing);
        group.Freeze();
        _geo = group;
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawGeometry(_brush, null, _geo);
}

/// <summary>The pin in the header: lit amber and upright when the clock stays on top.</summary>
internal sealed class PinButton : FrameworkElement
{
    public const double Box = 26;

    // Pin outline after the Lucide "pin" icon (ISC licence), 24 x 24 grid.
    private static readonly Geometry Icon = Geometry.Parse(
        "M12,17 v5 M9,10.76 a2,2 0 0 1 -1.11,1.79 l-1.78,0.9 A2,2 0 0 0 5,15.24 V16 a1,1 0 0 0 1,1 h12 " +
        "a1,1 0 0 0 1,-1 v-0.76 a2,2 0 0 0 -1.11,-1.79 l-1.78,-0.9 A2,2 0 0 1 15,10.76 V7 a1,1 0 0 1 1,-1 " +
        "a2,2 0 0 0 0,-4 H8 a2,2 0 0 0 0,4 a1,1 0 0 1 1,1 z");

    private bool _on, _hover;

    public PinButton()
    {
        Width = Height = Box;
        ToolTip = "Always on top";
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public bool On
    {
        get => _on;
        set { _on = value; ToolTip = value ? "Always on top: on" : "Always on top: off"; InvalidateVisual(); }
    }

    public bool Hover
    {
        get => _hover;
        set { if (_hover != value) { _hover = value; InvalidateVisual(); } }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Look.T;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Box, Box)); // click area
        double c = Box / 2;
        if (_on) dc.DrawEllipse(t.PinGlow, null, new Point(c, c), 15, 15);

        const double k = 0.6;
        dc.PushTransform(new TranslateTransform(c, c));
        dc.PushTransform(new RotateTransform(_on ? 0 : 45));
        dc.PushTransform(new ScaleTransform(k, k));
        dc.PushTransform(new TranslateTransform(-12, -12));
        dc.DrawGeometry(_on ? t.PinOnFill : null, _on ? t.PinOn : _hover ? t.PinOffHover : t.PinOff, Icon);
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
}

/// <summary>The bell in the header: dim when the alarm is off, amber when it is set, glowing while it rings or is being set.</summary>
internal sealed class BellButton : FrameworkElement
{
    public const double Box = 26;

    // Bell outline after the Lucide "bell" icon (ISC licence), 24 x 24 grid.
    private static readonly Geometry Icon = Geometry.Parse(
        "M10.268,21 a2,2 0 0 0 3.464,0 M3.262,15.326 A1,1 0 0 0 4,17 h16 a1,1 0 0 0 0.74,-1.673 " +
        "C19.41,13.956 18,12.499 18,8 A6,6 0 0 0 6,8 c0,4.499 -1.411,5.956 -2.738,7.326");

    private bool _armed, _lit, _hover;

    public BellButton()
    {
        Width = Height = Box;
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public bool Armed { get => _armed; set { _armed = value; InvalidateVisual(); } }
    public bool Lit { get => _lit; set { if (_lit != value) { _lit = value; InvalidateVisual(); } } }
    public bool Hover { get => _hover; set { if (_hover != value) { _hover = value; InvalidateVisual(); } } }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Look.T;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Box, Box)); // click area
        double c = Box / 2;
        bool red = Look.AlarmFlash; // ringing: red with the digits
        if (red) dc.DrawEllipse(Look.AlarmGlow, null, new Point(c, c), 16, 16);
        else if (_lit) dc.DrawEllipse(t.PinGlow, null, new Point(c, c), 15, 15);

        const double k = 0.6;
        bool on = _armed || _lit;
        dc.PushTransform(new TranslateTransform(c, c));
        dc.PushTransform(new ScaleTransform(k, k));
        dc.PushTransform(new TranslateTransform(-12, -12));
        if (red) dc.DrawGeometry(Look.AlarmFill, Look.AlarmPen, Icon);
        else dc.DrawGeometry(on ? t.PinOnFill : null, on ? t.PinOn : _hover ? t.PinOffHover : t.PinOff, Icon);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}

/// <summary>A small labelled button in the header, shown while the alarm time is being set.</summary>
internal sealed class HeaderButton : FrameworkElement
{
    public const double H = 20;
    private readonly string _text;
    private readonly bool _primary;
    private bool _hover;

    public HeaderButton(string text, double width, bool primary)
    {
        _text = text;
        _primary = primary;
        Width = width;
        Height = H;
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public bool Hover { get => _hover; set { if (_hover != value) { _hover = value; InvalidateVisual(); } } }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Look.T;
        var box = new Rect(0.5, 0.5, Width - 1, H - 1);
        Brush ink;
        if (_primary)
        {
            // the one to press: filled amber, dark lettering
            dc.DrawRoundedRectangle(t.Weekday, null, box, 5, 5);
            if (_hover) dc.DrawRoundedRectangle(Look.Solid(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), null, box, 5, 5);
            ink = Look.Solid(Color.FromRgb(0x16, 0x14, 0x10));
        }
        else
        {
            var line = _hover ? t.PinOffHover.Brush : t.PinOff.Brush;
            dc.DrawRoundedRectangle(Brushes.Transparent, new Pen(line, 1), box, 5, 5);
            ink = _hover ? t.PinOffHover.Brush : t.Date;
        }

        var ft = new FormattedText(_text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Look.LabelFace, 10.5, ink, 1.0);
        var g = ft.BuildGeometry(new Point(0, 0));
        var b = g.Bounds;
        dc.PushTransform(new TranslateTransform((Width - b.Width) / 2 - b.X, (H - b.Height) / 2 - b.Y));
        dc.DrawGeometry(ink, null, g);
        dc.Pop();
    }
}

/// <summary>A small arrow above or below a card: "click here to go up / down". Shown while setting the alarm.</summary>
internal sealed class SetArrow : FrameworkElement
{
    private readonly bool _up;

    public SetArrow(bool up)
    {
        _up = up;
        Width = 14;
        Height = 8;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pen = Look.T.PinOn; // amber, round ends
        if (_up) { dc.DrawLine(pen, new Point(1, 7), new Point(7, 1)); dc.DrawLine(pen, new Point(7, 1), new Point(13, 7)); }
        else { dc.DrawLine(pen, new Point(1, 1), new Point(7, 7)); dc.DrawLine(pen, new Point(7, 7), new Point(13, 1)); }
    }
}

/// <summary>Six dots in the lower right corner: the handle for scaling. Shown while the mouse is over the clock.</summary>
internal sealed class Grip : FrameworkElement
{
    public const double Box = 12;

    public Grip()
    {
        Width = Height = Box;
        Opacity = 0;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double step = 4.4, r = 1.05;
        for (int row = 0; row < 3; row++)
            for (int col = 2 - row; col < 3; col++)
                dc.DrawEllipse(Look.T.GripDot, null, new Point(1.4 + col * step, 1.4 + row * step), r, r);
    }
}

/// <summary>The body of the clock: matt black housing with a recessed slot for the flap modules.</summary>
internal sealed class HousingVisual : FrameworkElement
{
    private static readonly Brush Grain = MakeGrain();
    private readonly Rect _body, _slot;

    public HousingVisual(Rect body, Rect slot)
    {
        _body = body;
        _slot = slot;
        IsHitTestVisible = false;
        CacheMode = Look.Cache;
    }

    /// <summary>Fine noise, like powder-coated metal. Very low contrast on purpose.</summary>
    private static Brush MakeGrain()
    {
        const int n = 128;
        var px = new byte[n * n * 4];
        var rnd = new Random(7);
        for (int i = 0; i < n * n; i++)
        {
            byte a = (byte)rnd.Next(0, 13);
            byte v = rnd.Next(2) == 0 ? (byte)0 : a; // premultiplied: black or white speck
            px[i * 4] = v; px[i * 4 + 1] = v; px[i * 4 + 2] = v; px[i * 4 + 3] = a;
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, n * 0.5, n * 0.5),
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double r = 16;
        var t = Look.T;

        // body
        dc.DrawRoundedRectangle(t.Body, null, _body, r, r);
        dc.DrawRoundedRectangle(Grain, null, _body, r, r);

        // bevel: light along the top edge, a faint return at the bottom
        var inner = Rect.Inflate(_body, -0.75, -0.75);
        dc.DrawRoundedRectangle(null, t.Bevel, inner, r - 0.75, r - 0.75);
        dc.DrawRoundedRectangle(null, t.Outline, Rect.Inflate(_body, 0.2, 0.2), r + 0.2, r + 0.2);

        // recessed slot: dark at the top lip, a thin light line at the bottom lip
        dc.DrawRoundedRectangle(t.SlotFill, t.SlotEdge, _slot, 11, 11);
    }
}

/// <summary>The whole clock as one scalable drawing: shadow, housing, header line, flap modules.</summary>
internal sealed class ClockFace : Canvas
{
    private const double M = 40;       // room around the housing for its shadow
    private const double Pad = 16;     // housing edge to modules
    private const double HeadH = 38;   // header line above the modules
    private const double Gap = 8;      // between modules

    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private readonly List<FlapModule> _digits = new();
    private SpacedText _weekday = null!, _date = null!, _alarmLabel = null!;
    private PinButton _pin = null!;
    private BellButton _bell = null!;
    private Grip _grip = null!;
    private readonly List<Rect> _digitRects = new();
    private readonly List<ColonModule> _colons = new();
    private readonly List<FrameworkElement> _setOnly = new(); // shown only while the alarm is being set
    private HeaderButton _done = null!, _cancel = null!;
    private bool _started, _pinned, _alarmOn, _setMode;
    private string _alarmText = "";

    public ClockFace(bool seconds) => Build(seconds);

    public bool Seconds { get; private set; }
    public double DesignW { get; private set; }
    public double DesignH { get; private set; }
    public Rect Housing { get; private set; }
    public Rect PinRect { get; private set; }

    public bool Pinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            _pin.On = value;
        }
    }

    /// <summary>(Re)build every part in the current colours. Called at start and when an option changes.</summary>
    public void Build(bool seconds)
    {
        Seconds = seconds;
        Children.Clear();
        _digits.Clear();
        _started = false;

        var theme = Look.T;
        _weekday = new SpacedText(12.5, 2.6, theme.Weekday);
        _date = new SpacedText(12.5, 2.6, theme.Date);
        _alarmLabel = new SpacedText(12.5, 2.2, theme.Weekday);
        _pin = new PinButton { On = _pinned };
        _bell = new BellButton { Armed = _alarmOn };
        _grip = new Grip();
        _digitRects.Clear();
        _colons.Clear();
        _setOnly.Clear();
        _setMode = false;

        int n = seconds ? 6 : 4, colons = seconds ? 2 : 1;
        double rowW = n * Look.ModW + colons * Look.ColonW + (n + colons - 1) * Gap;
        double hw = rowW + 2 * Pad, hh = HeadH + Look.ModH + Pad;

        DesignW = hw + 2 * M;
        DesignH = hh + 2 * M;
        Width = DesignW;
        Height = DesignH;
        Housing = new Rect(M, M, hw, hh);

        Children.Add(new Image
        {
            Source = ShadowImage(),
            Width = DesignW,
            Height = DesignH,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        });

        var slot = new Rect(M + Pad - 5, M + HeadH - 5, rowW + 10, Look.ModH + 10);
        Children.Add(new HousingVisual(Housing, slot));

        // header line: weekday in amber, date in soft white, pin on the right
        double headMid = M + 4 + (HeadH - 5) / 2;
        Children.Add(_weekday);
        Children.Add(_date);
        SetLeft(_weekday, M + Pad + 1);
        SetTop(_weekday, headMid - _weekday.CapHeight / 2);
        SetTop(_date, headMid - _date.CapHeight / 2);
        PlaceDate();

        Children.Add(_pin);
        PinRect = new Rect(M + hw - Pad - PinButton.Box + 5, headMid - PinButton.Box / 2, PinButton.Box, PinButton.Box);
        SetLeft(_pin, PinRect.X);
        SetTop(_pin, PinRect.Y);

        // the alarm bell sits left of the pin, the alarm time left of the bell
        Children.Add(_bell);
        BellRect = new Rect(PinRect.X - BellButton.Box + 2, PinRect.Y, BellButton.Box, BellButton.Box);
        SetLeft(_bell, BellRect.X);
        SetTop(_bell, BellRect.Y);
        Children.Add(_alarmLabel);
        SetTop(_alarmLabel, headMid - _alarmLabel.CapHeight / 2);
        PlaceAlarmLabel();

        double x = M + Pad, y = M + HeadH;
        for (int i = 0; i < n; i++)
        {
            if (i > 0 && i % 2 == 0)
            {
                var colon = new ColonModule();
                _colons.Add(colon);
                Children.Add(colon);
                SetLeft(colon, x);
                SetTop(colon, y);
                x += Look.ColonW + Gap;
            }
            var m = new FlapModule();
            Children.Add(m);
            SetLeft(m, x);
            SetTop(m, y);
            _digits.Add(m);
            _digitRects.Add(new Rect(x, y, Look.ModW, Look.ModH));
            x += Look.ModW + Gap;
        }

        // Shown only while the alarm time is being set: Done and Cancel where bell and pin normally are,
        // and an up and a down arrow on each of the four cards of the alarm time.
        _done = new HeaderButton("DONE", 50, primary: true);
        _cancel = new HeaderButton("CANCEL", 62, primary: false);
        DoneRect = new Rect(M + hw - Pad - _done.Width, headMid - HeaderButton.H / 2, _done.Width, HeaderButton.H);
        CancelRect = new Rect(DoneRect.X - 8 - _cancel.Width, DoneRect.Y, _cancel.Width, HeaderButton.H);
        Place(_done, DoneRect.X, DoneRect.Y);
        Place(_cancel, CancelRect.X, CancelRect.Y);
        for (int i = 0; i < 4 && i < _digitRects.Count; i++)
        {
            var r = _digitRects[i];
            Place(new SetArrow(up: true), r.X + r.Width / 2 - 7, r.Y + 8);
            Place(new SetArrow(up: false), r.X + r.Width / 2 - 7, r.Bottom - 16);
        }

        Children.Add(_grip);
        SetLeft(_grip, M + hw - Grip.Box - 5.5);
        SetTop(_grip, M + hh - Grip.Box - 5.5);

        void Place(FrameworkElement el, double left, double top)
        {
            el.Visibility = Visibility.Hidden;
            _setOnly.Add(el);
            Children.Add(el);
            SetLeft(el, left);
            SetTop(el, top);
        }
    }

    private static readonly Dictionary<bool, ImageSource> ShadowCache = new();

    /// <summary>
    /// The soft shadow is painted once into a picture and then only stretched.
    /// A live blur would be recalculated on every flip and every step of a resize.
    /// </summary>
    private ImageSource ShadowImage()
    {
        if (ShadowCache.TryGetValue(Seconds, out var cached)) return cached;

        var c = new Canvas { Width = DesignW, Height = DesignH };
        c.Children.Add(Shadow(blur: 30, depth: 0, opacity: 0.30));
        c.Children.Add(Shadow(blur: 16, depth: 8, opacity: 0.46));
        var size = new Size(DesignW, DesignH);
        c.Measure(size);
        c.Arrange(new Rect(size));
        c.UpdateLayout();

        const double k = 1.5;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(DesignW * k), (int)Math.Ceiling(DesignH * k),
            96 * k, 96 * k, PixelFormats.Pbgra32);
        bmp.Render(c);
        bmp.Freeze();
        ShadowCache[Seconds] = bmp;
        return bmp;
    }

    private FrameworkElement Shadow(double blur, double depth, double opacity)
    {
        var r = new System.Windows.Shapes.Rectangle
        {
            Width = Housing.Width - 6,
            Height = Housing.Height - 6,
            RadiusX = 14,
            RadiusY = 14,
            Fill = Brushes.Black,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Colors.Black },
        };
        SetLeft(r, Housing.X + 3);
        SetTop(r, Housing.Y + 3);
        return r;
    }

    private void PlaceDate() => SetLeft(_date, M + Pad + 1 + _weekday.TextWidth + 13);

    private void PlaceAlarmLabel()
    {
        _alarmLabel.Text = _alarmOn && !_setMode ? _alarmText : "";
        SetLeft(_alarmLabel, BellRect.X - 2 - _alarmLabel.TextWidth);
    }

    private void SetHeader(DateTime now)
    {
        if (_setMode)
        {
            _weekday.Text = "SET ALARM";
            _date.Text = "";
        }
        else
        {
            _weekday.Text = now.ToString("dddd", En).ToUpperInvariant();
            _date.Text = now.ToString("dd MMM", En).ToUpperInvariant();
        }
        PlaceDate();
    }

    // ---------- alarm ----------

    public Rect BellRect { get; private set; }

    /// <summary>Where digit number <paramref name="i"/> sits (0 and 1 are the hours, 2 and 3 the minutes).</summary>
    public Rect DigitRect(int i) => i >= 0 && i < _digitRects.Count ? _digitRects[i] : Rect.Empty;

    /// <summary>Show whether the alarm is set, and for what time (shown next to the bell).</summary>
    public void SetAlarm(bool on, int hour, int minute)
    {
        _alarmOn = on;
        _alarmText = $"{hour:00}:{minute:00}";
        _bell.Armed = on;
        _bell.ToolTip = on ? $"Alarm at {_alarmText}. Click to switch it off." : "Alarm off. Click to set it.";
        PlaceAlarmLabel();
    }

    public Rect DoneRect { get; private set; }
    public Rect CancelRect { get; private set; }

    public void SetBellHover(bool over) => _bell.Hover = over;

    public void SetSetButtonsHover(bool done, bool cancel)
    {
        _done.Hover = done;
        _cancel.Hover = cancel;
    }

    /// <summary>
    /// The alarm rings: digits, dots and bell turn red for the flash (<paramref name="red"/> true)
    /// and back to normal between flashes.
    /// </summary>
    public void SetAlarmFlash(bool red)
    {
        if (Look.AlarmFlash == red) return;
        Look.AlarmFlash = red;
        foreach (var d in _digits) d.Repaint();
        foreach (var c in _colons) c.Repaint();
        _bell.InvalidateVisual();
    }

    /// <summary>
    /// Switch between showing the time and showing the alarm time for setting.
    /// While setting, Done and Cancel take the place of bell and pin, and the cards get arrows.
    /// </summary>
    public void SetSetMode(bool on)
    {
        _setMode = on;
        SetHeader(DateTime.Now);
        PlaceAlarmLabel();
        var normal = on ? Visibility.Hidden : Visibility.Visible;
        _bell.Visibility = normal;
        _pin.Visibility = normal;
        foreach (var el in _setOnly) el.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>
    /// While setting: put the alarm time on the cards. Going up, the cards turn like on the real thing;
    /// going down they change at once, because a flap display can only turn forward.
    /// </summary>
    public void ShowAlarmDigits(int hour, int minute, bool turn)
    {
        _started = true;
        string t = $"{hour:00}{minute:00}";
        for (int i = 0; i < _digits.Count; i++)
        {
            char c = i < 4 ? t[i] : ' ';
            if (turn && i < 4) _digits[i].SetTarget(c); else _digits[i].SetInstant(c);
        }
    }

    /// <summary>Show a time. The first call after a build runs the board-style start-up shuffle.</summary>
    public void SetTime(DateTime now)
    {
        SetHeader(now);
        string t = now.ToString(Seconds ? "HHmmss" : "HHmm", CultureInfo.InvariantCulture);

        if (_started)
        {
            for (int i = 0; i < _digits.Count; i++) _digits[i].SetTarget(t[i]);
            return;
        }

        _started = true;
        for (int i = 0; i < _digits.Count; i++)
        {
            var module = _digits[i];
            char c = t[i];
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120 + i * 95) };
            delay.Tick += (_, _) => { delay.Stop(); module.SetTarget(c); };
            delay.Start();
        }
    }

    /// <summary>Show a time at once, no motion (used after switching the seconds on or off, and for test pictures).</summary>
    public void SetTimeInstant(DateTime now)
    {
        SetHeader(now);
        _started = true;
        string t = now.ToString(Seconds ? "HHmmss" : "HHmm", CultureInfo.InvariantCulture);
        for (int i = 0; i < _digits.Count; i++) _digits[i].SetInstant(t[i]);
    }

    public void DebugPose(int index, char from, char to, double angle)
    {
        if (index >= 0 && index < _digits.Count) _digits[index].DebugPose(from, to, angle);
    }

    public void DebugInstant(int index, char c)
    {
        if (index >= 0 && index < _digits.Count) _digits[index].SetInstant(c);
    }

    public int DigitCount => _digits.Count;

    public void SetHover(bool over, bool animate = true)
    {
        if (!animate)
        {
            _grip.BeginAnimation(OpacityProperty, null);
            _grip.Opacity = over ? 1 : 0;
            return;
        }
        _grip.BeginAnimation(OpacityProperty,
            new DoubleAnimation(over ? 1 : 0, TimeSpan.FromMilliseconds(over ? 160 : 260)));
    }

    public void SetPinHover(bool over) => _pin.Hover = over;
}
