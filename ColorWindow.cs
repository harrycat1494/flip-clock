using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FlipClock;

/// <summary>Colours as hue (0-360), saturation and brightness (0-1), and as "#RRGGBB" text.</summary>
internal static class Hsv
{
    public static Color ToColor(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = ((int)(h / 60)) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(B(r + m), B(g + m), B(b + m));

        static byte B(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    /// <summary>A grey has no hue of its own: <paramref name="h"/> is then left as it was.</summary>
    public static void From(Color color, ref double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) return;
        double hue = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (hue * 60 + 360) % 360;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Reads "#RRGGBB", "RRGGBB" or the short "#RGB".</summary>
    public static bool TryHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().TrimStart('#');
        if (t.Length == 3) t = $"{t[0]}{t[0]}{t[1]}{t[1]}{t[2]}{t[2]}";
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}

/// <summary>The big field of the chooser: left to right more colour, top to bottom darker.</summary>
internal sealed class SvPad : FrameworkElement
{
    private static readonly Brush Darken = MakeDarken();
    private static readonly Pen Edge = Look.Stroke(Look.Solid(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Pen HandleLight = Look.Stroke(Brushes.White, 2);
    private static readonly Pen HandleDark = Look.Stroke(Look.Solid(Color.FromArgb(0x90, 0, 0, 0)), 1);
    private double _h, _s, _v;

    public event Action<double, double>? Picked;

    public SvPad(double width, double height)
    {
        Width = width;
        Height = height;
        Cursor = Cursors.Cross;
    }

    private static Brush MakeDarken()
    {
        var b = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
        b.Freeze();
        return b;
    }

    public void Set(double h, double s, double v)
    {
        _h = h;
        _s = s;
        _v = v;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(0, 0, Width, Height);
        dc.PushClip(new RectangleGeometry(box, 8, 8));
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, Hsv.ToColor(_h, 1, 1), 0), null, box);
        dc.DrawRectangle(Darken, null, box);
        dc.Pop();
        dc.DrawRoundedRectangle(null, Edge, Rect.Inflate(box, -0.5, -0.5), 7.5, 7.5);

        const double r = 7;
        var p = new Point(Math.Clamp(_s * Width, r + 1, Width - r - 1), Math.Clamp((1 - _v) * Height, r + 1, Height - r - 1));
        dc.DrawEllipse(Look.Solid(Hsv.ToColor(_h, _s, _v)), HandleLight, p, r, r);
        dc.DrawEllipse(null, HandleDark, p, r + 1.5, r + 1.5);
    }

    private void Pick(Point p) =>
        Picked?.Invoke(Math.Clamp(p.X / Width, 0, 1), 1 - Math.Clamp(p.Y / Height, 0, 1));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Pick(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();
}

/// <summary>The rainbow strip under the field.</summary>
internal sealed class HueBar : FrameworkElement
{
    private const double R = 9; // half the height: the handle is a circle as tall as the element
    private static readonly Pen HandleLight = Look.Stroke(Brushes.White, 2);
    private static readonly Pen HandleDark = Look.Stroke(Look.Solid(Color.FromArgb(0x90, 0, 0, 0)), 1);
    private readonly Brush _rainbow;
    private double _h;

    public event Action<double>? Picked;

    public HueBar(double width)
    {
        Width = width;
        Height = 2 * R;
        Cursor = Cursors.Hand;
        var b = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(R, 0),
            EndPoint = new Point(width - R, 0),
        };
        for (int i = 0; i <= 6; i++) b.GradientStops.Add(new GradientStop(Hsv.ToColor(i * 60, 1, 1), i / 6.0));
        b.Freeze();
        _rainbow = b;
    }

    public void Set(double h)
    {
        _h = h;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height)); // the whole height can be grabbed
        dc.DrawRoundedRectangle(_rainbow, null, new Rect(0, R - 6, Width, 12), 6, 6);
        var p = new Point(R + Math.Clamp(_h / 360, 0, 1) * (Width - 2 * R), R);
        dc.DrawEllipse(Look.Solid(Hsv.ToColor(_h, 1, 1)), HandleLight, p, R - 2, R - 2);
        dc.DrawEllipse(null, HandleDark, p, R - 0.5, R - 0.5);
    }

