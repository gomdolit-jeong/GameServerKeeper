using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.UI;

/// <summary>색상 표시 + 줄 수 제한 + 우클릭 메뉴(줄바꿈/복사/지우기)가 있는 로그 창</summary>
public sealed class LogView : RichTextBox
{
    private const int MaxLines = 3000;
    private readonly bool _includeScope;

    public LogView(bool includeScope)
    {
        _includeScope = includeScope;
        Dock = DockStyle.Fill;
        ReadOnly = true;
        BackColor = Theme.LogBg;
        ForeColor = Color.Gainsboro;
        Font = new Font("Consolas", 9.5f);
        WordWrap = true; // 긴 로그(경로, SteamCMD 출력 등)가 잘리지 않도록 기본 줄바꿈
        BorderStyle = BorderStyle.None;
        DetectUrls = false;
        ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var wrap = new ToolStripMenuItem("줄바꿈") { CheckOnClick = true, Checked = WordWrap };
        wrap.CheckedChanged += (_, _) =>
        {
            WordWrap = wrap.Checked;
            SelectionStart = TextLength;
            ScrollToCaret();
        };
        var copy = new ToolStripMenuItem("복사", null, (_, _) =>
        {
            var text = SelectionLength > 0 ? SelectedText : Text;
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        });
        menu.Opening += (_, _) => copy.Text = SelectionLength > 0 ? "선택 영역 복사" : "전체 복사";
        menu.Items.Add(wrap);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(copy);
        menu.Items.Add("화면 지우기 (파일 로그는 유지)", null, (_, _) => Clear());
        return menu;
    }

    public void Append(LogEntry e)
    {
        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionColor = e.Level switch
        {
            LogLevel.Error => Color.FromArgb(255, 120, 120),
            LogLevel.Warn => Color.FromArgb(251, 191, 36),
            _ when e.Source == "SteamCMD" => Color.FromArgb(125, 170, 230),
            _ when e.Source == "Player" => Color.FromArgb(110, 220, 150),
            _ => Color.FromArgb(214, 218, 226)
        };
        AppendText(Logger.FormatForUi(e, _includeScope) + Environment.NewLine);
    }

    /// <summary>배치 추가 후 1회 호출: 오래된 줄 정리 + 맨 아래로 스크롤</summary>
    public void Commit()
    {
        var lines = Lines.Length;
        if (lines > MaxLines)
        {
            var cut = GetFirstCharIndexFromLine(lines - MaxLines);
            ReadOnly = false;
            Select(0, cut);
            SelectedText = "";
            ReadOnly = true;
        }
        SelectionStart = TextLength;
        ScrollToCaret();
    }
}

internal static class Ui
{
    public static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB";

    public static string Uptime(TimeSpan up) =>
        up.TotalDays >= 1 ? $"{(int)up.TotalDays}일 {up.Hours}시간 {up.Minutes}분"
        : up.TotalHours >= 1 ? $"{up.Hours}시간 {up.Minutes}분"
        : $"{up.Minutes}분 {up.Seconds}초";

    public static string Relative(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "방금 전";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}분 전";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}시간 전";
        return $"{(int)d.TotalDays}일 전";
    }

    public static string Upcoming(DateTime t)
    {
        var d = t - DateTime.Now;
        var day = t.Date == DateTime.Today ? "오늘" : t.Date == DateTime.Today.AddDays(1) ? "내일" : t.ToString("MM-dd");
        return d.TotalHours < 1 ? $"{day} {t:HH:mm} ({Math.Max(0, (int)d.TotalMinutes)}분 후)" : $"{day} {t:HH:mm}";
    }

    public static void OpenFolder(string path, IAppLog log)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { log.Warn("UI", $"폴더 열기 실패: {ex.Message}"); }
    }

    /// <summary>카드 위쪽 제목 줄</summary>
    public static Control CardHeader(string title, string glyph, Color? fg = null, Color? bg = null)
    {
        var color = fg ?? Theme.Text;
        var panel = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = bg ?? Theme.Surface };
        panel.Paint += (_, e) =>
        {
            TextRenderer.DrawText(e.Graphics, glyph, Theme.IconFont(10.5f), new Rectangle(0, 0, 20, 26), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, title, Theme.Font(10.5f, FontStyle.Bold), new Rectangle(26, 0, panel.Width - 26, 26), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        return panel;
    }
}
