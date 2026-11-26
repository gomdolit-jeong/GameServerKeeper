using System.Drawing.Drawing2D;

namespace GameServerKeeper.UI;

public enum ButtonVariant { Primary, Secondary, Success, Danger, Ghost }

/// <summary>둥근 모서리 + 아이콘 + 호버 효과가 있는 버튼</summary>
public class ModernButton : Button
{
    private bool _hover, _down;
    private ButtonVariant _variant;
    private string? _glyph;

    public ModernButton(string text = "", string? glyph = null, ButtonVariant variant = ButtonVariant.Secondary)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Text = text;
        _glyph = glyph;
        _variant = variant;
        Font = Theme.Font(9.5f);
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 0, 8, 0);
        Padding = new Padding(14, 0, 14, 0);
    }

    public ButtonVariant Variant { get => _variant; set { _variant = value; Invalidate(); } }
    public string? Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
    /// <summary>높이 30 (기본 36)</summary>
    public bool Compact { get; set; }
    /// <summary>Ghost 버튼 글자색 지정</summary>
    public Color? TextColor { get; set; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int h = Compact ? 30 : 36;
        bool hasText = !string.IsNullOrEmpty(Text);
        if (!hasText) return new Size(h, h);
        int w = Padding.Horizontal + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        if (_glyph != null) w += 16 + 8;
        return new Size(w, h);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    private (Color Bg, Color Fg, Color Border) Palette()
    {
        var parent = Parent?.BackColor ?? Theme.Bg;
        bool darkParent = Theme.IsDark(parent);
        if (!Enabled)
            return _variant == ButtonVariant.Ghost
                ? (parent, darkParent ? Theme.SidebarMuted : Theme.TextFaint, Color.Empty)
                : (Theme.NeutralSoft, Theme.TextFaint, Theme.Border);

        Color Shade(Color c) => _down ? Theme.Blend(c, Color.Black, 0.22) : _hover ? Theme.Blend(c, Color.Black, 0.1) : c;
        return _variant switch
        {
            ButtonVariant.Primary => (_down ? Theme.AccentDown : _hover ? Theme.AccentHover : Theme.Accent, Color.White, Color.Empty),
            ButtonVariant.Success => (Shade(Theme.Success), Color.White, Color.Empty),
            ButtonVariant.Danger => (Shade(Theme.Danger), Color.White, Color.Empty),
            ButtonVariant.Ghost => (
                _down ? Theme.Blend(parent, darkParent ? Color.White : Color.Black, 0.12)
                : _hover ? Theme.Blend(parent, darkParent ? Color.White : Color.Black, 0.06) : parent,
                TextColor ?? (darkParent ? Theme.SidebarText : Theme.Text), Color.Empty),
            _ => (_down ? Theme.NeutralSoft : _hover ? Theme.SurfaceAlt : Theme.Surface, Theme.Text,
                  _hover ? Theme.Blend(Theme.Border, Color.Black, 0.12) : Theme.Border)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.SetupGraphics(g);

        var (bg, fg, border) = Palette();
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundRect(rect, 7))
        {
            using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var path = Theme.RoundRect(RectangleF.Inflate(rect, -2, -2), 5);
            using var p = new Pen(Color.FromArgb(140, Theme.Accent), 1.5f);
            g.DrawPath(p, path);
        }

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        bool hasText = !string.IsNullOrEmpty(Text);
        int textW = hasText ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
        int glyphW = _glyph != null ? 16 : 0;
        int gap = _glyph != null && hasText ? 8 : 0;
        int x = (Width - (glyphW + gap + textW)) / 2;

        if (_glyph != null)
            TextRenderer.DrawText(g, _glyph, Theme.IconFont(10.5f), new Rectangle(x, 0, glyphW, Height), fg, flags | TextFormatFlags.HorizontalCenter);
        if (hasText)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphW + gap, 0, textW + 4, Height), fg, flags);
    }
}

