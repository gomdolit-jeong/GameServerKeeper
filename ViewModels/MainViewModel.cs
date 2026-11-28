using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;
using GameServerKeeper.Services;

namespace GameServerKeeper.ViewModels;

/// <summary>
/// 메인 창: 사이드바(전체 현황 + 서버 목록) + 내용 영역.
/// 로그/상태 이벤트는 백그라운드 스레드에서 오므로 큐에 쌓고, View 의 UI 타이머가 <see cref="Tick"/> 으로 반영한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IServerHost
{
    public const string AppTitle = "GameServerKeeper";

    private readonly ServerManager _manager;
    private readonly Logger _log;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly SynchronizationContext? _ui;
    private int _tick;

    public MainViewModel(ServerManager manager, Logger log, IDialogService dialogs, IShellService shell)
    {
        _manager = manager;
        _log = log;
        _dialogs = dialogs;
        _shell = shell;
        _ui = SynchronizationContext.Current;

        GlobalLog = new LogViewModel(includeScope: true, shell);
        Overview = new OverviewViewModel(this);
        _currentPage = Overview;

        _log.EntryWritten += e => _pendingLogs.Enqueue(e);
        _manager.ServersChanged += () => Post(SyncServers);
        SyncServers();
        Overview.Refresh();
    }

    public ServerManager Manager => _manager;
    public AppConfig Config => _manager.Config;
    public ObservableCollection<ServerViewModel> Servers { get; } = new();
    public OverviewViewModel Overview { get; }
    public LogViewModel GlobalLog { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    private object _currentPage;

    /// <summary>사이드바에서 고른 서버. null 이면 전체 현황.</summary>
    [ObservableProperty] private ServerViewModel? _selectedServer;

    partial void OnSelectedServerChanged(ServerViewModel? value)
    {
        CurrentPage = value ?? (object)Overview;
        value?.Refresh();
    }

    public bool IsOverviewSelected => ReferenceEquals(CurrentPage, Overview);

    [ObservableProperty] private string _serverSectionTitle = "서버  0";
    [ObservableProperty] private string _trayText = AppTitle;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isShuttingDown;

    public string WindowTitle => IsShuttingDown ? $"{AppTitle} — 서버 종료 중..." : AppTitle;

    /// <summary>오류 로그가 기록됨 (창이 숨겨져 있으면 트레이 알림)</summary>
    public event Action<LogEntry>? ErrorLogged;

    /// <summary>질문을 해야 해서 창을 보여 줘야 할 때</summary>
    public event Action? ShowWindowRequested;

    public void Initialize() => _manager.Initialize();

    private void Post(Action action)
    {
        if (_ui == null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    // ═════════════ 주기 갱신 ═════════════

    /// <param name="visible">창이 보이는지. 숨겨져 있으면 화면 갱신은 1초에 한 번만.</param>
    public void Tick(bool visible)
    {
        FlushLogs();
        bool everySecond = ++_tick % 4 == 0;
        if (visible || everySecond)
        {
            foreach (var s in Servers) s.Refresh();
            if (IsOverviewSelected) Overview.Refresh();
        }
        if (everySecond) UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        int running = 0, players = 0;
        foreach (var server in _manager.Servers)
        {
            var s = server.LatestStatus;
            if (s?.ProcessRunning == true) running++;
            players += s?.Players?.Count ?? 0;
        }
        var text = $"{AppTitle}: 서버 {running}/{_manager.Servers.Count} 실행 · 접속 {players}명";
        TrayText = text.Length > 63 ? text[..63] : text;
    }

    private void FlushLogs()
    {
        if (_pendingLogs.IsEmpty) return;

        var touched = new HashSet<LogViewModel>();
        int count = 0;
        while (count++ < 500 && _pendingLogs.TryDequeue(out var e))
        {
            if (e.Level == LogLevel.Debug) continue; // Debug 는 파일에만

            GlobalLog.Append(e);
            touched.Add(GlobalLog);
            if (e.ScopeId != null && Servers.FirstOrDefault(v => v.Id == e.ScopeId) is { } server)
            {
                server.Log.Append(e);
                touched.Add(server.Log);
            }
            if (e.Level == LogLevel.Error) ErrorLogged?.Invoke(e);
        }
        foreach (var l in touched) l.Trim();
    }

    /// <summary>서버 추가/삭제/게임 변경 시 목록 맞추기</summary>
    private void SyncServers()
    {
        // 삭제됐거나 (게임 종류 변경으로) 인스턴스가 새로 만들어진 서버 제거
        string? reopenId = null;
        foreach (var vm in Servers.Where(v => !ReferenceEquals(_manager.Find(v.Id), v.Server)).ToList())
        {
            if (ReferenceEquals(SelectedServer, vm))
            {
                if (_manager.Find(vm.Id) != null) reopenId = vm.Id;
                SelectedServer = null;
            }
            Servers.Remove(vm);
        }

        // 추가 + 순서 맞추기
        for (int i = 0; i < _manager.Servers.Count; i++)
        {
            var server = _manager.Servers[i];
            var vm = Servers.FirstOrDefault(v => ReferenceEquals(v.Server, server));
            if (vm == null) Servers.Insert(i, new ServerViewModel(server, _manager, _log, _dialogs, _shell, this));
            else if (Servers.IndexOf(vm) != i) Servers.Move(Servers.IndexOf(vm), i);
        }

        ServerSectionTitle = $"서버  {Servers.Count}";
        Overview.SyncCards();
        if (reopenId != null) SelectedServer = Servers.FirstOrDefault(v => v.Id == reopenId);
    }

    // ═════════════ 화면 전환 ═════════════

    [RelayCommand]
    private void ShowOverview()
    {
        SelectedServer = null;
        Overview.Refresh();
    }

    public void Open(ServerViewModel server) => SelectedServer = server;

    [RelayCommand]
    private void OpenLogFolder() => _shell.OpenFolder(_log.LogDirectory);

    // ═════════════ 서버 관리 ═════════════

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

    [RelayCommand]
    private void AddServer()
    {
        var pick = new AddServerViewModel(_dialogs);
        if (!_dialogs.ShowAddServer(pick)) return;

        ServerProfile profile;
        try { profile = pick.DetectedGame.CreateProfile(string.IsNullOrWhiteSpace(pick.CleanExePath) ? null : pick.CleanExePath); }
        catch { profile = GameRegistry.Generic.CreateProfile(null); }
        if (!string.IsNullOrWhiteSpace(pick.ServerName)) profile.Name = pick.ServerName.Trim();

        // 같은 백업 폴더에 같은 게임 서버가 이미 있으면 백업 파일 이름이 겹치지 않게 서버 이름으로
        if (Config.Servers.Any(s => string.Equals(s.BackupDir, profile.BackupDir, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(s.BackupPrefix, profile.BackupPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var safe = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
            profile.BackupPrefix = $"{safe}_Backup";
        }

        var settings = new SettingsViewModel("서버 추가", () => SettingsSubtitle(GameRegistry.Get(profile.GameId)), profile,
            () => _manager.ValidateProfile(profile), _dialogs, () => CapabilitiesOf(profile));
        if (!_dialogs.ShowSettings(settings)) return;

        try
        {
            var instance = _manager.AddServer(profile);
            SyncServers();
            SelectedServer = Servers.FirstOrDefault(v => ReferenceEquals(v.Server, instance));
        }
        catch (Exception ex) { _dialogs.Alert("서버 추가 실패", ex.Message); }
    }

    public void Edit(ServerViewModel server)
    {
        var instance = server.Server;
        var copy = instance.Profile.Clone();
        var settings = new SettingsViewModel($"서버 설정 — {instance.Name}", () => SettingsSubtitle(GameRegistry.Get(copy.GameId)), copy,
            () => _manager.ValidateProfile(copy), _dialogs, () => CapabilitiesOf(copy),
            allowGameChange: !instance.IsServerRunning && !instance.IsBusy);
        if (!_dialogs.ShowSettings(settings)) return;

        try
        {
            _manager.UpdateServer(copy);
            SyncServers();
        }
        catch (Exception ex) { _dialogs.Alert("설정 저장 실패", ex.Message); }
    }

    public void Remove(ServerViewModel server)
    {
        var choice = _dialogs.Choose("서버 삭제",
            $"'{server.Name}' 서버를 목록에서 삭제할까요?\n서버 파일과 백업 파일은 삭제되지 않아요.",
            [new("삭제", null, ChoiceKind.Danger), new("취소")]);
        if (choice != 0) return;

        try
        {
            if (!_manager.RemoveServer(server.Id, out var reason))
                _dialogs.Alert("서버 삭제", reason ?? "삭제할 수 없어요.");
            SyncServers();
        }
        catch (Exception ex) { _dialogs.Alert("서버 삭제 실패", ex.Message); }
    }

    [RelayCommand]
    private void EditGlobal()
    {
        var copy = Config.Clone();
        var settings = new SettingsViewModel("전역 설정", () => "모든 서버에 공통으로 적용되는 설정이에요.", copy, copy.ValidateGlobal, _dialogs);
        if (!_dialogs.ShowSettings(settings)) return;

        try { _manager.UpdateGlobal(copy); }
        catch (Exception ex) { _dialogs.Alert("설정 저장 실패", ex.Message); }
    }

    // ═════════════ 종료 ═════════════

    /// <summary>프로그램을 끝내도 되는지 묻고, 필요하면 서버를 모두 안전 종료한다. 끝내도 되면 true.</summary>
    public async Task<bool> PrepareExitAsync()
    {
        var running = _manager.RunningServers.ToList();
        if (running.Count == 0) return true;

        ShowWindowRequested?.Invoke();
        var choice = _dialogs.Choose("프로그램 종료",
            $"실행 중인 서버 {running.Count}대: {string.Join(", ", running.Select(s => s.Name))}",
            [
                new("모든 서버 안전 종료 후 끝내기", "서버를 모두 안전 종료한 뒤 프로그램을 닫아요.", ChoiceKind.Primary),
                new("프로그램만 끝내기", "서버는 계속 실행돼요. 다시 실행하면 자동으로 서버에 연결돼요."),
                new("취소"),
            ]);

        if (choice == 1) return true;
        if (choice != 0) return false;

        IsShuttingDown = true;
        await _manager.StopAllAsync("서버 관리자 종료");
        return true;
    }
}
