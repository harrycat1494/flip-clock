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
    private SpacedText _weekday = null!, _date = null!;
    private PinButton _pin = null!;
    private Grip _grip = null!;
    private bool _started, _pinned;

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
        _pin = new PinButton { On = _pinned };
        _grip = new Grip();

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

        double x = M + Pad, y = M + HeadH;
        for (int i = 0; i < n; i++)
        {
            if (i > 0 && i % 2 == 0)
            {
                var colon = new ColonModule();
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
            x += Look.ModW + Gap;
        }

        Children.Add(_grip);
        SetLeft(_grip, M + hw - Grip.Box - 5.5);
        SetTop(_grip, M + hh - Grip.Box - 5.5);
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

    private void SetHeader(DateTime now)
    {
        _weekday.Text = now.ToString("dddd", En).ToUpperInvariant();
        _date.Text = now.ToString("dd MMM", En).ToUpperInvariant();
        PlaceDate();
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
