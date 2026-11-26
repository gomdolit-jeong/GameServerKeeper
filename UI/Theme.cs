using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>색상·글꼴·도형 등 UI 공통 스타일</summary>
internal static class Theme
{
    // ── 기본 ──
    public static readonly Color Bg = Color.FromArgb(243, 244, 247);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 249, 251);
    public static readonly Color Border = Color.FromArgb(226, 229, 235);
    public static readonly Color Text = Color.FromArgb(24, 28, 35);
    public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);
    public static readonly Color TextFaint = Color.FromArgb(160, 166, 178);

    // ── 강조 ──
    public static readonly Color Accent = Color.FromArgb(79, 70, 229);
    public static readonly Color AccentHover = Color.FromArgb(67, 56, 202);
    public static readonly Color AccentDown = Color.FromArgb(55, 48, 163);
    public static readonly Color AccentSoft = Color.FromArgb(238, 240, 255);

    // ── 상태 ──
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color SuccessSoft = Color.FromArgb(220, 252, 231);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color WarningSoft = Color.FromArgb(254, 243, 199);
    public static readonly Color Info = Color.FromArgb(37, 99, 235);
    public static readonly Color InfoSoft = Color.FromArgb(219, 234, 254);
    public static readonly Color Neutral = Color.FromArgb(107, 114, 128);
    public static readonly Color NeutralSoft = Color.FromArgb(241, 243, 246);

    // ── 사이드바 / 로그 ──
    public static readonly Color Sidebar = Color.FromArgb(17, 24, 39);
    public static readonly Color SidebarHover = Color.FromArgb(31, 41, 55);
    public static readonly Color SidebarActive = Color.FromArgb(42, 52, 72);
    public static readonly Color SidebarText = Color.FromArgb(229, 231, 235);
    public static readonly Color SidebarMuted = Color.FromArgb(138, 148, 166);
    public static readonly Color LogBg = Color.FromArgb(17, 20, 27);

    private const string UiFontName = "Malgun Gothic";
    public static readonly string IconFontName =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private static readonly Dictionary<(string, float, FontStyle, GraphicsUnit), Font> FontCache = new();

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) => Get(UiFontName, size, style, GraphicsUnit.Point);
    public static Font IconFont(float size) => Get(IconFontName, size, FontStyle.Regular, GraphicsUnit.Point);
    public static Font IconFontPx(float px) => Get(IconFontName, px, FontStyle.Regular, GraphicsUnit.Pixel);

    private static Font Get(string name, float size, FontStyle style, GraphicsUnit unit)
    {
        lock (FontCache)
        {
            var key = (name, size, style, unit);
            if (!FontCache.TryGetValue(key, out var f))
                FontCache[key] = f = new Font(name, size, style, unit);
            return f;
        }
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static bool IsDark(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 128;

    public static (string Text, Color Fg, Color Bg) State(ServerState s) => s switch
    {
        ServerState.Running => ("실행 중", Success, SuccessSoft),
        ServerState.Starting => ("시작 중", Warning, WarningSoft),
        ServerState.Stopping => ("종료 중", Warning, WarningSoft),
        ServerState.Updating => ("업데이트 중", Info, InfoSoft),
        ServerState.BackingUp => ("백업 중", Info, InfoSoft),
        _ => ("중지됨", Neutral, Color.FromArgb(229, 231, 235))
    };

    public static ToolTip CreateToolTip() => new()
    {
        AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 100, ShowAlways = true
    };

    /// <summary>툴팁이 화면 끝까지 길어지지 않도록 단어 단위 줄바꿈</summary>
    public static string WrapTip(string text, int maxChars = 44)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxChars)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static void SetupGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }
}

/// <summary>Segoe Fluent Icons / MDL2 Assets 글리프</summary>
internal static class Glyphs
{
    public const string Play = "";
    public const string Stop = "";
    public const string Restart = "";
    public const string Save = "";
    public const string Download = "";
    public const string Announce = "";
    public const string Folder = "";
    public const string Settings = "";
    public const string Add = "";
    public const string Edit = "";
    public const string Delete = "";
    public const string Home = "";
    public const string Info = "";
    public const string People = "";
    public const string Clock = "";
    public const string Calendar = "";
    public const string History = "";
    public const string Globe = "";
    public const string Game = "";
    public const string Memory = "";
    public const string View = "";
    public const string Document = "";
    public const string Server = "";
    public const string Warning = "";
}

/// <summary>프로그램 아이콘 (코드로 그림 — 별도 리소스 파일 불필요)</summary>
internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon != null) return _icon;
        using var bmp = Render(64);
        _icon = Icon.FromHandle(bmp.GetHicon());
        return _icon;
    }

    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var rect = new RectangleF(0, 0, size - 1, size - 1);
        using (var path = Theme.RoundRect(rect, size * 0.24f))
        using (var brush = new LinearGradientBrush(rect, Theme.Accent, Color.FromArgb(147, 51, 234), 45f))
            g.FillPath(brush, path);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Glyphs.Game, Theme.IconFontPx(size * 0.5f), Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
        return bmp;
    }
}
