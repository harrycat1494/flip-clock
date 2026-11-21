using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FlipClock;

/// <summary>All measurements are in design units; the window scales the whole face as one vector drawing.</summary>
internal static class Look
{
    public const double ModW = 92, ModH = 132, ColonW = 30, Radius = 7, Split = 2.2, DigitInk = 78;

    public static Typeface DigitFace = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public static readonly Typeface LabelFace = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>The colours in use right now (black or white clock).</summary>
    public static Theme T
    {
        get => _t ??= Theme.Black;
        set => _t = value;
    }
    private static Theme? _t;

    /// <summary>Vertical gradient relative to whatever shape it fills.</summary>
    public static Brush Relative(params (Color c, double at)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        b.Freeze();
        return b;
    }

    public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    public static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static Brush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Vertical gradient in absolute design units, so both halves of a card share one light direction.</summary>
    public static Brush Vertical(double y0, double y1, params Color[] stops)
    {
        var b = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, y0),
            EndPoint = new Point(0, y1),
        };
        for (int i = 0; i < stops.Length; i++)
            b.GradientStops.Add(new GradientStop(stops[i], stops.Length == 1 ? 0 : (double)i / (stops.Length - 1)));
        b.Freeze();
        return b;
    }

    public static Pen Stroke(Brush brush, double thickness, bool round = false)
    {
        var p = new Pen(brush, thickness);
        if (round)
        {
            p.StartLineCap = p.EndLineCap = PenLineCap.Round;
            p.LineJoin = PenLineJoin.Round;
        }
        p.Freeze();
        return p;
    }
}

/// <summary>Every colour of the clock in one place. Two sets: black and white.</summary>
internal sealed class Theme
{
    public static readonly Theme Black = MakeBlack();
    public static readonly Theme White = MakeWhite();

    public string Name = "black";
    public double ShadeK = 1; // how strongly a turning flap darkens

    // flap cards
    public Brush Digit = null!, CardTop = null!, CardBottom = null!, LipLight = null!, Overhang = null!, TopFalloff = null!;
    public Pen EdgeLight = null!;
    public Brush HingeMetal = null!;
    public Pen HingeOutline = null!;

    // header line
    public Brush Weekday = null!, Date = null!, PinOnFill = null!, PinGlow = null!, GripDot = null!;
    public Pen PinOn = null!, PinOff = null!, PinOffHover = null!;

    // housing
    public Brush Body = null!, SlotFill = null!;
    public Pen Bevel = null!, Outline = null!, SlotEdge = null!;

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color A(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static Brush Glow(Color c, byte alpha)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        b.Freeze();
        return b;
    }

