using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Services;

namespace GameServerKeeper.Core.Server;

public enum ServerState { Stopped, Starting, Running, Stopping, Updating, BackingUp }

public sealed record ServerStatus(
    string ServerId,
    string Name,
    string GameName,
    GameCapabilities Capabilities,
    ServerState State,
    bool ProcessRunning,
    int? Pid,
    long? WorkingSetBytes,
    long MemoryThresholdBytes,
    IReadOnlyList<PlayerInfo>? Players,
    bool ApiOnline,
    TimeSpan? Uptime,
    DateTime? NextScheduledRestart,
    DateTime? LastBackup,
    string? CurrentOperation,
    DateTime? StartTime);

/// <summary>
/// 서버 1대의 운영 오케스트레이터 (게임 중립).
/// - 시작/안전 종료/재시작/백업/업데이트를 한 번에 하나씩만 실행 (서버마다 독립된 lock)
/// - 백그라운드 루프: 메모리 감시, 접속자 조회, 정기 재시작
/// - 크래시 감지 → 자동 재시작 (반복 크래시 시 중단)
/// 게임별 동작(저장/공지/종료/접속자)은 <see cref="IGameServerAdapter"/> 에 위임하고,
/// 어댑터가 지원하지 않는 기능(<see cref="GameCapabilities"/>)은 자동으로 건너뛴다.
/// </summary>
public sealed class ServerInstance : IDisposable
{
    private const string Src = "Server";

    private readonly IAppLog _log;
    private readonly IGameServerAdapter _adapter;
    private readonly GameDefinition _game;
    private readonly ProcessManager _proc;
    private readonly SteamCmdUpdater _updater;
    private readonly BackupManager _backup;
    private readonly DiscordNotifier _discord;

    private readonly SemaphoreSlim _opLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<DateTime> _crashTimes = new();

    private volatile ServerProfile _profile;
    private volatile AppConfig _global;
    private volatile ServerState _state = ServerState.Stopped;
    private volatile string? _currentOp;
    private volatile IReadOnlyList<PlayerInfo>? _players;
    private volatile bool _apiOnline;
    private long _workingSet = -1;
    private DateOnly? _lastScheduledRunDate;
    private int _memoryRestartPending;
    private long _manualActionStamp; // 수동 시작/종료 시 증가 → 대기 중인 크래시 재시작 취소
    private bool _initialized;

    /// <summary>상태 스냅샷 변경 알림 (임의의 스레드에서 호출됨)</summary>
    public event Action<ServerStatus>? StatusChanged;

    public ServerInstance(ServerProfile profile, AppConfig global, Logger rootLog)
    {
        _profile = profile;
        _global = global;
        _game = GameRegistry.Get(profile.GameId);
        _log = rootLog.ForScope(profile.Id, () => _profile.Name);

        _adapter = _game.CreateAdapter(_log);
        _proc = new ProcessManager(_log);
        _updater = new SteamCmdUpdater(_log);
        _backup = new BackupManager(_log);
        _discord = new DiscordNotifier(_log, () => _profile.Name);
        _proc.ServerExited += OnServerExited;

        ApplySettings(profile, global);
    }

    public string Id => _profile.Id;
    public string Name => _profile.Name;
    public ServerProfile Profile => _profile;
    public GameDefinition Game => _game;
    public GameCapabilities Capabilities => _adapter.Capabilities;
    public bool AnnounceSupportsUnicode => _adapter.AnnounceSupportsUnicode;
    public ServerState State => _state;
    public bool IsServerRunning => _proc.IsRunning;
    public bool IsBusy => _currentOp != null;
    public int? LastKnownPlayerCount => _players?.Count;
    public ServerStatus? LatestStatus { get; private set; }

    private string SteamCmdPath =>
        string.IsNullOrWhiteSpace(_profile.SteamCmdPath) ? _global.SteamCmdPath : _profile.SteamCmdPath;