/// <summary>켜짐/꺼짐 스위치 (CheckBox 대체)</summary>
public class ToggleSwitch : CheckBox
{
    private bool _hover;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        Font = Theme.Font(9.5f);
        Height = 28;
        Fit();
    }

    /// <summary>지정하면 켜짐/꺼짐 대신 이 글자를 표시</summary>
    public string? Label { get => _label; set { _label = value; Fit(); Invalidate(); } }
    private string? _label;

    private string Caption => _label ?? (Checked ? "켜짐" : "꺼짐");

    private void Fit()
    {
        var widest = _label ?? "켜짐";
        Width = 46 + 10 + TextRenderer.MeasureText(widest, Font, Size.Empty, TextFormatFlags.NoPadding).Width + 6;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.SetupGraphics(g);

        var track = new RectangleF(1, (Height - 24) / 2f, 44, 24);
        Color trackColor = !Enabled ? Theme.NeutralSoft
            : Checked ? (_hover ? Theme.AccentHover : Theme.Accent)
            : (_hover ? Color.FromArgb(190, 196, 206) : Color.FromArgb(209, 213, 219));
        using (var path = Theme.RoundRect(track, 12))
        using (var b = new SolidBrush(trackColor))
            g.FillPath(b, path);

        float knob = 18;
        float kx = Checked ? track.Right - knob - 3 : track.X + 3;
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            g.FillEllipse(shadow, kx, track.Y + 4, knob, knob);
        g.FillEllipse(Brushes.White, kx, track.Y + 3, knob, knob);

        var fg = !Enabled ? Theme.TextFaint : _label != null ? Theme.Text : Checked ? Theme.Accent : Theme.TextMuted;
        TextRenderer.DrawText(g, Caption, Font, new Rectangle(56, 0, Width - 56, Height), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>둥근 모서리 흰색 카드</summary>
public class Card : Panel
{
    private Color _fill = Theme.Surface;

    public Card()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Surface;
        Padding = new Padding(16);
    }

    public Color FillColor { get => _fill; set { _fill = value; BackColor = value; Invalidate(); } }
    public Color BorderColor { get; set; } = Theme.Border;
    public int Radius { get; set; } = 12;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.SetupGraphics(g);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.RoundRect(rect, Radius);
        using (var b = new SolidBrush(_fill)) g.FillPath(b, path);
        if (BorderColor != Color.Empty) using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
    }
}

/// <summary>아이콘 + 제목 + 큰 값 + 보조 문구 + (선택) 진행 막대가 있는 상태 타일</summary>
public class StatTile : Control
{
    private string _value = "-";
    private string? _sub;
    private Color _valueColor = Theme.Text;
    private double? _progress;
    private Color _progressColor = Theme.Accent;

    public StatTile(string caption, string glyph, Color accent)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Caption = caption;
        Glyph = glyph;
        Accent = accent;
        Height = 96;
        Margin = new Padding(0, 0, 12, 0);
    }

    public string Caption { get; }
    public string Glyph { get; }
    public Color Accent { get; }
    /// <summary>좁은 타일용: 아이콘을 제목 옆에 작게 두고 값을 넓게 표시</summary>
    public bool Compact { get; set; }

    public void Set(string value, string? sub = null, Color? valueColor = null, double? progress = null, Color? progressColor = null)
    {
        var vc = valueColor ?? Theme.Text;
        var pc = progressColor ?? Accent;
        if (value == _value && sub == _sub && vc == _valueColor && progress == _progress && pc == _progressColor) return;
        _value = value; _sub = sub; _valueColor = vc; _progress = progress; _progressColor = pc;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.SetupGraphics(g);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundRect(rect, 12))
        {
            g.FillPath(Brushes.White, path);
            using var p = new Pen(Theme.Border);
            g.DrawPath(p, path);
        }

        var ell = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (Compact)
        {
            // 작은 아이콘 + 제목 한 줄, 아래에 값을 타일 폭 전체로
            var small = new RectangleF(14, 12, 24, 24);
            using (var b = new SolidBrush(Theme.Blend(Accent, Color.White, 0.86))) g.FillEllipse(b, small);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(9f), Rectangle.Round(small), Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int cw = Width - 28;
            TextRenderer.DrawText(g, Caption, Theme.Font(8.75f), new Rectangle(44, 12, cw - 30, 24), Theme.TextMuted, ell | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _value, Theme.Font(13f, FontStyle.Bold), new Rectangle(14, 40, cw, 26), _valueColor, ell);
            if (_sub != null)
                TextRenderer.DrawText(g, _sub, Theme.Font(8.25f), new Rectangle(14, 66, cw, 16), Theme.TextMuted, ell);
        }
        else
        {
            var circle = new RectangleF(16, 16, 36, 36);
            using (var b = new SolidBrush(Theme.Blend(Accent, Color.White, 0.86))) g.FillEllipse(b, circle);
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(12f), Rectangle.Round(circle), Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int x = 64, w = Width - x - 12;
            TextRenderer.DrawText(g, Caption, Theme.Font(9f), new Rectangle(x, 14, w, 18), Theme.TextMuted, ell);
            TextRenderer.DrawText(g, _value, Theme.Font(13.5f, FontStyle.Bold), new Rectangle(x, 32, w, 28), _valueColor, ell);
            if (_sub != null)
                TextRenderer.DrawText(g, _sub, Theme.Font(8.5f), new Rectangle(x, 60, w, 18), Theme.TextMuted, ell);
        }

        if (_progress is double pr)
        {
            var bar = Compact ? new RectangleF(14, Height - 11, Width - 28, 5) : new RectangleF(16, Height - 14, Width - 32, 6);
            using (var path = Theme.RoundRect(bar, 3))
            using (var b = new SolidBrush(Theme.NeutralSoft))
                g.FillPath(b, path);
            var fill = bar with { Width = (float)(bar.Width * Math.Clamp(pr, 0, 1)) };
            if (fill.Width >= 6)
            {
                using var path = Theme.RoundRect(fill, 3);
                using var b = new SolidBrush(_progressColor);
                g.FillPath(b, path);
            }
        }
    }
}