    private static Theme MakeBlack()
    {
        const double h = Look.ModH, half = h / 2;
        var amber = C(0xF2, 0xB2, 0x33);
        return new Theme
        {
            Name = "black",
            ShadeK = 1,
            Digit = Look.Solid(C(0xF3, 0xF0, 0xE8)),
            CardTop = Look.Vertical(0, half, C(0x2F, 0x2F, 0x32), C(0x23, 0x23, 0x26)),
            CardBottom = Look.Vertical(half, h, C(0x26, 0x26, 0x29), C(0x19, 0x19, 0x1B)),
            EdgeLight = Look.Stroke(Look.Solid(A(0x26, 0xFF, 0xFF, 0xFF)), 1),
            LipLight = Look.Solid(A(0x16, 0xFF, 0xFF, 0xFF)),
            Overhang = Look.Vertical(half, half + 9, A(0x66, 0, 0, 0), A(0x00, 0, 0, 0)),
            TopFalloff = Look.Vertical(half - 16, half, A(0x00, 0, 0, 0), A(0x1E, 0, 0, 0)),
            HingeMetal = Look.Vertical(half - 8, half + 8, C(0x5A, 0x5A, 0x5F), C(0x34, 0x34, 0x38), C(0x1C, 0x1C, 0x1F)),
            HingeOutline = Look.Stroke(Look.Solid(A(0xB0, 0, 0, 0)), 0.6),

            Weekday = Look.Solid(amber),
            Date = Look.Solid(A(0xA8, 0xF3, 0xF0, 0xE8)),
            PinOn = Look.Stroke(Look.Solid(amber), 2, round: true),
            PinOff = Look.Stroke(Look.Solid(C(0x74, 0x74, 0x7A)), 2, round: true),
            PinOffHover = Look.Stroke(Look.Solid(C(0xC4, 0xC2, 0xBC)), 2, round: true),
            PinOnFill = Look.Solid(A(0x48, 0xF2, 0xB2, 0x33)),
            PinGlow = Glow(amber, 0x46),
            GripDot = Look.Solid(A(0x8C, 0xFF, 0xFF, 0xFF)),

            Body = Look.Relative((C(0x20, 0x20, 0x22), 0), (C(0x16, 0x16, 0x18), 0.45), (C(0x0F, 0x0F, 0x10), 1)),
            Bevel = Look.Stroke(Look.Relative((A(0x3A, 0xFF, 0xFF, 0xFF), 0), (A(0x10, 0xFF, 0xFF, 0xFF), 0.18),
                (A(0x06, 0xFF, 0xFF, 0xFF), 0.8), (A(0x12, 0xFF, 0xFF, 0xFF), 1)), 1),
            Outline = Look.Stroke(Look.Solid(A(0xA0, 0, 0, 0)), 0.8),
            SlotFill = Look.Relative((C(0x03, 0x03, 0x04), 0), (C(0x0A, 0x0A, 0x0B), 1)),
            SlotEdge = Look.Stroke(Look.Relative((A(0xE6, 0, 0, 0), 0), (A(0x90, 0, 0, 0), 0.5), (A(0x24, 0xFF, 0xFF, 0xFF), 1)), 1.2),
        };
    }

    private static Theme MakeWhite()
    {
        const double h = Look.ModH, half = h / 2;
        var amber = C(0xB0, 0x6A, 0x08); // darker than on black, so it stays readable on white
        return new Theme
        {
            Name = "white",
            ShadeK = 0.55,
            Digit = Look.Solid(C(0x1A, 0x1A, 0x1C)),
            CardTop = Look.Vertical(0, half, C(0xFC, 0xFB, 0xF8), C(0xEE, 0xEC, 0xE7)),
            CardBottom = Look.Vertical(half, h, C(0xF5, 0xF3, 0xEE), C(0xE3, 0xE1, 0xDB)),
            EdgeLight = Look.Stroke(Look.Solid(A(0xE6, 0xFF, 0xFF, 0xFF)), 1),
            LipLight = Look.Solid(A(0xB0, 0xFF, 0xFF, 0xFF)),
            Overhang = Look.Vertical(half, half + 9, A(0x30, 0, 0, 0), A(0x00, 0, 0, 0)),
            TopFalloff = Look.Vertical(half - 16, half, A(0x00, 0, 0, 0), A(0x10, 0, 0, 0)),
            HingeMetal = Look.Vertical(half - 8, half + 8, C(0xE4, 0xE4, 0xE7), C(0xBE, 0xBE, 0xC3), C(0x92, 0x92, 0x98)),
            HingeOutline = Look.Stroke(Look.Solid(A(0x70, 0, 0, 0)), 0.6),

            Weekday = Look.Solid(amber),
            Date = Look.Solid(A(0xA6, 0x1A, 0x1A, 0x1C)),
            PinOn = Look.Stroke(Look.Solid(amber), 2, round: true),
            PinOff = Look.Stroke(Look.Solid(C(0x9C, 0x9A, 0x96)), 2, round: true),
            PinOffHover = Look.Stroke(Look.Solid(C(0x4A, 0x4A, 0x4E)), 2, round: true),
            PinOnFill = Look.Solid(A(0x40, 0xB0, 0x6A, 0x08)),
            PinGlow = Glow(C(0xF2, 0xB2, 0x33), 0x38),
            GripDot = Look.Solid(A(0x70, 0, 0, 0)),

            Body = Look.Relative((C(0xF8, 0xF7, 0xF3), 0), (C(0xEC, 0xEA, 0xE5), 0.45), (C(0xDD, 0xDB, 0xD5), 1)),
            Bevel = Look.Stroke(Look.Relative((A(0xFF, 0xFF, 0xFF, 0xFF), 0), (A(0x90, 0xFF, 0xFF, 0xFF), 0.2),
                (A(0x30, 0xFF, 0xFF, 0xFF), 0.8), (A(0x18, 0, 0, 0), 1)), 1),
            Outline = Look.Stroke(Look.Solid(A(0x38, 0, 0, 0)), 0.8),
            SlotFill = Look.Relative((C(0x5E, 0x5C, 0x58), 0), (C(0x7A, 0x78, 0x73), 1)),
            SlotEdge = Look.Stroke(Look.Relative((A(0x90, 0, 0, 0), 0), (A(0x50, 0, 0, 0), 0.5), (A(0xD0, 0xFF, 0xFF, 0xFF), 1)), 1.2),
        };
    }
}

