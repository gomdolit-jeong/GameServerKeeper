using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>서버 1대의 탭 화면: 상태, 제어 버튼, 접속자 목록, 서버 전용 로그</summary>
public sealed class ServerPanel : UserControl
{
    private readonly ServerManager _manager;
    private readonly IAppLog _uiLog;
    private ServerStatus? _rendered;

    private readonly Label _lblGame = ValueLabel();
    private readonly Label _lblState = ValueLabel();
    private readonly Label _lblPid = ValueLabel();
    private readonly Label _lblMemory = ValueLabel();
    private readonly Label _lblPlayers = ValueLabel();
    private readonly Label _lblUptime = ValueLabel();
    private readonly Label _lblApi = ValueLabel();
    private readonly Label _lblNextRestart = ValueLabel();
    private readonly Label _lblLastBackup = ValueLabel();
    private readonly Label _lblOperation = ValueLabel();

    private readonly Button _btnStart = Ui.ActionButton("▶ 시작");
    private readonly Button _btnStop = Ui.ActionButton("■ 안전 종료");
    private readonly Button _btnRestart = Ui.ActionButton("⟳ 재시작");
    private readonly Button _btnBackup = Ui.ActionButton("💾 즉시 백업");
    private readonly Button _btnUpdate = Ui.ActionButton("⬇ 업데이트");
    private readonly Button _btnAnnounce = Ui.ActionButton("📢 공지");
    private readonly Button _btnOpenBackups = Ui.ActionButton("백업 폴더");
    private readonly CheckBox _chkUpdate = new() { Text = "시작 전 업데이트", AutoSize = true, Margin = new Padding(12, 9, 3, 3) };

