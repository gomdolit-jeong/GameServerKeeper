using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>서버 1대의 화면: 머리글, 제어 버튼, 상태 타일, 접속자 목록, 서버 로그</summary>
public sealed class ServerPanel : UserControl
{
    private readonly ServerManager _manager;
    private readonly IAppLog _uiLog;
    private readonly ToolTip _tip = Theme.CreateToolTip();
    private ServerStatus? _rendered;

    private readonly Label _lblName = new() { AutoSize = true, Font = Theme.Font(18f, FontStyle.Bold), ForeColor = Theme.Text, Location = new Point(0, 0) };
    private readonly StatusPill _pill = new();
    private readonly Label _lblSub = new() { AutoSize = true, Font = Theme.Font(9.5f), ForeColor = Theme.TextMuted, Location = new Point(2, 42) };

    private readonly ModernButton _btnStart = new("시작", Glyphs.Play, ButtonVariant.Success);
    private readonly ModernButton _btnStop = new("안전 종료", Glyphs.Stop, ButtonVariant.Danger);
    private readonly ModernButton _btnRestart = new("재시작", Glyphs.Restart);
    private readonly ModernButton _btnBackup = new("즉시 백업", Glyphs.Save);
    private readonly ModernButton _btnUpdate = new("업데이트", Glyphs.Download);
    private readonly ModernButton _btnAnnounce = new("공지", Glyphs.Announce);
    private readonly ModernButton _btnFolder = new("백업 폴더", Glyphs.Folder, ButtonVariant.Ghost);
    private readonly ToggleSwitch _tglUpdate = new() { Label = "시작 전 업데이트" };

    private readonly StatTile _tMemory = new("메모리", Glyphs.Memory, Color.FromArgb(124, 58, 237));
    private readonly StatTile _tPlayers = new("접속자", Glyphs.People, Theme.Info);
    private readonly StatTile _tUptime = new("가동 시간", Glyphs.Clock, Theme.Success);
    private readonly StatTile _tApi = new("서버 API", Glyphs.Globe, Color.FromArgb(14, 165, 233));
    private readonly StatTile _tNext = new("다음 정기 재시작", Glyphs.Calendar, Theme.Warning);
    private readonly StatTile _tBackup = new("마지막 백업", Glyphs.History, Color.FromArgb(13, 148, 136));