/// <summary>Digit outlines, centred on a flap card. Built once per character and reused.</summary>
internal static class Glyphs
{
    private static readonly Dictionary<char, Geometry> Cache = new();
    private static double _em;
    private static Rect _zero;

    public static void Reset()
    {
        Cache.Clear();
        _em = 0;
    }

    private static Geometry Raw(char c, double size) =>
        new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Look.DigitFace, size, Brushes.White, 1.0).BuildGeometry(new Point(0, 0));

    public static Geometry Get(char c)
    {
        if (c == ' ') return Geometry.Empty;
        if (Cache.TryGetValue(c, out var cached)) return cached;

        if (_em <= 0)
        {
            // Size the font so the ink of "0" is exactly DigitInk tall, whatever the font.
            _em = 100 * Look.DigitInk / Raw('0', 100).Bounds.Height;
            _zero = Raw('0', _em).Bounds;
        }

        var g = Raw(c, _em);
        var b = g.Bounds;
        double dx = (Look.ModW - b.Width) / 2 - b.X;          // centre each digit by its ink
        double dy = (Look.ModH - _zero.Height) / 2 - _zero.Y; // one shared baseline for all digits
        g = g.Clone();
        g.Transform = new TranslateTransform(dx, dy);
        g.Freeze();
        Cache[c] = g;
        return g;
    }
}

/// <summary>One half of a flap card (above or below the split) showing half of a character.</summary>
internal sealed class HalfFace : FrameworkElement
{
    private readonly bool _top;
    private readonly double _w;
    private readonly bool _colon;
    private char _ch = ' ';

    public HalfFace(bool top, double width = Look.ModW, bool colon = false)
    {
        _top = top;
        _w = width;
        _colon = colon;
        Width = width;
        Height = Look.ModH;
        IsHitTestVisible = false;
    }

    public char Char
    {
        get => _ch;
        set
        {
            if (_ch == value) return;
            _ch = value;
            InvalidateVisual();
        }
    }

    public static Rect HalfRect(bool top, double w)
    {
        double half = Look.ModH / 2, s = Look.Split / 2;
        return top ? new Rect(0, 0, w, half - s) : new Rect(0, half + s, w, half - s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = Look.ModH, half = h / 2, s = Look.Split / 2, r = Look.Radius;
        var card = new Rect(0, 0, _w, h);
        var t = Look.T;

        dc.PushClip(new RectangleGeometry(HalfRect(_top, _w)));
        dc.DrawRoundedRectangle(_top ? t.CardTop : t.CardBottom, null, card, r, r);

        if (_colon)
        {
            double d = 9.5, cy = _top ? h * 0.345 : h * 0.655;
            dc.DrawRoundedRectangle(t.Digit, null, new Rect((_w - d) / 2, cy - d / 2, d, d), 2, 2);
        }
        else
        {
            dc.DrawGeometry(t.Digit, null, Glyphs.Get(_ch));
        }

        if (_top)
        {
            // the flap leans back a little: it loses light towards the hinge
            dc.DrawRectangle(t.TopFalloff, null, new Rect(0, half - 16, _w, 16));
            // light catching the top edge
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, _w, 3)));
            dc.DrawRoundedRectangle(null, t.EdgeLight, new Rect(0.5, 0.5, _w - 1, h - 1), r - 0.5, r - 0.5);
            dc.Pop();
        }
        else
        {
            // shadow of the upper flap falling on the lower one, then the lit lip of the lower flap
            dc.DrawRectangle(t.Overhang, null, new Rect(0, half, _w, 9 + s));
            dc.DrawRectangle(t.LipLight, null, new Rect(0, half + s, _w, 0.9));
        }

        dc.Pop();
    }
}