/// <summary>● 실행 중 처럼 점 + 글자가 있는 둥근 상태 배지</summary>
public class StatusPill : Control
{
    private Color _fg = Theme.Neutral, _bg = Theme.NeutralSoft;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Theme.Font(9f, FontStyle.Bold);
        Height = 26;
    }

    public bool ShowDot { get; set; } = true;

    public void Set(string text, Color fg, Color bg)
    {
        if (text == Text && fg == _fg && bg == _bg) return;
        Text = text; _fg = fg; _bg = bg;
        Width = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + (ShowDot ? 34 : 22);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.SetupGraphics(g);
        using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (var b = new SolidBrush(_bg))
            g.FillPath(b, path);
        int x = 11;
        if (ShowDot)
        {
            using var dot = new SolidBrush(_fg);
            g.FillEllipse(dot, x, Height / 2f - 4, 8, 8);
            x += 14;
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), _fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>사이드바 / 설정 내비게이션 항목</summary>
public class NavItem : Control
{
    private bool _hover, _selected;
    private string? _sub, _badge;
    private Color? _dot;

    public NavItem(string text, string? glyph = null, bool dark = true)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Glyph = glyph;
        Dark = dark;
        Cursor = Cursors.Hand;
        Font = Theme.Font(9.5f);
        Height = 40;
        Margin = Padding.Empty;
    }

    public string? Glyph { get; set; }
    public bool Dark { get; }

    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Invalidate(); } }

    public void Update(string text, string? sub = null, Color? dot = null, string? badge = null)
    {
        if (text == Text && sub == _sub && dot == _dot && badge == _badge) return;
        Text = text; _sub = sub; _dot = dot; _badge = badge;
        Height = sub != null ? 52 : 40;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parent = Parent?.BackColor ?? (Dark ? Theme.Sidebar : Theme.SurfaceAlt);
        g.Clear(parent);
        Theme.SetupGraphics(g);

        var box = new RectangleF(8, 2, Width - 16, Height - 4);
        Color? fill = _selected ? (Dark ? Theme.SidebarActive : Theme.AccentSoft)
            : _hover ? (Dark ? Theme.SidebarHover : Theme.NeutralSoft) : null;
        if (fill is Color f)
        {
            using var path = Theme.RoundRect(box, 8);
            using var b = new SolidBrush(f);
            g.FillPath(b, path);
        }
        if (_selected)
        {
            using var path = Theme.RoundRect(new RectangleF(8, Height / 2f - 10, 3, 20), 1.5f);
            using var b = new SolidBrush(Dark ? Color.FromArgb(129, 140, 248) : Theme.Accent);
            g.FillPath(b, path);
        }

        Color main = Dark ? (_selected ? Color.White : Theme.SidebarText) : (_selected ? Theme.Accent : Theme.Text);
        Color muted = Dark ? Theme.SidebarMuted : Theme.TextMuted;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

        if (_dot is Color dc)
        {
            using var b = new SolidBrush(dc);
            g.FillEllipse(b, 22, Height / 2f - 5, 10, 10);
        }
        else if (Glyph != null)
        {
            TextRenderer.DrawText(g, Glyph, Theme.IconFont(11f), new Rectangle(18, 0, 20, Height),
                _selected ? main : muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int x = 46;
        int badgeW = 0;
        if (_badge != null)
        {
            badgeW = TextRenderer.MeasureText(_badge, Theme.Font(8.5f), Size.Empty, TextFormatFlags.NoPadding).Width + 8;
            TextRenderer.DrawText(g, _badge, Theme.Font(8.5f), new Rectangle(Width - 18 - badgeW, 0, badgeW, Height), muted,
                flags | TextFormatFlags.Right);
        }
        int w = Width - x - 18 - badgeW;
        if (_sub != null)
        {
            TextRenderer.DrawText(g, Text, Theme.Font(9.5f, FontStyle.Bold), new Rectangle(x, 7, w, 20), main, flags);
            TextRenderer.DrawText(g, _sub, Theme.Font(8.5f), new Rectangle(x, 27, w, 18), muted, flags);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, w, Height), main, flags);
        }
    }
}