    /// <summary>프로필/전역 설정 적용. 경로·실행 인자 변경은 다음 시작부터 반영된다.</summary>
    public void ApplySettings(ServerProfile profile, AppConfig global)
    {
        if (!string.Equals(profile.GameId, _game.GameId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("서버의 게임 종류는 바꿀 수 없습니다.");

        _profile = profile;
        _global = global;
        _adapter.Configure(profile);
        _discord.WebhookUrl = string.IsNullOrWhiteSpace(profile.DiscordWebhookUrl) ? global.DiscordWebhookUrl : profile.DiscordWebhookUrl;
        _backup.LoadLastBackupTime(profile.BackupDir, profile.BackupPrefix);
        if (_initialized) _log.Info(Src, "설정이 적용되었습니다.");
        RaiseStatus();
    }

    /// <summary>1회 호출: 설정 점검, 기존 서버 연결, 백그라운드 루프 시작, 자동 시작</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var warning in _adapter.CheckConfiguration())
            _log.Warn(Src, warning);

        if (_proc.TryAttachExisting(_profile.ServerInstallDir, _adapter.TrackedProcessName))
            SetState(ServerState.Running);

        StartLoop("Memory", () => TimeSpan.FromSeconds(Math.Max(2, _profile.MemoryCheckIntervalSeconds)), MemoryTickAsync);
        StartLoop("Players", () => TimeSpan.FromSeconds(Math.Max(3, _profile.PlayerPollIntervalSeconds)), PlayerTickAsync);
        StartLoop("Scheduler", () => TimeSpan.FromSeconds(15), SchedulerTickAsync);

        if (_profile.AutoStartOnLaunch && !_proc.IsRunning)
        {
            _log.Info(Src, "설정에 따라 서버를 자동 시작합니다.");
            _ = StartServerAsync();
        }
        RaiseStatus();
    }

    // ═════════════ Public 작업 (UI에서 호출) ═════════════

    public Task<bool> StartServerAsync(bool? update = null) => Task.Run(() =>
    {
        Interlocked.Increment(ref _manualActionStamp);
        lock (_crashTimes) _crashTimes.Clear(); // 수동 시작 시 크래시 카운터 초기화
        return RunExclusiveAsync("서버 시작", ct => StartCoreAsync(update ?? _profile.UpdateBeforeStart, ct));
    });

    public Task<bool> StopServerAsync(bool countdown, string reason = "관리자 종료") => Task.Run(() =>
    {
        Interlocked.Increment(ref _manualActionStamp);
        return RunExclusiveAsync("안전 종료", ct => StopCoreAsync(reason, countdown, ct), TimeSpan.FromMinutes(5));
    });

    public Task<bool> RestartServerAsync(bool countdown, bool backup, bool update, string reason = "관리자 재시작") => Task.Run(() =>
    {
        Interlocked.Increment(ref _manualActionStamp);
        return RunExclusiveAsync("재시작", ct => RestartCoreAsync(reason, countdown, backup, update, ct));
    });

    public Task<bool> BackupNowAsync() => Task.Run(() => RunExclusiveAsync("즉시 백업", BackupNowCoreAsync));

    public Task<bool> UpdateNowAsync() => Task.Run(() => RunExclusiveAsync("수동 업데이트", async ct =>
    {
        if (_proc.IsRunning)
        {
            _log.Warn(Src, "서버 실행 중에는 업데이트할 수 없습니다. 먼저 종료하세요.");
            return false;
        }
        SetState(ServerState.Updating);
        return await UpdateCoreAsync(ct).ConfigureAwait(false);
    }));

    public Task<bool> AnnounceAsync(string message) => Task.Run(() => _adapter.AnnounceAsync(message, _lifetime.Token));

    // ═════════════ 핵심 시퀀스 ═════════════

    private Task<bool> UpdateCoreAsync(CancellationToken ct) =>
        _updater.UpdateAsync(SteamCmdPath, _profile.ServerInstallDir, _profile.AppId, _profile.ValidateOnUpdate, ct);

    private async Task<bool> StartCoreAsync(bool update, CancellationToken ct)
    {
        if (_proc.IsRunning)
        {
            _log.Info(Src, "서버가 이미 실행 중입니다.");
            return true;
        }

        if (update && _profile.AppId > 0)
        {
            SetState(ServerState.Updating);
            if (!await UpdateCoreAsync(ct).ConfigureAwait(false))
                _log.Warn(Src, "업데이트에 실패했지만 기존 버전으로 서버를 시작합니다.");
        }

        SetState(ServerState.Starting);
        _log.Info(Src, "서버 시작 중...");
        var ok = await _proc.StartAsync(_profile.ServerExePath, _adapter.BuildArguments(_profile.ServerArguments), _adapter.TrackedProcessName,
            _profile.HideServerConsole, TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);

        if (!ok)
        {
            SetState(ServerState.Stopped);
            _ = _discord.SendAsync("❌ **서버 시작 실패** — 로그를 확인하세요.");
            return false;
        }

        _players = null;
        SetState(ServerState.Running);
        _log.Info(Src, $"서버 시작 완료 (PID {_proc.ServerPid})");
        _ = _discord.SendAsync($"✅ **서버 시작됨** (PID {_proc.ServerPid})");
        return true;
    }

    /// <summary>
    /// 안전 종료: (접속자 있으면) 카운트다운 공지 → 저장 → 종료 요청 → 대기 → 시간 초과 시에만 Kill.
    /// 게임이 지원하지 않는 단계는 건너뛴다.
    /// </summary>
    private async Task<bool> StopCoreAsync(string reason, bool countdown, CancellationToken ct)
    {
        if (!_proc.IsRunning)
        {
            _log.Info(Src, "서버가 실행 중이 아닙니다.");
            SetState(ServerState.Stopped);
            return true;
        }

        var caps = _adapter.Capabilities;
        var pid = _proc.ServerPid ?? -1;
        _proc.MarkIntentionalStop();
        SetState(ServerState.Stopping);
        _log.Info(Src, $"안전 종료 시작 — 사유: {reason}");

        // 카운트다운 여부: 접속자 조회가 가능하면 1명 이상일 때만, 불가능하면 공지 기능이 있을 때 항상
        bool doCountdown = false;
        if (countdown && caps.HasFlag(GameCapabilities.Announce))
        {
            if (caps.HasFlag(GameCapabilities.PlayerList))
            {
                var players = await _adapter.GetPlayersAsync(ct).ConfigureAwait(false);
                if (players == null) _log.Warn(Src, "서버 응답이 없어 공지 없이 종료합니다.");
                else if (players.Count == 0) _log.Info(Src, "접속자 0명 — 카운트다운 생략");
                doCountdown = players is { Count: > 0 };
            }
            else doCountdown = true;
        }
        if (doCountdown) await CountdownAsync(reason, ct).ConfigureAwait(false);

        // 1) 월드 저장
        if (caps.HasFlag(GameCapabilities.Save))
        {
            if (await _adapter.SaveAsync(ct).ConfigureAwait(false))
            {
                _log.Info(Src, "월드 저장 완료");
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false); // 디스크 flush 여유
            }
            else _log.Warn(Src, "월드 저장 요청 실패");
        }

        // 2) 정상 종료 요청
        bool accepted = false;
        if (caps.HasFlag(GameCapabilities.GracefulShutdown))
        {
            accepted = await _adapter.RequestShutdownAsync(pid, ct).ConfigureAwait(false);
            if (accepted) _log.Info(Src, "정상 종료 요청 전송");
            else _log.Warn(Src, "정상 종료 요청 실패 — 잠시 후 강제 종료합니다.");
        }
        else
        {
            _log.Warn(Src, "이 게임은 정상 종료 방법이 설정되지 않아 강제 종료합니다.");
        }

        // 3) 종료 대기 → 4) 시간 초과 시에만 Kill
        var timeout = TimeSpan.FromSeconds(accepted ? _profile.ShutdownTimeoutSeconds : 3);
        if (await _proc.WaitForExitAsync(timeout, ct).ConfigureAwait(false))
        {
            _log.Info(Src, "서버가 정상 종료되었습니다.");
        }
        else
        {
            if (accepted) _log.Warn(Src, $"{timeout.TotalSeconds:0}초 안에 종료되지 않아 강제 종료(Kill)합니다.");
            _proc.Kill();
            if (!await _proc.WaitForExitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false))
            {
                _log.Error(Src, "강제 종료 후에도 프로세스가 남아 있습니다!");
                _ = _discord.SendAsync("⚠️ **서버 종료 실패** — 프로세스가 남아 있습니다.");
                return false;
            }
        }