/// <summary>A black copy of a half card. Its opacity is the shading while a flap turns.</summary>
internal sealed class HalfShade : FrameworkElement
{
    private readonly bool _top;
    private readonly double _w;

    public HalfShade(bool top, double width = Look.ModW)
    {
        _top = top;
        _w = width;
        Width = width;
        Height = Look.ModH;
        Opacity = 0;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(HalfFace.HalfRect(_top, _w)));
        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(0, 0, _w, Look.ModH), Look.Radius, Look.Radius);
        dc.Pop();
    }
}

/// <summary>The two small axle clips left and right of the split.</summary>
internal sealed class HingePins : FrameworkElement
{
    public HingePins()
    {
        Width = Look.ModW;
        Height = Look.ModH;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double half = Look.ModH / 2;
        var t = Look.T;
        dc.DrawRoundedRectangle(t.HingeMetal, t.HingeOutline, new Rect(-2.4, half - 8, 6.4, 16), 1.6, 1.6);
        dc.DrawRoundedRectangle(t.HingeMetal, t.HingeOutline, new Rect(Look.ModW - 4, half - 8, 6.4, 16), 1.6, 1.6);
    }
}

/// <summary>A falling flap accelerates like a real one and settles with a tiny rebound.</summary>
internal sealed class FallEase : EasingFunctionBase
{
    public bool Settle { get; set; }

    public FallEase() => EasingMode = EasingMode.EaseIn;

    protected override double EaseInCore(double t)
    {
        if (!Settle) return Math.Pow(t, 1.5);
        const double land = 0.76;
        if (t < land) return Math.Pow(t / land, 2.2);
        return 1 - 0.05 * Math.Sin((t - land) / (1 - land) * Math.PI);
    }

    protected override Freezable CreateInstanceCore() => new FallEase();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        Settle = ((FallEase)source).Settle;
    }
}