    private readonly ListView _lvPlayers = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false
    };

    public ServerPanel(ServerInstance server, ServerManager manager, IAppLog uiLog)
    {
        Server = server;
        _manager = manager;
        _uiLog = uiLog;
        Dock = DockStyle.Fill;
        LogView = new LogView(includeScope: false);

        BuildLayout();
        WireEvents();
        _chkUpdate.Checked = server.Profile.UpdateBeforeStart;
    }

    public ServerInstance Server { get; }
    public LogView LogView { get; }

    private void BuildLayout()
    {
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3, Padding = new Padding(6) };
        for (int i = 0; i < 3; i++)
        {
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        }
        void Add(int col, int row, string caption, Label value)
        {
            status.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 6) }, col * 2, row);
            status.Controls.Add(value, col * 2 + 1, row);
        }
        Add(0, 0, "서버 상태", _lblState);
        Add(0, 1, "게임 / PID", _lblPid);
        Add(0, 2, "진행 중 작업", _lblOperation);
        Add(1, 0, "메모리", _lblMemory);
        Add(1, 1, "접속자", _lblPlayers);
        Add(1, 2, "가동 시간", _lblUptime);
        Add(2, 0, "서버 API", _lblApi);
        Add(2, 1, "다음 정기 재시작", _lblNextRestart);
        Add(2, 2, "마지막 백업", _lblLastBackup);
        _lblState.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);

        var statusGroup = new GroupBox { Text = "서버 상태", Dock = DockStyle.Top, Height = 120, Padding = new Padding(6) };
        statusGroup.Controls.Add(status);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(4, 6, 4, 4), WrapContents = false };
        buttons.Controls.AddRange([_btnStart, _btnStop, _btnRestart, _btnBackup, _btnUpdate, _btnAnnounce, _chkUpdate, _btnOpenBackups]);
        _lvPlayers.Columns.Add("이름", 130);
        _lvPlayers.Columns.Add("Lv", 40, HorizontalAlignment.Right);
        _lvPlayers.Columns.Add("Ping", 55, HorizontalAlignment.Right);
        _lvPlayers.Columns.Add("Id", 150);
        var playersGroup = new GroupBox { Text = "접속자", Dock = DockStyle.Fill, Padding = new Padding(6) };
        playersGroup.Controls.Add(_lvPlayers);

        var logGroup = new GroupBox { Text = "서버 로그", Dock = DockStyle.Fill, Padding = new Padding(6) };
        logGroup.Controls.Add(LogView);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
        split.Panel1.Controls.Add(playersGroup);
        split.Panel2.Controls.Add(logGroup);
        // 크기가 확정된 뒤에 한 번만 접속자 목록 폭을 잡는다 (HandleCreated 시점에는 폭이 0이라 무시됨)
        bool splitInitialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInitialized || split.Width < 700) return;
            split.SplitterDistance = 400;
            splitInitialized = true;
        };

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(statusGroup);
    }

    private void WireEvents()
    {
        _btnStart.Click += async (_, _) => await RunAsync(() => Server.StartServerAsync(_chkUpdate.Checked));
        _btnStop.Click += async (_, _) =>
        {
            var countdown = AskCountdown("안전 종료");
            if (countdown != null) await RunAsync(() => Server.StopServerAsync(countdown.Value));
        };
        _btnRestart.Click += async (_, _) =>
        {
            var countdown = AskCountdown("재시작");
            if (countdown != null)
                await RunAsync(() => Server.RestartServerAsync(countdown.Value, backup: false, update: _chkUpdate.Checked));
        };
        _btnBackup.Click += async (_, _) => await RunAsync(Server.BackupNowAsync);
        _btnUpdate.Click += async (_, _) => await RunAsync(Server.UpdateNowAsync);
        _btnAnnounce.Click += async (_, _) =>
        {
            var msg = Ui.Prompt(FindForm()!, Font, "인게임 공지", "공지할 메시지를 입력하세요:");
            if (string.IsNullOrWhiteSpace(msg)) return;
            var ok = await Server.AnnounceAsync(msg);
            _uiLog.Info("UI", ok ? $"[{Server.Name}] 공지 전송: {msg}" : $"[{Server.Name}] 공지 전송 실패");
        };
        _btnOpenBackups.Click += (_, _) => Ui.OpenFolder(Server.Profile.BackupDir, _uiLog);

        _chkUpdate.CheckedChanged += (_, _) =>
        {
            if (Server.Profile.UpdateBeforeStart == _chkUpdate.Checked) return;
            var updated = Server.Profile.Clone();
            updated.UpdateBeforeStart = _chkUpdate.Checked;
            try { _manager.UpdateServer(updated); } catch { /* 로그는 매니저가 남김 */ }
        };
    }

    /// <summary>UI 타이머에서 주기적으로 호출</summary>
    public void Render()
    {
        var s = Server.LatestStatus;
        if (s == null) return;

        _lblUptime.Text = s.ProcessRunning && s.Uptime is { } up ? Ui.Uptime(up) : "-";
        if (ReferenceEquals(s, _rendered)) return;
        _rendered = s;

        var caps = s.Capabilities;
        bool hasPlayers = caps.HasFlag(GameCapabilities.PlayerList);

        var (text, color) = Ui.StateText(s.State);
        _lblState.Text = text;
        _lblState.ForeColor = color;
        _lblPid.Text = $"{s.GameName} / {s.Pid?.ToString() ?? "-"}";
        _lblOperation.Text = s.CurrentOperation ?? "-";

        if (s.WorkingSetBytes is long ws)
        {
            double ratio = (double)ws / Math.Max(1, s.MemoryThresholdBytes);
            _lblMemory.Text = $"{Ui.Gb(ws)} / {Ui.Gb(s.MemoryThresholdBytes)} ({ratio:P0})";
            _lblMemory.ForeColor = ratio >= 0.9 ? Color.Firebrick : ratio >= 0.75 ? Color.DarkOrange : SystemColors.ControlText;
        }
        else
        {
            _lblMemory.Text = "-";
            _lblMemory.ForeColor = SystemColors.ControlText;
        }

        _lblPlayers.Text = !hasPlayers ? "지원 안 함"
            : s.Players != null ? $"{s.Players.Count}명"
            : s.ProcessRunning ? "? (API 응답 없음)" : "-";
        _lblApi.Text = !hasPlayers ? "없음" : !s.ProcessRunning ? "-" : s.ApiOnline ? "연결됨" : "응답 없음";
        _lblApi.ForeColor = hasPlayers && s.ProcessRunning && !s.ApiOnline ? Color.DarkOrange : SystemColors.ControlText;
        _lblNextRestart.Text = s.NextScheduledRestart?.ToString("MM-dd HH:mm") ?? "사용 안 함";
        _lblLastBackup.Text = s.LastBackup?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

        _lvPlayers.BeginUpdate();
        _lvPlayers.Items.Clear();
        foreach (var p in (s.Players ?? Array.Empty<PlayerInfo>()).OrderBy(p => p.Name))
        {
            var item = new ListViewItem(p.Name);
            item.SubItems.Add(p.Level?.ToString() ?? "");
            item.SubItems.Add(p.Ping is double ping ? $"{ping:0}" : "");
            item.SubItems.Add(p.Id);
            _lvPlayers.Items.Add(item);
        }
        _lvPlayers.EndUpdate();

        bool idle = s.CurrentOperation == null;
        _btnStart.Enabled = idle && !s.ProcessRunning;
        _btnStop.Enabled = idle && s.ProcessRunning;
        _btnRestart.Enabled = idle && s.ProcessRunning;
        _btnBackup.Enabled = idle;
        _btnUpdate.Enabled = idle && !s.ProcessRunning && Server.Profile.AppId > 0;
        _btnAnnounce.Enabled = caps.HasFlag(GameCapabilities.Announce) && s.ProcessRunning && (s.ApiOnline || !hasPlayers);
        _chkUpdate.Enabled = Server.Profile.AppId > 0;
        ApplyButtonColors();
    }

    /// <summary>시작/종료 버튼 강조색은 활성 상태일 때만 (비활성인데 색이 있으면 눌리는 것처럼 보임)</summary>
    private void ApplyButtonColors()
    {
        _btnStart.BackColor = _btnStart.Enabled ? Color.FromArgb(220, 245, 225) : SystemColors.Control;
        _btnStop.BackColor = _btnStop.Enabled ? Color.FromArgb(250, 225, 225) : SystemColors.Control;
    }

    private async Task RunAsync(Func<Task<bool>> action)
    {
        foreach (var b in new[] { _btnStart, _btnStop, _btnRestart, _btnBackup, _btnUpdate }) b.Enabled = false;
        ApplyButtonColors();
        try { await action(); }
        catch (Exception ex) { _uiLog.Error("UI", "작업 실행 오류", ex); }
        finally { _rendered = null; Render(); }
    }

    /// <summary>true=카운트다운, false=즉시, null=취소</summary>
    private bool? AskCountdown(string action)
    {
        var caps = Server.Capabilities;
        if (!caps.HasFlag(GameCapabilities.Announce)) return false; // 공지 불가 → 묻지 않음
        var count = Server.LastKnownPlayerCount;
        if (count == 0) return false; // 아무도 없으면 즉시

        var countText = caps.HasFlag(GameCapabilities.PlayerList) ? $"{count?.ToString() ?? "알 수 없음"}명" : "확인 불가";
        var r = MessageBox.Show(FindForm(),
            $"[{Server.Name}] 현재 접속자: {countText}\n\n" +
            $"[예]  카운트다운 공지({Server.Profile.ShutdownCountdown}초) 후 {action}\n" +
            $"[아니요]  공지 없이 즉시 저장 후 {action}\n" +
            "[취소]  돌아가기",
            action, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r switch { DialogResult.Yes => true, DialogResult.No => false, _ => null };
    }

    private static Label ValueLabel() => new() { AutoSize = true, Text = "-", Margin = new Padding(3, 6, 3, 6) };
}
