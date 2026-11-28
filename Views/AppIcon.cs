using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace GameServerKeeper.Views;

/// <summary>
/// 프로그램 아이콘 (코드로 그림 — 별도 리소스 파일 불필요).
/// 트레이 아이콘(NotifyIcon)이 System.Drawing.Icon 을 요구해서 GDI+ 로 그린 뒤 WPF 용으로도 변환한다.
/// </summary>
internal static class AppIcon
{
    private static Drawing.Icon? _icon;
    private static ImageSource? _image;

    public static Drawing.Icon Icon
    {
        get
        {
            if (_icon != null) return _icon;
            using var bmp = Render(64);
            _icon = Drawing.Icon.FromHandle(bmp.GetHicon());
            return _icon;
        }
    }

    public static ImageSource ImageSource
    {
        get
        {
            if (_image != null) return _image;
            var src = Imaging.CreateBitmapSourceFromHIcon(Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return _image = src;
        }
    }

    private static Drawing.Bitmap Render(int size)
    {
        var bmp = new Drawing.Bitmap(size, size);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var rect = new Drawing.RectangleF(0, 0, size - 1, size - 1);
        using (var path = RoundRect(rect, size * 0.24f))
        using (var brush = new Drawing2D.LinearGradientBrush(rect, Drawing.Color.FromArgb(79, 70, 229), Drawing.Color.FromArgb(147, 51, 234), 45f))
            g.FillPath(brush, path);

        var fontName = Drawing.FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        using var font = new Drawing.Font(fontName, size * 0.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel);
        using var sf = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        g.DrawString(ViewModels.Glyphs.Game, font, Drawing.Brushes.White, new Drawing.RectangleF(0, size * 0.03f, size, size), sf);
        return bmp;
    }

    private static Drawing2D.GraphicsPath RoundRect(Drawing.RectangleF r, float radius)
    {
        var path = new Drawing2D.GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
