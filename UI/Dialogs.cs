using GameServerKeeper.Core.Games;

namespace GameServerKeeper.UI;

/// <summary>
/// 서버 추가 1단계: 이름 + 서버 실행 파일. 게임 종류는 실행 파일 이름으로 자동 인식한다.
/// (인식 결과가 틀리면 다음 단계 설정 화면의 "게임 종류"에서 바꿀 수 있음)
/// </summary>
public sealed class AddServerDialog : Form
{
    private readonly TextBox _txtName = new() { Font = Theme.Font(10.5f), BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _txtExe = new() { Font = Theme.Font(10.5f), BorderStyle = BorderStyle.FixedSingle };
    private readonly SelectCard _detected = new("", "", Glyphs.Server) { ShowRadio = false, Selected = true, Cursor = Cursors.Default, Height = 84 };
    private readonly ToolTip _tip = Theme.CreateToolTip();
    private bool _nameTouched;
    private bool _settingName;

    public GameDefinition DetectedGame { get; private set; } = GameRegistry.Generic;
    public string ServerName => _txtName.Text.Trim();
    public string ExePath => _txtExe.Text.Trim().Trim('"');

    public AddServerDialog()
    {
        Text = "서버 추가";
        Icon = AppIcon.Get();
        Font = Theme.Font(9.5f);
        BackColor = Theme.Surface;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        const int left = 28, width = 540;
        int y = 22;
        Controls.Add(new Label { Text = "서버 추가", Font = Theme.Font(15f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(left, y) });
        y += 36;
        Controls.Add(new Label
        {
            Text = "서버 실행 파일을 고르면 어떤 게임인지 자동으로 알아내요.", ForeColor = Theme.TextMuted, AutoSize = true, Location = new Point(left + 2, y)
        });
        y += 40;

        // 서버 실행 파일
        Controls.Add(new Label { Text = "서버 실행 파일", Font = Theme.Font(9.75f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(left, y) });
        y += 26;
        var browse = new ModernButton("찾아보기", Glyphs.Folder) { Compact = true };
        browse.Click += (_, _) => Browse();
        Controls.Add(browse);
        Load += (_, _) => { browse.Location = new Point(left + width - browse.Width, _txtExe.Top - 3); _txtExe.Width = width - browse.Width - 8; };
        _txtExe.Location = new Point(left, y);
        _txtExe.Width = width - 110;
        _txtExe.TextChanged += (_, _) => Detect();
        Controls.Add(_txtExe);
        _tip.SetToolTip(_txtExe, Theme.WrapTip("서버를 실행하는 exe 또는 bat 파일이에요. 예: D:\\Steam_Dedicated_Server\\Palworld\\PalServer.exe"));
        y += 44;

        // 인식된 게임
        _detected.Location = new Point(left, y);
        _detected.Width = width;
        Controls.Add(_detected);
        y += _detected.Height + 22;

        // 서버 이름
        Controls.Add(new Label { Text = "서버 이름", Font = Theme.Font(9.75f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(left, y) });
        y += 26;
        _txtName.Location = new Point(left, y);
        _txtName.Width = width;
        _txtName.Text = "새 서버";
        _txtName.TextChanged += (_, _) => { if (!_settingName) _nameTouched = true; };
        Controls.Add(_txtName);
        _tip.SetToolTip(_txtName, "사이드바와 디스코드 알림에 표시되는 이름이에요.");
        y += 52;

        var next = new ModernButton("다음", Glyphs.Play, ButtonVariant.Primary) { DialogResult = DialogResult.OK };
        var cancel = new ModernButton("취소") { DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(left, y), BackColor = Theme.Surface, WrapContents = false };
        cancel.Margin = new Padding(8, 0, 0, 0);
        next.Margin = Padding.Empty;
        bar.Controls.AddRange([cancel, next]);
        Controls.Add(bar);
        Load += (_, _) => bar.Left = left + width - bar.Width;
        _tip.SetToolTip(next, "다음 화면에서 폴더·업데이트·백업 같은 세부 설정을 확인하고 저장해요.");

        ClientSize = new Size(left * 2 + width, y + 60);
        AcceptButton = next;
        CancelButton = cancel;
        Detect();
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "서버 실행 파일 선택",
            Filter = "실행 파일|*.exe;*.bat;*.cmd|모든 파일|*.*",
        };
        try
        {
            var dir = Path.GetDirectoryName(ExePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            else if (Directory.Exists(@"D:\Steam_Dedicated_Server")) dlg.InitialDirectory = @"D:\Steam_Dedicated_Server";
        }
        catch { /* 잘못된 경로 무시 */ }
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtExe.Text = dlg.FileName;
    }

    private void Detect()
    {
        var path = ExePath;
        DetectedGame = GameRegistry.Detect(path);
        bool generic = ReferenceEquals(DetectedGame, GameRegistry.Generic);

        if (string.IsNullOrWhiteSpace(path))
            _detected.SetContent("실행 파일을 골라 주세요", "고른 파일을 보고 게임을 자동으로 알아내요. 모르는 게임은 '범용'으로 추가돼요.", Glyphs.Info);
        else if (generic)
            _detected.SetContent("알 수 없는 게임 → 범용으로 추가해요", DetectedGame.Description, Glyphs.Server);
        else
            _detected.SetContent($"{DetectedGame.DisplayName} 서버로 인식했어요", DetectedGame.Description, Glyphs.Game);

        // 이름을 직접 바꾸지 않았다면 게임에 맞는 이름 제안
        if (!_nameTouched && !string.IsNullOrWhiteSpace(path))
        {
            _settingName = true;
            try { _txtName.Text = DetectedGame.CreateProfile(path).Name; } catch { /* 잘못된 경로 */ }
            _settingName = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>한 줄 입력 대화상자</summary>
public static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string caption)
    {
        using var f = new Form
        {
            Text = title, Icon = AppIcon.Get(), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(500, 190), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            Font = Theme.Font(9.5f), BackColor = Theme.Surface
        };
        var t = new Label { Text = title, Font = Theme.Font(13f, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true, Location = new Point(24, 18) };
        var c = new Label { Text = caption, ForeColor = Theme.TextMuted, AutoSize = true, MaximumSize = new Size(452, 0), Location = new Point(25, 52) };
        var txt = new TextBox { Location = new Point(24, 86), Width = 452, Font = Theme.Font(10.5f), BorderStyle = BorderStyle.FixedSingle };
        var ok = new ModernButton("보내기", Glyphs.Announce, ButtonVariant.Primary) { DialogResult = DialogResult.OK };
        var cancel = new ModernButton("취소") { DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface, WrapContents = false };
        cancel.Margin = new Padding(8, 0, 0, 0);
        ok.Margin = Padding.Empty;
        bar.Controls.AddRange([cancel, ok]);
        f.Controls.AddRange([t, c, txt, bar]);
        f.Load += (_, _) => bar.Location = new Point(476 - bar.Width, 134);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text.Trim() : null;
    }
}
