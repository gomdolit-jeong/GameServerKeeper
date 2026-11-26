using System.Collections.Concurrent;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;

namespace GameServerKeeper.UI;

/// <summary>
/// 메인 화면: [전체 현황] 탭 + 서버별 탭.
/// 로그/상태 이벤트는 백그라운드 스레드에서 오므로 큐에 쌓고 UI 타이머로 반영한다.
/// </summary>
public sealed class MainForm : Form
{
    private const string AppTitle = "GameServerKeeper";

    private readonly ServerManager _manager;
    private readonly Logger _log;
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly Dictionary<string, ServerPanel> _panels = new();
    private bool _exitRequested;
    private bool _exiting;
    private int _tick;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
    private readonly TabPage _overviewTab = new("전체 현황");
    private readonly ListView _lvOverview = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false
    };
    private readonly LogView _globalLog = new(includeScope: true);

    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };

    public MainForm(ServerManager manager, Logger logger)
    {
        _manager = manager;
        _log = logger;

        Text = AppTitle;
        Icon = SystemIcons.Application;
        Font = new Font("Malgun Gothic", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(900, 560);

        BuildLayout();
        BuildTray();

        _log.EntryWritten += e => _pendingLogs.Enqueue(e);
        _manager.ServersChanged += () => BeginInvokeSafe(RebuildServerTabs);
        _uiTimer.Tick += (_, _) => OnUiTick();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && Cfg.MinimizeToTray) HideToTray();
        };
    }

    private AppConfig Cfg => _manager.Config;

    // ═════════════ 레이아웃 ═════════════

    private void BuildLayout()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2), ImageScalingSize = new Size(16, 16) };
        toolbar.Items.Add(new ToolStripButton("＋ 서버 추가", null, (_, _) => AddServer()));
        toolbar.Items.Add(new ToolStripButton("✎ 서버 설정", null, (_, _) => EditServer(SelectedServer())));
        toolbar.Items.Add(new ToolStripButton("✕ 서버 삭제", null, (_, _) => RemoveServer(SelectedServer())));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripButton("⚙ 전역 설정", null, (_, _) => EditGlobal()));
        toolbar.Items.Add(new ToolStripButton("📁 로그 폴더", null, (_, _) => Ui.OpenFolder(_log.LogDirectory, _log)));

        _lvOverview.Columns.Add("서버", 160);
        _lvOverview.Columns.Add("게임", 140);
        _lvOverview.Columns.Add("상태", 110);
        _lvOverview.Columns.Add("메모리", 150);
        _lvOverview.Columns.Add("접속자", 70, HorizontalAlignment.Right);
        _lvOverview.Columns.Add("가동 시간", 110);
        _lvOverview.Columns.Add("다음 정기 재시작", 120);
        _lvOverview.Columns.Add("진행 중 작업", 140);
        _lvOverview.DoubleClick += (_, _) => SelectServerTab(OverviewSelectedServer());

        var overviewMenu = new ContextMenuStrip();
        overviewMenu.Items.Add("탭 열기", null, (_, _) => SelectServerTab(OverviewSelectedServer()));
        overviewMenu.Items.Add("서버 설정", null, (_, _) => EditServer(OverviewSelectedServer()));
        _lvOverview.ContextMenuStrip = overviewMenu;

        var listGroup = new GroupBox { Text = "서버 목록 (더블클릭: 상세 탭)", Dock = DockStyle.Fill, Padding = new Padding(6) };
        listGroup.Controls.Add(_lvOverview);
        var logGroup = new GroupBox { Text = "전체 로그", Dock = DockStyle.Fill, Padding = new Padding(6) };
        logGroup.Controls.Add(_globalLog);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
        split.Panel1.Controls.Add(listGroup);
        split.Panel2.Controls.Add(logGroup);
        _overviewTab.Controls.Add(split);
        _overviewTab.HandleCreated += (_, _) => split.SplitterDistance = 220;

        _tabs.TabPages.Add(_overviewTab);
        Controls.Add(_tabs);
        Controls.Add(toolbar);
    }

    private void RebuildServerTabs()
    {
        var selectedId = (_tabs.SelectedTab?.Tag as ServerPanel)?.Server.Id;

        // 삭제된 서버 탭 제거
        foreach (var id in _panels.Keys.Where(id => _manager.Find(id) == null).ToList())
        {
            var panel = _panels[id];
            _tabs.TabPages.Remove((TabPage)panel.Parent!);
            panel.Parent!.Dispose();
            _panels.Remove(id);
        }

        // 새 서버 탭 추가 + 이름 갱신
        foreach (var server in _manager.Servers)
        {
            if (!_panels.TryGetValue(server.Id, out var panel))
            {
                panel = new ServerPanel(server, _manager, _log);
                var page = new TabPage(server.Name) { Tag = panel };
                page.Controls.Add(panel);
                _tabs.TabPages.Add(page);
                _panels[server.Id] = panel;
            }
            ((TabPage)panel.Parent!).Text = server.Name;
        }

        if (selectedId != null && _panels.TryGetValue(selectedId, out var sel))
            _tabs.SelectedTab = (TabPage)sel.Parent!;

        RenderOverview(force: true);
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
            var (stateText, _) = Ui.StateText(s.State);
            var sub = new ToolStripMenuItem($"{s.Name}  —  {stateText}");
            sub.DropDownItems.Add("시작", null, async (_, _) => await s.StartServerAsync()).Enabled = !s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("안전 종료 (카운트다운)", null, async (_, _) => await s.StopServerAsync(true)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("재시작 (카운트다운)", null, async (_, _) => await s.RestartServerAsync(true, false, s.Profile.UpdateBeforeStart)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("즉시 백업", null, async (_, _) => await s.BackupNowAsync()).Enabled = !s.IsBusy;
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add("탭 열기", null, (_, _) => { ShowFromTray(); SelectServerTab(s); });
            _trayMenu.Items.Add(sub);
        }

        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("매니저 종료", null, (_, _) => { _exitRequested = true; Close(); });
    }

    // ═════════════ 수명 주기 ═════════════

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RebuildServerTabs();
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
                "[예]  모든 서버를 안전 종료한 뒤 매니저 종료\n" +
                "[아니요]  서버는 계속 실행하고 매니저만 종료\n" +
                "         (다시 실행하면 자동으로 서버에 연결됩니다)\n" +
                "[취소]  돌아가기",
                "매니저 종료", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

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
        foreach (var p in _panels.Values) p.Render();
        if (++_tick % 4 == 0) RenderOverview(force: false); // 1초마다
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

    private void RenderOverview(bool force)
    {
        // 목록 갱신은 전체 현황 탭이 보일 때만 (트레이 텍스트는 항상)
        bool listVisible = force || (Visible && _tabs.SelectedTab == _overviewTab);
        if (!listVisible)
        {
            UpdateTrayText();
            return;
        }

        var selectedId = OverviewSelectedServer()?.Id;
        _lvOverview.BeginUpdate();
        _lvOverview.Items.Clear();

        foreach (var server in _manager.Servers)
        {
            var s = server.LatestStatus;
            var item = new ListViewItem(server.Name) { Tag = server, UseItemStyleForSubItems = false };
            item.SubItems.Add(server.Game.DisplayName);

            if (s == null)
            {
                for (int i = 0; i < 6; i++) item.SubItems.Add("-");
            }
            else
            {
                var (stateText, stateColor) = Ui.StateText(s.State);
                item.SubItems.Add(stateText).ForeColor = stateColor;

                if (s.WorkingSetBytes is long ws)
                {
                    double ratio = (double)ws / Math.Max(1, s.MemoryThresholdBytes);
                    item.SubItems.Add($"{Ui.Gb(ws)} ({ratio:P0})").ForeColor =
                        ratio >= 0.9 ? Color.Firebrick : ratio >= 0.75 ? Color.DarkOrange : SystemColors.ControlText;
                }
                else item.SubItems.Add("-");

                item.SubItems.Add(!s.Capabilities.HasFlag(GameCapabilities.PlayerList) ? "N/A"
                    : s.Players != null ? s.Players.Count.ToString() : s.ProcessRunning ? "?" : "-");
                item.SubItems.Add(s.ProcessRunning && s.Uptime is { } up ? Ui.Uptime(up) : "-");
                item.SubItems.Add(s.NextScheduledRestart?.ToString("MM-dd HH:mm") ?? "-");
                item.SubItems.Add(s.CurrentOperation ?? "");
            }

            _lvOverview.Items.Add(item);
            if (server.Id == selectedId) item.Selected = true;
        }
        _lvOverview.EndUpdate();
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        var statuses = _manager.Servers.Select(s => s.LatestStatus).Where(s => s != null).ToList();
        int running = statuses.Count(s => s!.ProcessRunning);
        int players = statuses.Sum(s => s!.Players?.Count ?? 0);
        var trayText = $"서버 {running}/{_manager.Servers.Count} 실행 | 접속 {players}명";
        _tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    // ═════════════ 서버 관리 ═════════════

    private ServerInstance? SelectedServer() =>
        (_tabs.SelectedTab?.Tag as ServerPanel)?.Server ?? OverviewSelectedServer();

    private ServerInstance? OverviewSelectedServer() =>
        _lvOverview.SelectedItems.Count > 0 ? _lvOverview.SelectedItems[0].Tag as ServerInstance : null;

    private void SelectServerTab(ServerInstance? server)
    {
        if (server != null && _panels.TryGetValue(server.Id, out var panel))
            _tabs.SelectedTab = (TabPage)panel.Parent!;
    }

    private void AddServer()
    {
        using var pick = new AddServerDialog();
        if (pick.ShowDialog(this) != DialogResult.OK || pick.SelectedGame == null) return;

        var profile = pick.SelectedGame.CreateDefaultProfile();
        if (!string.IsNullOrWhiteSpace(pick.ServerName)) profile.Name = pick.ServerName;

        using var dlg = new PropertyEditForm($"서버 추가 — {pick.SelectedGame.DisplayName}", profile,
            () => _manager.ValidateProfile(profile), ProfileNote(pick.SelectedGame));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var instance = _manager.AddServer(profile);
            SelectServerTab(instance);
        }
        catch (Exception ex) { ShowError("서버 추가 실패", ex); }
    }

    private void EditServer(ServerInstance? server)
    {
        if (server == null)
        {
            MessageBox.Show(this, "편집할 서버 탭을 선택하거나 목록에서 서버를 선택하세요.", AppTitle);
            return;
        }

        var copy = server.Profile.Clone();
        using var dlg = new PropertyEditForm($"서버 설정 — {server.Name}", copy, () => _manager.ValidateProfile(copy), ProfileNote(server.Game));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try { _manager.UpdateServer(copy); }
        catch (Exception ex) { ShowError("설정 저장 실패", ex); }
    }

    private void RemoveServer(ServerInstance? server)
    {
        if (server == null)
        {
            MessageBox.Show(this, "삭제할 서버 탭을 선택하거나 목록에서 서버를 선택하세요.", AppTitle);
            return;
        }

        var r = MessageBox.Show(this,
            $"'{server.Name}' 서버를 매니저에서 삭제할까요?\n\n서버 파일과 백업 파일은 삭제되지 않습니다.",
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
        using var dlg = new PropertyEditForm("전역 설정", copy, copy.ValidateGlobal);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try { _manager.UpdateGlobal(copy); }
        catch (Exception ex) { ShowError("설정 저장 실패", ex); }
    }

    private static string ProfileNote(GameDefinition game) => game.GameId switch
    {
        "palworld" => "※ REST API 포트/AdminPassword 는 PalWorldSettings.ini 와 같아야 합니다. 서버를 여러 대 운영하면 게임 포트(-port)와 REST 포트를 서버마다 다르게 지정하세요.",
        _ => "※ 경로/실행 인자 변경은 다음 서버 시작부터 적용됩니다. 서버를 여러 대 운영하면 포트를 서버마다 다르게 지정하세요."
    };

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