    private readonly ListView _lvPlayers = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Theme.Font(9.5f), BackColor = Theme.Surface
    };
    private readonly Label _lblEmpty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextFaint, Font = Theme.Font(9.5f), BackColor = Theme.Surface
    };

    public ServerPanel(ServerInstance server, ServerManager manager, IAppLog uiLog, Action onEdit, Action onRemove)
    {
        Server = server;
        _manager = manager;
        _uiLog = uiLog;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        LogView = new LogView(includeScope: false);

        BuildLayout(onEdit, onRemove);
        WireEvents();
        SetTooltips();
        _tglUpdate.Checked = server.Profile.UpdateBeforeStart;
    }

    public ServerInstance Server { get; }
    public LogView LogView { get; }

    // ═════════════ 레이아웃 ═════════════

    private void BuildLayout(Action onEdit, Action onRemove)
    {
        // 머리글
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Bg };
        var edit = new ModernButton("서버 설정", Glyphs.Settings);
        var remove = new ModernButton("삭제", Glyphs.Delete, ButtonVariant.Ghost) { TextColor = Theme.Danger };
        edit.Click += (_, _) => onEdit();
        remove.Click += (_, _) => onRemove();
        _tip.SetToolTip(edit, "이 서버의 경로·업데이트·크래시·정기 재시작 설정을 바꿔요.");
        _tip.SetToolTip(remove, "이 서버를 목록에서 지워요. 서버 파일과 백업은 지우지 않아요.");
        var headerButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 0) };
        headerButtons.Controls.AddRange([remove, edit]);
        edit.Margin = Padding.Empty;
        header.Controls.AddRange([_lblName, _pill, _lblSub, headerButtons]);

        // 제어 버튼
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 2, 0, 0) };
        _tglUpdate.Margin = new Padding(12, 4, 12, 0);
        actions.Controls.AddRange([_btnStart, _btnStop, _btnRestart, _btnBackup, _btnUpdate, _btnAnnounce, _tglUpdate, _btnFolder]);

        // 상태 타일
        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, ColumnCount = 6, RowCount = 1, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, 12) };
        StatTile[] all = [_tMemory, _tPlayers, _tUptime, _tApi, _tNext, _tBackup];
        for (int i = 0; i < all.Length; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / all.Length));
            all[i].Dock = DockStyle.Fill;
            all[i].Compact = true;
            all[i].Margin = new Padding(0, 0, i < all.Length - 1 ? 10 : 0, 0);
            tiles.Controls.Add(all[i], i, 0);
        }

        // 접속자 카드
        _lvPlayers.Columns.Add("이름", 150);
        _lvPlayers.Columns.Add("레벨", 60, HorizontalAlignment.Right);
        _lvPlayers.Columns.Add("핑", 60, HorizontalAlignment.Right);
        _lvPlayers.Columns.Add("Id", 140);
        var playersCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };
        playersCard.Controls.Add(_lvPlayers);
        playersCard.Controls.Add(_lblEmpty);
        playersCard.Controls.Add(Ui.CardHeader("접속자", Glyphs.People));

        // 로그 카드 (어두운 카드)
        var logCard = new Card { Dock = DockStyle.Fill, FillColor = Theme.LogBg, BorderColor = Theme.LogBg, Padding = new Padding(16, 12, 8, 12) };
        logCard.Controls.Add(LogView);
        logCard.Controls.Add(Ui.CardHeader("서버 로그", Glyphs.Document, Color.FromArgb(214, 218, 226), Theme.LogBg));
        _tip.SetToolTip(LogView, "우클릭: 줄바꿈 켜기/끄기, 복사, 화면 지우기");

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 14, BackColor = Theme.Bg
        };
        split.Panel1.BackColor = split.Panel2.BackColor = Theme.Bg;
        split.Panel1.Controls.Add(playersCard);
        split.Panel2.Controls.Add(logCard);
        bool splitInitialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInitialized || split.Width < 700) return;
            split.SplitterDistance = 420;
            splitInitialized = true;
        };

        Controls.Add(split);
        Controls.Add(tiles);
        Controls.Add(actions);
        Controls.Add(header);
    }

    private void SetTooltips()
    {
        _tip.SetToolTip(_btnStart, Theme.WrapTip("서버를 켜요. '시작 전 업데이트'가 켜져 있으면 SteamCMD로 최신 버전을 먼저 받아요."));
        _tip.SetToolTip(_btnStop, Theme.WrapTip("접속자가 있으면 카운트다운 공지 후 월드를 저장하고 정상 종료해요. 시간 안에 안 꺼지면 강제 종료해요."));
        _tip.SetToolTip(_btnRestart, Theme.WrapTip("안전 종료 후 다시 켜요."));
        _tip.SetToolTip(_btnBackup, Theme.WrapTip("세이브 폴더를 지금 바로 zip으로 백업해요. 서버가 켜져 있으면 먼저 저장을 요청해요."));
        _tip.SetToolTip(_btnUpdate, Theme.WrapTip("서버가 꺼져 있을 때 SteamCMD로 최신 버전을 받아요."));
        _tip.SetToolTip(_btnAnnounce, Theme.WrapTip("접속 중인 플레이어에게 인게임 공지를 보내요."));
        _tip.SetToolTip(_btnFolder, "백업 zip 파일이 있는 폴더를 열어요.");
        _tip.SetToolTip(_tglUpdate, Theme.WrapTip("켜두면 서버를 시작할 때마다 먼저 업데이트를 확인해요."));
        _tip.SetToolTip(_tMemory, Theme.WrapTip("실제 서버 프로세스가 쓰는 메모리예요. 막대가 기준에 닿으면 안전 재시작해요."));
        _tip.SetToolTip(_tPlayers, "지금 접속 중인 플레이어 수예요.");
        _tip.SetToolTip(_tUptime, "서버가 마지막으로 켜진 뒤 지난 시간이에요.");
        _tip.SetToolTip(_tApi, Theme.WrapTip("공지·저장·접속자 조회에 쓰는 서버 관리 API의 연결 상태예요."));
        _tip.SetToolTip(_tNext, Theme.WrapTip("다음에 자동으로 안전 종료 → 백업 → 업데이트 → 재시작할 시각이에요."));
        _tip.SetToolTip(_tBackup, "가장 최근에 만든 백업이에요.");
    }

    private void WireEvents()
    {
        _btnStart.Click += async (_, _) => await RunAsync(() => Server.StartServerAsync(_tglUpdate.Checked));
        _btnStop.Click += async (_, _) =>
        {
            var countdown = AskCountdown(FindForm()!, Server, "안전 종료");
            if (countdown != null) await RunAsync(() => Server.StopServerAsync(countdown.Value));
        };
        _btnRestart.Click += async (_, _) =>
        {
            var countdown = AskCountdown(FindForm()!, Server, "재시작");
            if (countdown != null)
                await RunAsync(() => Server.RestartServerAsync(countdown.Value, backup: false, update: _tglUpdate.Checked));
        };
        _btnBackup.Click += async (_, _) => await RunAsync(Server.BackupNowAsync);
        _btnUpdate.Click += async (_, _) => await RunAsync(Server.UpdateNowAsync);
        _btnAnnounce.Click += async (_, _) =>
        {
            var msg = PromptDialog.Show(FindForm()!, "인게임 공지", $"{Server.Name}에 접속한 플레이어에게 보낼 메시지를 입력하세요.");
            if (string.IsNullOrWhiteSpace(msg)) return;
            var ok = await Server.AnnounceAsync(msg);
            _uiLog.Info("UI", ok ? $"[{Server.Name}] 공지 전송: {msg}" : $"[{Server.Name}] 공지 전송 실패");
        };
        _btnFolder.Click += (_, _) => Ui.OpenFolder(Server.Profile.BackupDir, _uiLog);

        _tglUpdate.CheckedChanged += (_, _) =>
        {
            if (Server.Profile.UpdateBeforeStart == _tglUpdate.Checked) return;
            var updated = Server.Profile.Clone();
            updated.UpdateBeforeStart = _tglUpdate.Checked;
            try { _manager.UpdateServer(updated); } catch { /* 로그는 매니저가 남김 */ }
        };
    }

    // ═════════════ 렌더링 ═════════════

    /// <summary>UI 타이머에서 주기적으로 호출</summary>
    public void Render()
    {
        var s = Server.LatestStatus;
        if (s == null) return;

        // 가동 시간은 매번 갱신
        if (s.ProcessRunning && s.StartTime is DateTime st)
            _tUptime.Set(Ui.Uptime(DateTime.Now - st), $"{st:MM-dd HH:mm} 시작");
        else
            _tUptime.Set("-", "서버가 꺼져 있어요");

        if (ReferenceEquals(s, _rendered)) return;
        _rendered = s;

        var caps = s.Capabilities;
        bool hasPlayers = caps.HasFlag(GameCapabilities.PlayerList);

        // 머리글
        _lblName.Text = s.Name;
        var (stateText, fg, bg) = Theme.State(s.State);
        if (s.CurrentOperation != null) _pill.Set($"{s.CurrentOperation} 진행 중", Theme.Warning, Theme.WarningSoft);
        else _pill.Set(stateText, fg, bg);
        _pill.Location = new Point(_lblName.Right + 12, _lblName.Top + (_lblName.Height - _pill.Height) / 2);
        _lblSub.Text = s.ProcessRunning ? $"{s.GameName}   ·   PID {s.Pid}" : $"{s.GameName}   ·   서버가 꺼져 있어요";

        // 타일
        if (s.WorkingSetBytes is long ws)
        {
            double ratio = (double)ws / Math.Max(1, s.MemoryThresholdBytes);
            var color = ratio >= 0.9 ? Theme.Danger : ratio >= 0.75 ? Theme.Warning : Color.FromArgb(124, 58, 237);
            _tMemory.Set(Ui.Gb(ws), $"기준 {Ui.Gb(s.MemoryThresholdBytes)}의 {ratio:P0}", null, ratio, color);
        }
        else _tMemory.Set("-", $"기준 {Ui.Gb(s.MemoryThresholdBytes)}", null, 0);

        if (!hasPlayers) _tPlayers.Set("지원 안 함", "이 게임은 접속자 조회 불가", Theme.TextFaint);
        else if (s.Players != null) _tPlayers.Set($"{s.Players.Count}명");
        else if (s.ProcessRunning) _tPlayers.Set("확인 중", "서버 응답을 기다리는 중", Theme.Warning);
        else _tPlayers.Set("-", "서버가 꺼져 있어요");

        if (!hasPlayers) _tApi.Set("없음", "이 게임은 관리 API 미지원", Theme.TextFaint);
        else if (!s.ProcessRunning) _tApi.Set("-", "서버가 꺼져 있어요");
        else if (s.ApiOnline) _tApi.Set("연결됨", "공지·저장 가능", Theme.Success);
        else _tApi.Set("응답 없음", "시작 중이거나 API 설정 확인", Theme.Warning);

        if (s.NextScheduledRestart is DateTime next) _tNext.Set(Ui.Upcoming(next), "안전 종료 → 백업 → 업데이트");
        else _tNext.Set("사용 안 함", "서버 설정에서 켤 수 있어요", Theme.TextFaint);

        if (s.LastBackup is DateTime lb) _tBackup.Set(Ui.Relative(lb), lb.ToString("yyyy-MM-dd HH:mm"));
        else _tBackup.Set("없음", "아직 백업이 없어요", Theme.TextFaint);

        // 접속자 목록
        var players = s.Players ?? Array.Empty<PlayerInfo>();
        _lvPlayers.BeginUpdate();
        _lvPlayers.Items.Clear();
        foreach (var p in players.OrderBy(p => p.Name))
        {
            var item = new ListViewItem(p.Name);
            item.SubItems.Add(p.Level?.ToString() ?? "");
            item.SubItems.Add(p.Ping is double ping ? $"{ping:0}" : "");
            item.SubItems.Add(p.Id);
            _lvPlayers.Items.Add(item);
        }
        _lvPlayers.EndUpdate();
        bool empty = players.Count == 0;
        _lblEmpty.Text = !hasPlayers ? "이 게임은 접속자 조회를 지원하지 않아요"
            : !s.ProcessRunning ? "서버가 꺼져 있어요"
            : s.Players == null ? "서버 응답을 기다리는 중이에요" : "접속한 플레이어가 없어요";
        _lblEmpty.Visible = empty;
        _lvPlayers.Visible = !empty;

        // 버튼
        bool idle = s.CurrentOperation == null;
        _btnStart.Enabled = idle && !s.ProcessRunning;
        _btnStop.Enabled = idle && s.ProcessRunning;
        _btnRestart.Enabled = idle && s.ProcessRunning;
        _btnBackup.Enabled = idle;
        _btnUpdate.Enabled = idle && !s.ProcessRunning && Server.Profile.AppId > 0;
        _btnAnnounce.Enabled = caps.HasFlag(GameCapabilities.Announce) && s.ProcessRunning && (s.ApiOnline || !hasPlayers);
        _btnAnnounce.Visible = caps.HasFlag(GameCapabilities.Announce);
        _tglUpdate.Enabled = Server.Profile.AppId > 0;
    }

    private async Task RunAsync(Func<Task<bool>> action)
    {
        foreach (var b in new[] { _btnStart, _btnStop, _btnRestart, _btnBackup, _btnUpdate }) b.Enabled = false;
        try { await action(); }
        catch (Exception ex) { _uiLog.Error("UI", "작업 실행 오류", ex); }
        finally { _rendered = null; Render(); }
    }

    /// <summary>true=카운트다운, false=즉시, null=취소</summary>
    public static bool? AskCountdown(IWin32Window owner, ServerInstance server, string action)
    {
        var caps = server.Capabilities;
        if (!caps.HasFlag(GameCapabilities.Announce)) return false; // 공지 불가 → 묻지 않음
        var count = server.LastKnownPlayerCount;
        if (count == 0) return false; // 아무도 없으면 즉시

        var countText = caps.HasFlag(GameCapabilities.PlayerList) ? $"{count?.ToString() ?? "알 수 없음"}명" : "확인 불가";
        var r = MessageBox.Show(owner,
            $"[{server.Name}] 현재 접속자: {countText}\n\n" +
            $"[예]  카운트다운 공지({server.Profile.ShutdownCountdown}초) 후 {action}\n" +
            $"[아니요]  공지 없이 바로 저장 후 {action}\n" +
            "[취소]  돌아가기",
            action, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r switch { DialogResult.Yes => true, DialogResult.No => false, _ => null };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