    private void Pick(Point p) => Picked?.Invoke(Math.Clamp((p.X - R) / (Width - 2 * R), 0, 1) * 360);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Pick(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();
}

/// <summary>The switch at the top of the chooser: which part of the clock is being coloured.</summary>
internal sealed class PartSwitch : FrameworkElement
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Track = Look.Solid(Color.FromRgb(0x12, 0x12, 0x14));
    private static readonly Brush Thumb = Look.Solid(Color.FromRgb(0x30, 0x30, 0x35));
    private static readonly Brush On = Look.Solid(Color.FromRgb(0xF3, 0xF0, 0xE8));
    private static readonly Brush Off = Look.Solid(Color.FromRgb(0x93, 0x90, 0x8A));
    private static readonly Brush OffHover = Look.Solid(Color.FromRgb(0xC9, 0xC6, 0xBF));
    private readonly string[] _labels;
    private int _selected, _hover = -1;

    public event Action<int>? Chosen;

    public PartSwitch(double width, params string[] labels)
    {
        _labels = labels;
        Width = width;
        Height = 32;
        Cursor = Cursors.Hand;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, Width, Height), 9, 9);
        double w = (Width - 6) / _labels.Length, dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = 0; i < _labels.Length; i++)
        {
            var cell = new Rect(3 + i * w, 3, w, Height - 6);
            if (i == _selected) dc.DrawRoundedRectangle(Thumb, null, cell, 6.5, 6.5);
            var ink = i == _selected ? On : i == _hover ? OffHover : Off;
            var ft = new FormattedText(_labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 12.5, ink, dip);
            dc.DrawText(ft, new Point(cell.X + (cell.Width - ft.Width) / 2, cell.Y + (cell.Height - ft.Height) / 2));
        }
    }

    private int At(Point p) => Math.Clamp((int)((p.X - 3) / ((Width - 6) / _labels.Length)), 0, _labels.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = At(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        int i = At(e.GetPosition(this));
        if (i == _selected) return;
        Selected = i;
        Chosen?.Invoke(i);
    }
}

