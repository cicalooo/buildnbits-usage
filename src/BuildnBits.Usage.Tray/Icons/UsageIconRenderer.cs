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

    public static Icon Create(
        ProviderKind provider,
        int? remainingPercent,
        bool highContrast,
        int? dpiOverride = null,
        bool largerDigits = true)
    {
        var dpi = dpiOverride ?? GetDpi();
        var logical = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        var size = Math.Max(32, logical * 2 * dpi / 96);
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.Clear(Color.Transparent);

            var accent = provider == ProviderKind.Codex ? CodexColor : GrokColor;
            var fill = highContrast ? SystemColors.Window : Color.FromArgb(255, 18, 18, 20);
            var ink = highContrast ? SystemColors.WindowText : Color.White;
            if (highContrast)
            {
                accent = SystemColors.WindowText;
            }

            g.FillRectangle(new SolidBrush(fill), 0, 0, size, size);
            using (var pen = new Pen(accent, 1f))
            {
                g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
            }

            var text = remainingPercent is null ? "—" : PercentageMath.DisplayPercent(remainingPercent.Value).ToString();
            var pad = largerDigits ? 1 : 3;
            var box = new Rectangle(pad, pad, size - pad * 2, size - pad * 2);
            using var font = FitFont(g, text, box.Size);
            TextRenderer.DrawText(
                g,
                text,
                font,
                box,
                ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

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

    private static Font FitFont(Graphics g, string text, Size box)
    {
        foreach (var name in new[] { "Segoe UI", "Consolas" })
        {
            if (!FontFamily.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            for (var px = box.Height; px >= 8; px--)
            {
                var candidate = new Font(name, px, FontStyle.Bold, GraphicsUnit.Pixel);
                var measured = TextRenderer.MeasureText(g, text, candidate, box, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (measured.Width <= box.Width && measured.Height <= box.Height)
                {
                    return candidate;
                }

                candidate.Dispose();
            }
        }

        return new Font("Segoe UI", Math.Max(10, box.Height * 0.7f), FontStyle.Bold, GraphicsUnit.Pixel);
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
