using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Tray.Icons;

public static class UsageIconRenderer
{
    public static readonly Color CodexColor = Color.FromArgb(16, 163, 127);
    public static readonly Color GrokColor = Color.FromArgb(200, 80, 40);
    public static readonly Color AgyColor = Color.FromArgb(66, 133, 244);

    public static Icon Create(
        ProviderKind provider,
        int? remainingPercent,
        bool highContrast,
        int? dpiOverride = null,
        bool largerDigits = true)
    {
        using var bitmap = RenderBitmap(provider, remainingPercent, highContrast, dpiOverride, largerDigits);
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>Renders the tray glyph to a bitmap. Used by tests and icon creation.</summary>
    public static Bitmap RenderBitmap(
        ProviderKind provider,
        int? remainingPercent,
        bool highContrast,
        int? dpiOverride = null,
        bool largerDigits = true)
    {
        var dpi = dpiOverride ?? GetDpi();
        var logical = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        var size = Math.Max(32, logical * 2 * dpi / 96);
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        var accent = provider switch
        {
            ProviderKind.Codex => CodexColor,
            ProviderKind.Grok => GrokColor,
            ProviderKind.Agy => AgyColor,
            _ => Color.Gray
        };
        var fill = highContrast ? SystemColors.Window : Color.FromArgb(255, 18, 18, 20);
        var ink = highContrast ? SystemColors.WindowText : Color.White;
        if (highContrast)
        {
            accent = SystemColors.WindowText;
        }

        using (var background = new SolidBrush(fill))
        {
            g.FillRectangle(background, 0, 0, size, size);
        }

        using (var pen = new Pen(accent, 1f))
        {
            g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
        }

        var text = remainingPercent is null
            ? "—"
            : PercentageMath.DisplayPercent(remainingPercent.Value).ToString();
        // Asymmetric padding: keep underside clearance while allowing larger digits.
        var padX = largerDigits ? 2 : 3;
        var padTop = largerDigits ? 2 : 3;
        var padBottom = largerDigits ? 6 : 5;
        var box = new RectangleF(padX, padTop, size - padX * 2, size - padTop - padBottom);
        using var font = FitFont(g, text, box.Size, largerDigits);
        using var brush = new SolidBrush(ink);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var measured = g.MeasureString(text, font, new SizeF(short.MaxValue, short.MaxValue), format);
        var x = box.X + Math.Max(0f, (box.Width - measured.Width) / 2f);
        // Mild upward bias so larger glyphs still clear the bottom border.
        var y = box.Y + Math.Max(0f, (box.Height - measured.Height) * 0.30f);
        if (y + measured.Height > box.Bottom)
        {
            y = Math.Max(box.Y, box.Bottom - measured.Height);
        }

        g.DrawString(text, font, brush, x, y, format);
        return bitmap;
    }

    private static Font FitFont(Graphics g, string text, SizeF box, bool largerDigits)
    {
        var maxWidth = Math.Max(8f, box.Width * 0.94f);
        var maxHeight = Math.Max(8f, box.Height * 0.90f);
        // Larger tray digits, still capped so shell downscaling does not clip.
        var scale = text.Length switch
        {
            >= 3 => largerDigits ? 0.62f : 0.54f,
            2 => largerDigits ? 0.78f : 0.68f,
            _ => largerDigits ? 0.84f : 0.74f
        };
        var startPx = Math.Max(8, (int)Math.Floor(maxHeight * scale));
        var candidates = new (string Name, FontStyle Style)[]
        {
            ("Segoe UI Semibold", FontStyle.Regular),
            ("Segoe UI", FontStyle.Bold),
            ("Consolas", FontStyle.Bold),
            ("Cascadia Mono", FontStyle.Bold)
        };

        foreach (var (name, style) in candidates)
        {
            if (!FontFamily.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            for (var px = startPx; px >= 8; px--)
            {
                Font candidate;
                try
                {
                    candidate = new Font(name, px, style, GraphicsUnit.Pixel);
                }
                catch (ArgumentException)
                {
                    break;
                }

                var measured = g.MeasureString(
                    text,
                    candidate,
                    new SizeF(short.MaxValue, short.MaxValue),
                    StringFormat.GenericTypographic);
                if (measured.Width <= maxWidth && measured.Height <= maxHeight)
                {
                    return candidate;
                }

                candidate.Dispose();
            }
        }

        return new Font("Segoe UI", Math.Max(8, maxHeight * 0.55f), FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static int GetDpi()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)g.DpiX;
    }

    private static class NativeMethods
    {
        public const int SM_CXSMICON = 49;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