        _proc.CleanupLauncher();
        _players = null;
        Interlocked.Exchange(ref _workingSet, -1);
        SetState(ServerState.Stopped);
        _ = _discord.SendAsync($"🛑 **서버 종료됨** — {reason}");
        return true;
    }

    private async Task CountdownAsync(string reason, CancellationToken ct)
    {
        var canListPlayers = _adapter.Capabilities.HasFlag(GameCapabilities.PlayerList);
        var marks = _profile.GetCountdownSeconds();
        for (int i = 0; i < marks.Count; i++)
        {
            // 도중에 모두 나가면 남은 카운트다운 생략
            if (i > 0 && canListPlayers)
            {
                var current = await _adapter.GetPlayersAsync(ct).ConfigureAwait(false);
                if (current is { Count: 0 })
                {
                    _log.Info(Src, "접속자가 모두 나가 카운트다운을 중단합니다.");
                    return;
                }
            }

            string msg;
            try { msg = string.Format(_profile.AnnounceFormat, marks[i], reason); }
            catch (FormatException) { msg = $"[{reason}] Server will shut down in {marks[i]} seconds."; }

            if (await _adapter.AnnounceAsync(msg, ct).ConfigureAwait(false))
                _log.Info(Src, $"공지: {msg}");

            int next = i + 1 < marks.Count ? marks[i + 1] : 0;
            await Task.Delay(TimeSpan.FromSeconds(marks[i] - next), ct).ConfigureAwait(false);
        }
        await _adapter.AnnounceAsync(_adapter.AnnounceSupportsUnicode
            ? "월드를 저장하고 서버를 종료합니다."
            : "Saving the world and shutting down the server.", ct).ConfigureAwait(false);
    }

    private async Task<bool> RestartCoreAsync(string reason, bool countdown, bool backup, bool update, CancellationToken ct)
    {
        _log.Info(Src, $"재시작 시퀀스 시작 — {reason} (백업: {backup}, 업데이트: {update})");
        if (!await StopCoreAsync(reason, countdown, ct).ConfigureAwait(false))
        {
            _log.Error(Src, "서버를 종료하지 못해 재시작을 중단합니다.");
            return false;
        }

        if (backup) await BackupCoreAsync(ct).ConfigureAwait(false);
        return await StartCoreAsync(update, ct).ConfigureAwait(false);
    }

    private async Task<bool> BackupCoreAsync(CancellationToken ct)
    {
        SetState(ServerState.BackingUp);
        try
        {
            var p = _profile;
            var path = await _backup.CreateBackupAsync(p.SaveDir, p.BackupDir, p.BackupPrefix, ct).ConfigureAwait(false);
            _backup.CleanupOldBackups(p.BackupDir, p.BackupPrefix, p.BackupRetentionDays);

            _ = _discord.SendAsync(path != null
                ? $"💾 **백업 완료** — `{Path.GetFileName(path)}`"
                : "⚠️ **백업 실패** — 로그를 확인하세요.");
            return path != null;
        }
        finally
        {
            SetState(_proc.IsRunning ? ServerState.Running : ServerState.Stopped);
        }
    }

    /// <summary>수동 즉시 백업: 실행 중이면 (가능한 경우) 먼저 저장 후 핫 백업</summary>
    private async Task<bool> BackupNowCoreAsync(CancellationToken ct)
    {
        if (_proc.IsRunning)
        {
            if (_adapter.Capabilities.HasFlag(GameCapabilities.Save))
            {
                _log.Info(Src, "서버 실행 중 — 월드 저장 후 핫 백업을 수행합니다.");
                if (await _adapter.SaveAsync(ct).ConfigureAwait(false))
                    await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                else
                    _log.Warn(Src, "저장 요청 실패 — 마지막 자동 저장 기준으로 백업됩니다.");
            }
            else
            {
                _log.Warn(Src, "서버 실행 중 핫 백업 — 이 게임은 저장 요청을 지원하지 않아 마지막 자동 저장 기준입니다.");
            }
        }
        return await BackupCoreAsync(ct).ConfigureAwait(false);
    }

    // ═════════════ 크래시 감지 ═════════════

    private void OnServerExited(object? sender, ServerExitedEventArgs e)
    {
        _players = null;
        _apiOnline = false;
        Interlocked.Exchange(ref _workingSet, -1);

        if (e.Intentional)
        {
            _log.Info(Src, $"서버 프로세스 종료 확인 (PID {e.Pid}, ExitCode {e.ExitCode?.ToString() ?? "?"})");
            RaiseStatus();
            return;
        }

        _log.Error(Src, $"서버가 예기치 않게 종료되었습니다! (PID {e.Pid}, ExitCode {e.ExitCode?.ToString() ?? "?"})");
        _proc.CleanupLauncher();
        if (_currentOp == null) SetState(ServerState.Stopped); else RaiseStatus();
        _ = _discord.SendAsync($"💥 **서버 크래시 감지** (ExitCode {e.ExitCode?.ToString() ?? "?"})");

        if (_profile.AutoRestartOnCrash)
            _ = Task.Run(HandleCrashRestartAsync);
    }

    private async Task HandleCrashRestartAsync()
    {
        var p = _profile;
        int count;
        var now = DateTime.Now;
        lock (_crashTimes)
        {
            _crashTimes.Enqueue(now);
            while (_crashTimes.Count > 0 && _crashTimes.Peek() < now.AddMinutes(-p.CrashWindowMinutes))
                _crashTimes.Dequeue();
            count = _crashTimes.Count;
        }

        if (count > p.MaxCrashRestarts)
        {
            _log.Error(Src, $"{p.CrashWindowMinutes}분 내 {count}회 크래시 — 자동 재시작을 중단합니다. 수동 확인이 필요합니다.");
            _ = _discord.SendAsync($"🚨 **반복 크래시** ({p.CrashWindowMinutes}분 내 {count}회) — 자동 재시작 중단. 확인 필요!");
            return;
        }

        var stamp = Interlocked.Read(ref _manualActionStamp);
        _log.Warn(Src, $"{p.CrashRestartDelaySeconds}초 후 자동 재시작합니다. ({count}/{p.MaxCrashRestarts})");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, p.CrashRestartDelaySeconds)), _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        if (Interlocked.Read(ref _manualActionStamp) != stamp)
        {
            _log.Info(Src, "대기 중 수동 조작이 있어 크래시 자동 재시작을 취소합니다.");
            return;
        }
        if (_proc.IsRunning) return;

        await RunExclusiveAsync("크래시 복구 재시작", ct => StartCoreAsync(false, ct), TimeSpan.FromMinutes(10)).ConfigureAwait(false);
    }

    // ═════════════ 백그라운드 루프 ═════════════

    private void StartLoop(string name, Func<TimeSpan> interval, Func<CancellationToken, Task> tick)
    {
        var ct = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await tick(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { _log.Error(Src, $"{name} 루프 오류", ex); }

                try { await Task.Delay(interval(), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    private Task MemoryTickAsync(CancellationToken ct)
    {
        var p = _profile;
        var ws = _proc.GetWorkingSet();
        Interlocked.Exchange(ref _workingSet, ws ?? -1);
        RaiseStatus();

        if (ws == null || !p.MemoryRestartEnabled || _state != ServerState.Running || _currentOp != null)
            return Task.CompletedTask;
        if (ws.Value < p.MemoryThresholdBytes)
            return Task.CompletedTask;
        if (Interlocked.Exchange(ref _memoryRestartPending, 1) == 1)
            return Task.CompletedTask;

        var gb = ws.Value / 1024.0 / 1024 / 1024;
        _log.Warn(Src, $"메모리 임계치 초과: {gb:0.00} GB ≥ {p.MemoryThresholdGB:0.##} GB — 안전 재시작을 수행합니다.");
        _ = _discord.SendAsync($"🧠 **메모리 임계치 초과** ({gb:0.00} GB) — 안전 재시작");

        // 루프를 막지 않도록 분리 실행. 접속자 0명이면 StopCore 가 카운트다운을 생략한다.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunExclusiveAsync("메모리 초과 재시작",
                    c => RestartCoreAsync("메모리 정리 재시작", countdown: true, backup: p.BackupOnMemoryRestart, update: false, c))
                    .ConfigureAwait(false);
            }
            finally { Interlocked.Exchange(ref _memoryRestartPending, 0); }
        });
        return Task.CompletedTask;
    }

    private async Task PlayerTickAsync(CancellationToken ct)
    {
        if (!_adapter.Capabilities.HasFlag(GameCapabilities.PlayerList)) return;

        if (!_proc.IsRunning || _state == ServerState.Starting)
        {
            if (_apiOnline || _players != null)
            {
                _apiOnline = false;
                _players = null;
                RaiseStatus();
            }
            return;
        }

        var list = await _adapter.GetPlayersAsync(ct).ConfigureAwait(false);
        bool online = list != null;

        if (online && !_apiOnline) _log.Info(Src, "서버 API 연결됨");
        else if (!online && _apiOnline) _log.Warn(Src, "서버 API 응답 없음 (서버 과부하/정지 또는 API 설정 확인)");
        _apiOnline = online;

        if (list != null)
        {
            var old = _players;
            if (old == null)
            {
                _log.Info(Src, $"현재 접속자 {list.Count}명");
            }
            else
            {
                var oldKeys = old.Select(p => p.Id).ToHashSet();
                var newKeys = list.Select(p => p.Id).ToHashSet();
                foreach (var p in list.Where(p => !oldKeys.Contains(p.Id)))
                    _log.Info("Player", $"접속: {p.Name}{(p.Level is int lv ? $" (Lv.{lv})" : "")} — 현재 {list.Count}명");
                foreach (var p in old.Where(p => !newKeys.Contains(p.Id)))
                    _log.Info("Player", $"퇴장: {p.Name} — 현재 {list.Count}명");
            }
            _players = list;
        }
        RaiseStatus();
    }

    private async Task SchedulerTickAsync(CancellationToken ct)
    {
        var p = _profile;
        if (!p.ScheduledRestartEnabled || !p.TryGetScheduledTime(out var t)) return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (_lastScheduledRunDate == today) return;

        // 지정 시각부터 10분 안에서만 실행 (앱을 늦게 켰을 때 엉뚱한 시각에 재시작되지 않도록)
        if (now.TimeOfDay < t || now.TimeOfDay > t + TimeSpan.FromMinutes(10)) return;
        _lastScheduledRunDate = today;

        if (_proc.IsRunning)
        {
            _log.Info(Src, $"정기 재시작 시각({p.ScheduledRestartTime}) 도달 — 안전 종료 → 백업 → 업데이트 → 재시작");
            await RunExclusiveAsync("정기 재시작",
                c => RestartCoreAsync("정기 재시작", countdown: true, p.BackupOnScheduledRestart, p.UpdateOnScheduledRestart, c),
                TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        }
        else if (p.BackupOnScheduledRestart)
        {
            _log.Info(Src, "정기 작업 시각 — 서버가 꺼져 있어 백업만 수행합니다.");
            await RunExclusiveAsync("정기 백업", BackupCoreAsync, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        }
    }

    // ═════════════ 공통 ═════════════

    private async Task<bool> RunExclusiveAsync(string opName, Func<CancellationToken, Task<bool>> body, TimeSpan? wait = null)
    {
        if (!await _opLock.WaitAsync(wait ?? TimeSpan.Zero).ConfigureAwait(false))
        {
            _log.Warn(Src, $"'{_currentOp}' 작업이 진행 중이라 '{opName}' 요청을 처리할 수 없습니다.");
            return false;
        }

        try
        {
            _currentOp = opName;
            RaiseStatus();
            return await body(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Warn(Src, $"'{opName}' 작업이 취소되었습니다.");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(Src, $"'{opName}' 작업 중 오류", ex);
            return false;
        }
        finally
        {
            _currentOp = null;
            SetState(_proc.IsRunning ? ServerState.Running : ServerState.Stopped);
            _opLock.Release();
        }
    }

    private void SetState(ServerState s)
    {
        _state = s;
        RaiseStatus();
    }

    private DateTime? GetNextScheduledRestart()
    {
        var p = _profile;
        if (!p.ScheduledRestartEnabled || !p.TryGetScheduledTime(out var t)) return null;
        var now = DateTime.Now;
        var todayRun = now.Date + t;
        bool doneToday = _lastScheduledRunDate == DateOnly.FromDateTime(now);
        return now < todayRun && !doneToday ? todayRun : todayRun.AddDays(1);
    }

    private void RaiseStatus()
    {
        var p = _profile;
        var ws = Interlocked.Read(ref _workingSet);
        var start = _proc.ServerStartTime;
        var status = new ServerStatus(
            p.Id, p.Name, _game.DisplayName, _adapter.Capabilities,
            _state,
            _proc.IsRunning,
            _proc.ServerPid,
            ws >= 0 ? ws : null,
            p.MemoryThresholdBytes,
            _players,
            _apiOnline,
            start.HasValue ? DateTime.Now - start.Value : null,
            GetNextScheduledRestart(),
            _backup.LastBackupTime,
            _currentOp,
            start);
        LatestStatus = status;
        try { StatusChanged?.Invoke(status); } catch { /* UI 예외 무시 */ }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _proc.ServerExited -= OnServerExited;
        _proc.Dispose(); // 서버 프로세스 자체는 종료하지 않음
        _adapter.Dispose();
        _lifetime.Dispose();
    }
}
