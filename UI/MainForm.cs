using System.Collections.Concurrent;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>
/// 메인 화면: 왼쪽 사이드바(전체 현황 + 서버 목록) + 오른쪽 내용 영역.
/// 로그/상태 이벤트는 백그라운드 스레드에서 오므로 큐에 쌓고 UI 타이머로 반영한다.
/// </summary>
public sealed class MainForm : Form
{
    private const string AppTitle = "GameServerKeeper";

    private readonly ServerManager _manager;
    private readonly Logger _log;
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly Dictionary<string, ServerPanel> _panels = new();
    private readonly Dictionary<string, NavItem> _serverNavs = new();
    private readonly ToolTip _tip = Theme.CreateToolTip();
    private bool _exitRequested;
    private bool _exiting;
    private int _tick;

    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(28, 22, 28, 24) };
    private readonly Panel _serverList = new() { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, AutoScroll = true };
    private readonly NavItem _navOverview = new("전체 현황", Glyphs.Home) { Dock = DockStyle.Top };
    private readonly Label _lblServerSection = new()
    {
        Dock = DockStyle.Top, Height = 36, ForeColor = Theme.SidebarMuted, Font = Theme.Font(8.5f, FontStyle.Bold),
        Padding = new Padding(22, 14, 0, 0), BackColor = Theme.Sidebar
    };
    private readonly LogView _globalLog = new(includeScope: true);
    private readonly OverviewPage _overview;
    private Control? _currentPage;

    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };

    public MainForm(ServerManager manager, Logger logger)
    {
        _manager = manager;
        _log = logger;

        Text = AppTitle;
        Icon = AppIcon.Get();
        Font = Theme.Font(9.5f);
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1360, 840);
        MinimumSize = new Size(1100, 680);

        _overview = new OverviewPage(_manager, _globalLog, ShowServer, StartServer, StopServer, AddServer);
        _content.Controls.Add(_overview);

        Controls.Add(_content);
        Controls.Add(BuildSidebar());
        BuildTray();

        _log.EntryWritten += e => _pendingLogs.Enqueue(e);
        _manager.ServersChanged += () => BeginInvokeSafe(SyncServers);
        _uiTimer.Tick += (_, _) => OnUiTick();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && Cfg.MinimizeToTray) HideToTray();
        };
    }

    private AppConfig Cfg => _manager.Config;

    // ═════════════ 레이아웃 ═════════════

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = Theme.Sidebar };

        var brand = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Sidebar };
        brand.Controls.Add(new PictureBox { Image = AppIcon.Render(40), Size = new Size(40, 40), Location = new Point(20, 22), SizeMode = PictureBoxSizeMode.Zoom });
        brand.Controls.Add(new Label { Text = AppTitle, ForeColor = Color.White, Font = Theme.Font(11.5f, FontStyle.Bold), AutoSize = true, Location = new Point(70, 22) });
        brand.Controls.Add(new Label { Text = "데디케이트 서버 관리", ForeColor = Theme.SidebarMuted, Font = Theme.Font(8.5f), AutoSize = true, Location = new Point(71, 46) });

        _navOverview.Click += (_, _) => ShowPage(_overview, _navOverview);
        _tip.SetToolTip(_navOverview, "모든 서버의 상태를 한눈에 봐요.");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 3 * 40 + 20, BackColor = Theme.Sidebar, Padding = new Padding(0, 10, 0, 10) };
        var navAdd = new NavItem("서버 추가", Glyphs.Add) { Dock = DockStyle.Top };
        var navGlobal = new NavItem("전역 설정", Glyphs.Settings) { Dock = DockStyle.Top };
        var navLogs = new NavItem("로그 폴더", Glyphs.Folder) { Dock = DockStyle.Top };
        navAdd.Click += (_, _) => AddServer();
        navGlobal.Click += (_, _) => EditGlobal();
        navLogs.Click += (_, _) => Ui.OpenFolder(_log.LogDirectory, _log);
        _tip.SetToolTip(navAdd, "새 데디 서버를 등록해요.");
        _tip.SetToolTip(navGlobal, Theme.WrapTip("SteamCMD 경로, 디스코드 알림, 트레이 동작처럼 모든 서버에 공통인 설정이에요."));
        _tip.SetToolTip(navLogs, "일별 로그 파일(logs\\yyyy-MM-dd.log)이 있는 폴더를 열어요.");
        bottom.Controls.AddRange([navLogs, navGlobal, navAdd]); // Dock=Top: 마지막에 추가한 것이 맨 위
        bottom.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.SidebarHover });

        // 도킹 순서: 나중에 추가한 것이 바깥쪽(위)
        sidebar.Controls.Add(_serverList);
        sidebar.Controls.Add(bottom);
        sidebar.Controls.Add(_lblServerSection);
        sidebar.Controls.Add(_navOverview);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    /// <summary>서버 추가/삭제/이름 변경 시 사이드바·페이지 맞추기</summary>
    private void SyncServers()
    {
        // 삭제됐거나 (게임 종류 변경으로) 인스턴스가 새로 만들어진 서버의 화면 제거
        string? reopenId = null;
        foreach (var id in _panels.Keys.Where(id => !ReferenceEquals(_manager.Find(id), _panels[id].Server)).ToList())
        {
            if (ReferenceEquals(_currentPage, _panels[id]))
            {
                if (_manager.Find(id) != null) reopenId = id;
                ShowPage(_overview, _navOverview);
            }
            _content.Controls.Remove(_panels[id]);
            _panels[id].Dispose();
            _panels.Remove(id);
            _serverList.Controls.Remove(_serverNavs[id]);
            _serverNavs[id].Dispose();
            _serverNavs.Remove(id);
        }

        foreach (var server in _manager.Servers)
        {
            if (_panels.ContainsKey(server.Id)) continue;
            var s = server;
            var panel = new ServerPanel(s, _manager, _log, () => EditServer(s), () => RemoveServer(s)) { Visible = false };
            _content.Controls.Add(panel);
            _panels[s.Id] = panel;

            var nav = new NavItem(s.Name) { Dock = DockStyle.Top };
            nav.Click += (_, _) => ShowServer(s);
            _serverList.Controls.Add(nav);
            nav.BringToFront(); // Dock=Top 순서를 추가 순서대로
            _serverNavs[s.Id] = nav;
        }

        // 사이드바 순서를 서버 목록 순서와 맞춤 (Dock=Top: BringToFront 한 것이 아래로)
        foreach (var server in _manager.Servers)
            if (_serverNavs.TryGetValue(server.Id, out var n)) n.BringToFront();

        _lblServerSection.Text = $"서버  {_manager.Servers.Count}";
        _overview.SyncCards();
        RenderNav();
        if (reopenId != null && _manager.Find(reopenId) is { } reopened) ShowServer(reopened);
    }

    private void ShowPage(Control page, NavItem nav)
    {
        if (_currentPage != null && !ReferenceEquals(_currentPage, page)) _currentPage.Visible = false;
        page.Visible = true;
        page.BringToFront();
        _currentPage = page;
        _navOverview.Selected = ReferenceEquals(nav, _navOverview);
        foreach (var n in _serverNavs.Values) n.Selected = ReferenceEquals(n, nav);
        RenderCurrent();
    }

    private void ShowServer(ServerInstance server)
    {
        if (_panels.TryGetValue(server.Id, out var panel) && _serverNavs.TryGetValue(server.Id, out var nav))
            ShowPage(panel, nav);
    }

    private void BuildTray()
    {
        _tray.Icon = Icon;
        _tray.Text = AppTitle;
        _tray.ContextMenuStrip = _trayMenu;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _trayMenu.Opening += (_, _) => BuildTrayMenu();
        BuildTrayMenu();
    }

    /// <summary>트레이 메뉴는 열 때마다 서버 목록으로 다시 만든다</summary>
    private void BuildTrayMenu()
    {
        _trayMenu.Items.Clear();
        _trayMenu.Items.Add("열기", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());

        foreach (var server in _manager.Servers)
        {
            var s = server;
            var sub = new ToolStripMenuItem($"{s.Name}  —  {Theme.State(s.State).Text}");
            sub.DropDownItems.Add("시작", null, async (_, _) => await s.StartServerAsync()).Enabled = !s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("안전 종료 (카운트다운)", null, async (_, _) => await s.StopServerAsync(true)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("재시작 (카운트다운)", null, async (_, _) => await s.RestartServerAsync(true, false, s.Profile.UpdateBeforeStart)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("즉시 백업", null, async (_, _) => await s.BackupNowAsync()).Enabled = !s.IsBusy;
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add("화면 열기", null, (_, _) => { ShowFromTray(); ShowServer(s); });
            _trayMenu.Items.Add(sub);
        }

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("프로그램 종료", null, (_, _) => { _exitRequested = true; Close(); });
    }

    // ═════════════ 수명 주기 ═════════════

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SyncServers();
        ShowPage(_overview, _navOverview);
        _uiTimer.Start();
        _manager.Initialize();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Cfg.StartMinimized) HideToTray();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_exiting)
        {
            base.OnFormClosing(e);
            return;
        }

        // Windows 종료/로그오프 시에는 비동기 처리를 할 수 없으므로 그대로 닫는다
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _exiting = true;
            _tray.Visible = false;
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing && Cfg.CloseToTray)
        {
            HideToTray();
            return;
        }

        _exitRequested = false;
        _ = ExitApplicationAsync();
    }

    private async Task ExitApplicationAsync()
    {
        var running = _manager.RunningServers.ToList();
        if (running.Count > 0)
        {
            ShowFromTray();
            var r = MessageBox.Show(this,
                $"실행 중인 서버 {running.Count}대: {string.Join(", ", running.Select(s => s.Name))}\n\n" +
                "[예]  모든 서버를 안전 종료한 뒤 프로그램 종료\n" +
                "[아니요]  서버는 계속 실행하고 프로그램만 종료\n" +
                "         (다시 실행하면 자동으로 서버에 연결돼요)\n" +
                "[취소]  돌아가기",
                "프로그램 종료", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes)
            {
                Enabled = false;
                Text = $"{AppTitle} — 서버 종료 중...";
                await _manager.StopAllAsync("서버 관리자 종료");
            }
        }

        _exiting = true;
        _uiTimer.Stop();
        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer.Dispose();
            _tray.Dispose();
            _trayMenu.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void BeginInvokeSafe(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    // ═════════════ 렌더링 ═════════════

    private void OnUiTick()
    {
        FlushLogs();
        RenderCurrent();
        if (++_tick % 4 == 0) RenderNav(); // 1초마다
    }

    private void RenderCurrent()
    {
        if (!Visible) return;
        if (ReferenceEquals(_currentPage, _overview)) _overview.Render();
        else if (_currentPage is ServerPanel panel) panel.Render();
    }

    private void RenderNav()
    {
        int running = 0, players = 0;
        foreach (var server in _manager.Servers)
        {
            if (!_serverNavs.TryGetValue(server.Id, out var nav)) continue;
            var s = server.LatestStatus;
            var (stateText, fg, _) = Theme.State(s?.State ?? ServerState.Stopped);
            if (s?.CurrentOperation != null) { stateText = s.CurrentOperation; fg = Theme.Warning; }
            string? badge = s?.Players != null ? $"{s.Players.Count}명" : null;
            nav.Update(server.Name, $"{server.Game.DisplayName} · {stateText}", fg, badge);
            if (s?.ProcessRunning == true) running++;
            players += s?.Players?.Count ?? 0;
        }

        var trayText = $"{AppTitle}: 서버 {running}/{_manager.Servers.Count} 실행 · 접속 {players}명";
        _tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    private void FlushLogs()
    {
        if (_pendingLogs.IsEmpty) return;

        var touched = new HashSet<LogView>();
        int count = 0;
        while (count++ < 500 && _pendingLogs.TryDequeue(out var e))
        {
            if (e.Level == LogLevel.Debug) continue; // Debug 는 파일에만

            _globalLog.Append(e);
            touched.Add(_globalLog);
            if (e.ScopeId != null && _panels.TryGetValue(e.ScopeId, out var panel))
            {
                panel.LogView.Append(e);
                touched.Add(panel.LogView);
            }

            if (e.Level == LogLevel.Error && !Visible)
                _tray.ShowBalloonTip(5000, e.ScopeName ?? AppTitle, e.Message, ToolTipIcon.Error);
        }
        foreach (var v in touched) v.Commit();
    }

    // ═════════════ 서버 관리 ═════════════

    private async void StartServer(ServerInstance server) => await server.StartServerAsync();

    private async void StopServer(ServerInstance server)
    {
        var countdown = ServerPanel.AskCountdown(this, server, "안전 종료");
        if (countdown != null) await server.StopServerAsync(countdown.Value);
    }

    /// <summary>설정 화면에서 숨길 항목을 정하기 위해 현재 설정 기준의 지원 기능을 계산</summary>
    private static GameCapabilities CapabilitiesOf(ServerProfile profile)
    {
        using var adapter = GameRegistry.Get(profile.GameId).CreateAdapter(NullLog.Instance);
        adapter.Configure(profile);
        return adapter.Capabilities;
    }

    private static string SettingsSubtitle(GameDefinition game) =>
        $"{game.DisplayName}   ·   " + (string.IsNullOrEmpty(game.SettingsNote)
            ? "경로·실행 인자 변경은 다음 서버 시작부터 적용돼요."
            : game.SettingsNote);

    private void AddServer()
    {
        using var pick = new AddServerDialog();
        if (pick.ShowDialog(this) != DialogResult.OK) return;

        ServerProfile profile;
        try { profile = pick.DetectedGame.CreateProfile(string.IsNullOrWhiteSpace(pick.ExePath) ? null : pick.ExePath); }
        catch { profile = GameRegistry.Generic.CreateProfile(null); }
        if (!string.IsNullOrWhiteSpace(pick.ServerName)) profile.Name = pick.ServerName;

        // 같은 백업 폴더에 같은 게임 서버가 이미 있으면 백업 파일 이름이 겹치지 않게 서버 이름으로
        if (_manager.Config.Servers.Any(s => string.Equals(s.BackupDir, profile.BackupDir, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(s.BackupPrefix, profile.BackupPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var safe = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
            profile.BackupPrefix = $"{safe}_Backup";
        }

        using var dlg = new SettingsForm("서버 추가", () => SettingsSubtitle(GameRegistry.Get(profile.GameId)), profile,
            () => _manager.ValidateProfile(profile), () => CapabilitiesOf(profile));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var instance = _manager.AddServer(profile);
            ShowServer(instance);
        }
        catch (Exception ex) { ShowError("서버 추가 실패", ex); }
    }

    private void EditServer(ServerInstance server)
    {
        var copy = server.Profile.Clone();
        using var dlg = new SettingsForm($"서버 설정 — {server.Name}", () => SettingsSubtitle(GameRegistry.Get(copy.GameId)), copy,
            () => _manager.ValidateProfile(copy), () => CapabilitiesOf(copy),
            allowGameChange: !server.IsServerRunning && !server.IsBusy);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try { _manager.UpdateServer(copy); }
        catch (Exception ex) { ShowError("설정 저장 실패", ex); }
    }

    private void RemoveServer(ServerInstance server)
    {
        var r = MessageBox.Show(this,
            $"'{server.Name}' 서버를 목록에서 삭제할까요?\n\n서버 파일과 백업 파일은 삭제되지 않아요.",
            "서버 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r != DialogResult.Yes) return;

        try
        {
            if (!_manager.RemoveServer(server.Id, out var reason))
                MessageBox.Show(this, reason, "서버 삭제", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError("서버 삭제 실패", ex); }
    }

    private void EditGlobal()
    {
        var copy = Cfg.Clone();
        using var dlg = new SettingsForm("전역 설정", () => "모든 서버에 공통으로 적용되는 설정이에요.", copy, copy.ValidateGlobal);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try { _manager.UpdateGlobal(copy); }
        catch (Exception ex) { ShowError("설정 저장 실패", ex); }
    }

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