/// <summary>
/// One split-flap module. Like the real thing it holds a fixed deck of cards (blank, 0-9)
/// and can only turn forward, one card at a time, until it reaches the wanted character.
/// </summary>
internal sealed class FlapModule : Canvas
{
    private const string Deck = " 0123456789";
    private const double FastMs = 82, FinalMs = 330;

    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(FlapModule),
        new PropertyMetadata(0.0, (d, e) => ((FlapModule)d).OnAngle((double)e.NewValue)));

    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    private readonly HalfFace _staticTop = new(true), _staticBottom = new(false);
    private readonly HalfFace _flapFront = new(true), _flapBack = new(false);
    private readonly HalfShade _shadeTop = new(true), _shadeBottom = new(false);
    private readonly HalfShade _shadeFront = new(true), _shadeBack = new(false);
    private readonly Canvas _front = new(), _back = new();
    private readonly ScaleTransform _frontT = new(1, 1, Look.ModW / 2, Look.ModH / 2);
    private readonly ScaleTransform _backT = new(1, 1, Look.ModW / 2, Look.ModH / 2);

    private char _current = ' ', _target = ' ', _next = ' ';
    private bool _flipping;

    public FlapModule()
    {
        Width = Look.ModW;
        Height = Look.ModH;

        _front.Children.Add(_flapFront);
        _front.Children.Add(_shadeFront);
        _front.RenderTransform = _frontT;
        _front.Visibility = Visibility.Hidden;

        _back.Children.Add(_flapBack);
        _back.Children.Add(_shadeBack);
        _back.RenderTransform = _backT;
        _back.Visibility = Visibility.Hidden;

        Children.Add(_staticTop);
        Children.Add(_shadeTop);
        Children.Add(_staticBottom);
        Children.Add(_shadeBottom);
        Children.Add(_front);
        Children.Add(_back);
        Children.Add(new HingePins());
    }

    /// <summary>Turn forward until the module shows <paramref name="c"/>.</summary>
    public void SetTarget(char c)
    {
        if (Deck.IndexOf(c) < 0) c = ' ';
        _target = c;
        if (!_flipping && _current != _target) StartFlip();
    }

    /// <summary>Show a character at once, without turning.</summary>
    public void SetInstant(char c)
    {
        if (Deck.IndexOf(c) < 0) c = ' ';
        BeginAnimation(AngleProperty, null);
        _flipping = false;
        _current = _target = c;
        Rest();
    }

    /// <summary>Freeze the module in the middle of a turn (used for test pictures).</summary>
    public void DebugPose(char from, char to, double angle)
    {
        _current = from;
        _next = to;
        _flipping = true;
        _flapFront.Char = from;
        _staticBottom.Char = from;
        _flapBack.Char = to;
        _staticTop.Char = to;
        Pose(angle);
    }

    private void Rest()
    {
        _staticTop.Char = _current;
        _staticBottom.Char = _current;
        _front.Visibility = Visibility.Hidden;
        _back.Visibility = Visibility.Hidden;
        _shadeTop.Opacity = 0;
        _shadeBottom.Opacity = 0;
    }

    private static int Steps(char from, char to)
    {
        int a = Deck.IndexOf(from), b = Deck.IndexOf(to);
        return ((b - a) % Deck.Length + Deck.Length) % Deck.Length;
    }

    private void StartFlip()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            // Windows "animation effects" are switched off: change without motion.
            SetInstant(_target);
            return;
        }

        _next = Deck[(Deck.IndexOf(_current) + 1) % Deck.Length];
        _flipping = true;

        _flapFront.Char = _current;   // old character, upper half, on the falling flap
        _staticBottom.Char = _current;
        _flapBack.Char = _next;       // new character, lower half, on the back of the falling flap
        _staticTop.Char = _next;      // new upper half, uncovered as the flap falls
        Pose(0);

        bool last = Steps(_next, _target) == 0;
        var anim = new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(last ? FinalMs : FastMs))
        {
            EasingFunction = new FallEase { Settle = last },
            FillBehavior = FillBehavior.HoldEnd,
        };
        anim.Completed += (_, _) => EndFlip();
        BeginAnimation(AngleProperty, anim);
    }

    private void EndFlip()
    {
        _flipping = false;
        BeginAnimation(AngleProperty, null);
        _current = _next;
        Rest();
        if (_current != _target) StartFlip();
    }

    private void OnAngle(double a)
    {
        if (_flipping) Pose(a);
    }

    private void Pose(double a)
    {
        a = Math.Clamp(a, 0, 180);
        double rad = a * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double widen = 1 + 0.03 * sin; // the flap edge comes towards the viewer
        double k = Look.T.ShadeK;

        if (a < 90)
        {
            _front.Visibility = Visibility.Visible;
            _back.Visibility = Visibility.Hidden;
            _frontT.ScaleY = Math.Max(cos, 0.0005);
            _frontT.ScaleX = widen;
            _shadeFront.Opacity = k * 0.55 * sin;
            _shadeTop.Opacity = k * 0.42 * (1 - a / 90);
            _shadeBottom.Opacity = k * 0.38 * (a / 90);
        }
        else
        {
            _front.Visibility = Visibility.Hidden;
            _back.Visibility = Visibility.Visible;
            _backT.ScaleY = Math.Max(-cos, 0.0005);
            _backT.ScaleX = widen;
            _shadeBack.Opacity = k * 0.5 * sin;
            _shadeTop.Opacity = 0;
            _shadeBottom.Opacity = k * 0.38;
        }
    }
}

/// <summary>The narrow card between hours and minutes with the two dots.</summary>
internal sealed class ColonModule : Canvas
{
    public ColonModule()
    {
        Width = Look.ColonW;
        Height = Look.ModH;
        Children.Add(new HalfFace(true, Look.ColonW, colon: true));
        Children.Add(new HalfFace(false, Look.ColonW, colon: true));
    }
}
