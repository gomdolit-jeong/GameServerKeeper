using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>색상 표시 + 줄 수 제한이 있는 로그 창</summary>
public sealed class LogView : RichTextBox
{
    private const int MaxLines = 3000;
    private readonly bool _includeScope;

    public LogView(bool includeScope)
    {
        _includeScope = includeScope;
        Dock = DockStyle.Fill;
        ReadOnly = true;
        BackColor = Color.FromArgb(24, 24, 28);
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
            LogLevel.Error => Color.FromArgb(255, 110, 110),
            LogLevel.Warn => Color.FromArgb(255, 200, 90),
            _ when e.Source == "SteamCMD" => Color.FromArgb(140, 180, 220),
            _ when e.Source == "Player" => Color.FromArgb(140, 220, 150),
            _ => Color.Gainsboro
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
    public static (string Text, Color Color) StateText(ServerState s) => s switch
    {
        ServerState.Running => ("● 실행 중", Color.ForestGreen),
        ServerState.Starting => ("◐ 시작 중", Color.DarkOrange),
        ServerState.Stopping => ("◐ 종료 중", Color.DarkOrange),
        ServerState.Updating => ("⬇ 업데이트 중", Color.SteelBlue),
        ServerState.BackingUp => ("💾 백업 중", Color.SteelBlue),
        _ => ("○ 중지됨", Color.Firebrick)
    };

    public static string Gb(long bytes) => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB";

    public static string Uptime(TimeSpan up) => $"{(int)up.TotalDays}일 {up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}";

    public static Button ActionButton(string text) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(90, 30), Margin = new Padding(3), UseVisualStyleBackColor = true
    };

    public static void OpenFolder(string path, IAppLog log)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { log.Warn("UI", $"폴더 열기 실패: {ex.Message}"); }
    }

    public static string? Prompt(IWin32Window owner, Font font, string title, string caption)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(440, 110), MinimizeBox = false, MaximizeBox = false, Font = font, ShowInTaskbar = false
        };
        var lbl = new Label { Text = caption, Left = 12, Top = 12, AutoSize = true };
        var txt = new TextBox { Left = 12, Top = 36, Width = 416 };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Left = 272, Top = 70, Width = 75 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Left = 353, Top = 70, Width = 75 };
        f.Controls.AddRange([lbl, txt, ok, cancel]);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text.Trim() : null;
    }
}