/// <summary>선택형 카드 (서버 추가 화면의 게임 선택)</summary>
public class SelectCard : Control
{
    private bool _hover, _selected;

    public SelectCard(string title, string description, string glyph)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = title;
        Description = description;
        Glyph = glyph;
        Cursor = Cursors.Hand;
        Height = 78;
    }

    public string Description { get; private set; }
    public string Glyph { get; private set; }
    /// <summary>false 면 선택 표시(라디오) 없이 정보 카드로만 사용</summary>
    public bool ShowRadio { get; set; } = true;
    public event EventHandler? SelectedChanged;

    public void SetContent(string title, string description, string glyph)
    {
        Text = title; Description = description; Glyph = glyph;
        Invalidate();
    }

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnClick(EventArgs e) { Selected = true; base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.SetupGraphics(g);

        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Theme.RoundRect(rect, 10))
        {
            using (var b = new SolidBrush(_selected ? Theme.AccentSoft : _hover ? Theme.SurfaceAlt : Theme.Surface)) g.FillPath(b, path);
            using var p = new Pen(_selected ? Theme.Accent : Theme.Border, _selected ? 2f : 1f);
            g.DrawPath(p, path);
        }

        var circle = new Rectangle(16, (Height - 40) / 2, 40, 40);
        using (var b = new SolidBrush(_selected ? Theme.Accent : Theme.NeutralSoft)) g.FillEllipse(b, circle);
        TextRenderer.DrawText(g, Glyph, Theme.IconFont(13f), circle, _selected ? Color.White : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = 70, w = Width - x - (ShowRadio ? 44 : 16);
        TextRenderer.DrawText(g, Text, Theme.Font(10.5f, FontStyle.Bold), new Rectangle(x, 12, w, 22), Theme.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, Description, Theme.Font(8.75f), new Rectangle(x, 34, w, Height - 40), Theme.TextMuted,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);

        if (!ShowRadio) return;
        // 라디오 표시
        var radio = new RectangleF(Width - 34, Height / 2f - 9, 18, 18);
        using (var p = new Pen(_selected ? Theme.Accent : Theme.TextFaint, 2f)) g.DrawEllipse(p, radio);
        if (_selected)
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, RectangleF.Inflate(radio, -5, -5));
    }
}

/// <summary>점선 테두리의 "+ 추가" 카드</summary>
public class AddCard : Control
{
    private bool _hover;

    public AddCard(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.SetupGraphics(g);
        var color = _hover ? Theme.Accent : Theme.TextFaint;
        using (var path = Theme.RoundRect(new RectangleF(1, 1, Width - 3, Height - 3), 12))
        {
            if (_hover) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, path);
            using var p = new Pen(color, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawPath(p, path);
        }
        var c = new Rectangle(Width / 2 - 20, Height / 2 - 34, 40, 40);
        TextRenderer.DrawText(g, Glyphs.Add, Theme.IconFont(16f), c, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Theme.Font(10f, FontStyle.Bold), new Rectangle(0, Height / 2 + 10, Width, 24), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}
