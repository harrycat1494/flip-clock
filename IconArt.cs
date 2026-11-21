#if TOOLS
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlipClock;

/// <summary>
/// Draws the program icon from the same parts as the clock itself and writes it as an .ico file.
/// Run once with:  FlipClock.exe --icon Assets\flipclock.ico
/// </summary>
internal static class IconArt
{
    private const double Art = 160; // design size of the icon drawing

    private sealed class Body : FrameworkElement
    {
        private readonly Rect _slot;
        public Body(Rect slot) => _slot = slot;

        protected override void OnRender(DrawingContext dc)
        {
            var t = Look.T;
            var body = new Rect(6, 6, Art - 12, Art - 12);
            const double r = 34;
            dc.DrawRoundedRectangle(t.Body, null, body, r, r);
            dc.DrawRoundedRectangle(null, t.Bevel, Rect.Inflate(body, -1, -1), r - 1, r - 1);
            dc.DrawRoundedRectangle(null, t.Outline, body, r, r);
            dc.DrawRoundedRectangle(t.SlotFill, t.SlotEdge, _slot, 9, 9);
        }
    }

    /// <summary>Large sizes show two flaps ("12"); tiny sizes show one, so it stays readable at 16 px.</summary>
    private static Canvas Draw(bool single)
    {
        var c = new Canvas { Width = Art, Height = Art };
        string digits = single ? "2" : "12";
        double k = single ? 0.86 : 0.64, gap = 5;
        double w = Look.ModW * k, h = Look.ModH * k;
        double total = digits.Length * w + (digits.Length - 1) * gap;
        double x = (Art - total) / 2, y = (Art - h) / 2;

        c.Children.Add(new Body(new Rect(x - 4, y - 4, total + 8, h + 8)));
        foreach (char d in digits)
        {
            var m = new FlapModule { RenderTransform = new ScaleTransform(k, k) };
            m.SetInstant(d);
            c.Children.Add(m);
            Canvas.SetLeft(m, x);
            Canvas.SetTop(m, y);
            x += w + gap;
        }

        var size = new Size(Art, Art);
        c.Measure(size);
        c.Arrange(new Rect(size));
        c.UpdateLayout();
        return c;
    }

    private static byte[] Png(int px)
    {
        var art = Draw(single: px <= 32);
        double dpi = 96.0 * px / Art;
        var bmp = new RenderTargetBitmap(px, px, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(art);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static void Write(string icoPath)
    {
        Look.T = Theme.Black;
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = sizes.Select(Png).ToArray();

        string full = Path.GetFullPath(icoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (var fs = File.Create(full))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)images[i].Length);
                w.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }

        // loose pictures next to it, for looking at the result
        string dir = Path.GetDirectoryName(full)!;
        for (int i = 0; i < sizes.Length; i++)
            File.WriteAllBytes(Path.Combine(dir, $"icon-{sizes[i]}.png"), images[i]);
    }
}
#endif