/// <summary>The band of the last five colours. A click takes one of them again.</summary>
internal sealed class RecentBand : FrameworkElement
{
    private const int Slots = Settings.RecentColorsKept;
    private const double Gap = 6;
    private static readonly Pen Hairline = Look.Stroke(Look.Solid(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Pen Empty = Look.Stroke(Look.Solid(Color.FromRgb(0x2C, 0x2C, 0x30)), 1);
    private static readonly Pen Lit = Look.Stroke(Look.Solid(Color.FromRgb(0xF3, 0xF0, 0xE8)), 1.5);
    private readonly IReadOnlyList<Color> _colors;
    private int _hover = -1;

    public event Action<Color>? Picked;

    public RecentBand(double width, IReadOnlyList<Color> colors)
    {
        _colors = colors;
        Width = width;
        Height = 28;
    }

    private double SlotW => (Width - (Slots - 1) * Gap) / Slots;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
        for (int i = 0; i < Slots; i++)
        {
            var box = new Rect(i * (SlotW + Gap) + 0.5, 0.5, SlotW - 1, Height - 1);
            if (i < _colors.Count) dc.DrawRoundedRectangle(Look.Solid(_colors[i]), i == _hover ? Lit : Hairline, box, 7, 7);
            else dc.DrawRoundedRectangle(null, Empty, box, 7, 7);
        }
    }

    private int At(Point p)
    {
        int i = (int)(p.X / (SlotW + Gap));
        return i >= 0 && i < _colors.Count && p.X - i * (SlotW + Gap) <= SlotW ? i : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = At(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Arrow;
        ToolTip = i >= 0 ? Hsv.Hex(_colors[i]) : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        int i = At(e.GetPosition(this));
        if (i >= 0) Picked?.Invoke(_colors[i]);
    }
}

/// <summary>
/// The colour chooser: a small dark panel next to the clock. It colours the digits, the flaps or the frame
/// (switch at the top), freely (field and rainbow strip, or a typed "#RRGGBB"), or with one of the last five
/// colours. Reset gives a part back the colour that belongs to the look.
/// </summary>
internal sealed class ColorWindow : Window
{
    private const double W = 252, Rim = 18; // width of the content; see-through rim that holds the shadow

    /// <summary>One part of the clock that can be coloured, and what is chosen for it right now.</summary>
    private sealed class Part
    {
        public double H, S, V;
        public bool Own;      // true = the colour that belongs to the look, nothing chosen
        public Color OwnColor;
        public Color Now => Own ? OwnColor : Hsv.ToColor(H, S, V);

        public Part(Color? chosen, Color own)
        {
            OwnColor = own;
            Own = chosen == null;
            Hsv.From(chosen ?? own, ref H, out S, out V);
        }

        /// <summary>The look's colour for this part is another one now (digits follow the flaps).</summary>
        public void SetOwn(Color own)
        {
            OwnColor = own;
            if (Own) Hsv.From(own, ref H, out S, out V);
        }
    }

    private readonly Part _digits, _flaps, _frame;
    private readonly Part[] _parts;
    private readonly Func<Color?, Color> _ownInk;
    private Part _cur;
    private readonly PartSwitch _switch;
    private readonly SvPad _pad;
    private readonly HueBar _hue;
    private readonly Border _swatch;
    private readonly TextBox _hex;
    private readonly Button _reset;
    private bool _syncing;

    /// <summary>A colour is being tried: read <see cref="Digits"/>, <see cref="Flaps"/> and <see cref="Frame"/>.</summary>
    public event Action? Changed;

    /// <summary>True when the panel was left with Done.</summary>
    public bool Accepted { get; private set; }

    /// <summary>What is chosen for each part. Null = the colour that belongs to the look.</summary>
    public Color? Digits => _digits.Own ? null : _digits.Now;
    public Color? Flaps => _flaps.Own ? null : _flaps.Now;
    public Color? Frame => _frame.Own ? null : _frame.Now;

    /// <param name="ownInk">The look's digit colour for a given flap colour (null = the look's own flaps).</param>
    public ColorWindow(Color? digits, Color? flaps, Color? frame, Color ownFlap, Color ownFrame,
        Func<Color?, Color> ownInk, IReadOnlyList<Color> recent)
    {
        _ownInk = ownInk;
        _flaps = new Part(flaps, ownFlap);
        _frame = new Part(frame, ownFrame);
        _digits = new Part(digits, ownInk(flaps));
        _parts = new[] { _digits, _flaps, _frame };
        _cur = _digits;

        Title = "Flip Clock colors";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        _switch = new PartSwitch(W, "Digits", "Flaps", "Frame");
        _switch.Chosen += ShowPart;

        _pad = new SvPad(W, 148) { Margin = new Thickness(0, 14, 0, 0) };
        _pad.Picked += (s, v) => Choose(_cur.H, s, v);
        _hue = new HueBar(W) { Margin = new Thickness(0, 12, 0, 0) };
        _hue.Picked += h => Choose(h, _cur.S, _cur.V);

        _swatch = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Look.Solid(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
        };
        _hex = new TextBox
        {
            Style = (Style)Application.Current.FindResource("PanelTextBox"),
            Width = 98,
            MaxLength = 7,
            CharacterCasing = CharacterCasing.Upper,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Type or paste a color, for example #F2B233",
        };
        _hex.TextChanged += (_, _) =>
        {
            if (_syncing || !Hsv.TryHex(_hex.Text, out var typed)) return;
            Take(typed, keepText: true);
        };
        _hex.LostKeyboardFocus += (_, _) => ShowHex();
        _hex.GotKeyboardFocus += (_, _) => _hex.SelectAll();
        _reset = new Button
        {
            Style = (Style)Application.Current.FindResource("PanelButtonQuiet"),
            Content = "Reset",
            ToolTip = "Back to the clock's own color",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _reset.Click += (_, _) =>
        {
            _cur.Own = true;
            Hsv.From(_cur.OwnColor, ref _cur.H, out _cur.S, out _cur.V);
            Refresh();
            Notify();
        };
        var valueRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_swatch, Dock.Left);
        DockPanel.SetDock(_hex, Dock.Left);
        valueRow.Children.Add(_swatch);
        valueRow.Children.Add(_hex);
        valueRow.Children.Add(_reset);

        var label = new SpacedText(10.5, 2.2, Look.Solid(Color.FromRgb(0x8E, 0x8B, 0x85))) { Text = "LAST FIVE", CacheMode = null };
        label.Width = label.TextWidth;
        label.Height = label.CapHeight;
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.Margin = new Thickness(1, 18, 0, 0);
        var band = new RecentBand(W, recent) { Margin = new Thickness(0, 9, 0, 0) };
        band.Picked += c => Take(c, keepText: false);

        var cancel = new Button { Style = (Style)Application.Current.FindResource("PanelButton"), Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var done = new Button
        {
            Style = (Style)Application.Current.FindResource("PanelButtonPrimary"),
            Content = "Done",
            Margin = new Thickness(8, 0, 0, 0),
        };
        done.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(done);

        var stack = new StackPanel { Width = W };
        stack.Children.Add(_switch);
        stack.Children.Add(_pad);
        stack.Children.Add(_hue);
        stack.Children.Add(valueRow);
        stack.Children.Add(label);
        stack.Children.Add(band);
        stack.Children.Add(buttons);

        var panel = new Border
        {
            Margin = new Thickness(Rim),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Look.Solid(Color.FromRgb(0x1B, 0x1B, 0x1D)),
            BorderBrush = Look.Solid(Color.FromRgb(0x34, 0x34, 0x38)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.5, Color = Colors.Black },
            Child = stack,
        };
        panel.MouseLeftButtonDown += (_, _) =>
        {
            // the empty parts of the panel are its handle
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        Content = panel;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
            {
                e.Handled = true;
                Accept();
            }
        };
        Refresh();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // like the clock itself: not in Alt+Tab, no button on the taskbar
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));
    }

    /// <summary>Show the colour of one part: 0 digits, 1 flaps, 2 frame.</summary>
    internal void ShowPart(int part)
    {
        _cur = _parts[Math.Clamp(part, 0, _parts.Length - 1)];
        _switch.Selected = Array.IndexOf(_parts, _cur);
        Refresh();
    }

    /// <summary>
    /// Put the panel under the clock, its left edge in line with the clock's; above it if there is no room
    /// below. Both rectangles are in screen units; the panel never leaves the work area.
    /// </summary>
    public void PlaceNear(Rect clock, Rect work)
    {
        var root = (FrameworkElement)Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = root.DesiredSize.Width, h = root.DesiredSize.Height; // both with the see-through rim
        double left = clock.Left - Rim, top = clock.Bottom + 8 - Rim;
        if (top + h - Rim > work.Bottom) top = clock.Top - 8 - h + Rim;
        top = Math.Clamp(top, work.Top - Rim + 4, Math.Max(work.Top - Rim + 4, work.Bottom - h + Rim - 4));
        left = Math.Clamp(left, work.Left - Rim + 4, Math.Max(work.Left - Rim + 4, work.Right - w + Rim - 4));
        Left = left;
        Top = top;
    }

    private void Accept()
    {
        Accepted = true;
        Close();
    }

    private void Choose(double h, double s, double v)
    {
        _cur.H = h;
        _cur.S = s;
        _cur.V = v;
        _cur.Own = false;
        Refresh();
        Notify();
    }

    private void Take(Color c, bool keepText)
    {
        Hsv.From(c, ref _cur.H, out _cur.S, out _cur.V);
        _cur.Own = false;
        Refresh(keepText);
        Notify();
    }

    private void Notify()
    {
        if (_cur == _flaps) _digits.SetOwn(_ownInk(Flaps)); // digits without a chosen colour follow the flaps
        Changed?.Invoke();
    }

    private void Refresh(bool keepText = false)
    {
        _pad.Set(_cur.H, _cur.S, _cur.V);
        _hue.Set(_cur.H);
        _swatch.Background = Look.Solid(_cur.Now);
        _reset.IsEnabled = !_cur.Own;
        if (!keepText) ShowHex();
    }

    private void ShowHex()
    {
        _syncing = true;
        string text = Hsv.Hex(_cur.Now);
        if (_hex.Text != text) _hex.Text = text;
        _syncing = false;
    }
}
