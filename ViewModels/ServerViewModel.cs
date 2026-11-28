using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;
using GameServerKeeper.Services;

namespace GameServerKeeper.ViewModels;

/// <summary>서버 화면 밖에서 처리해야 하는 동작 (페이지 전환, 설정 창, 삭제)</summary>
public interface IServerHost
{
    void Open(ServerViewModel server);
    void Edit(ServerViewModel server);
    void Remove(ServerViewModel server);
}

public sealed record PlayerRow(string Name, string Level, string Ping, string Id);

/// <summary>
/// 서버 1대. 서버 상세 화면, 전체 현황의 카드, 사이드바 항목이 모두 이 ViewModel 에 바인딩된다.
/// <see cref="Refresh"/> 를 UI 타이머가 주기적으로 호출해 <see cref="ServerInstance.LatestStatus"/> 를 화면 값으로 옮긴다.
/// </summary>
public sealed partial class ServerViewModel : ObservableObject
{
    private readonly ServerManager _manager;
    private readonly IAppLog _uiLog;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly IServerHost _host;
    private ServerStatus? _rendered;
    private bool _working; // 이 화면에서 요청한 작업이 진행 중

    public ServerViewModel(ServerInstance server, ServerManager manager, IAppLog uiLog,
        IDialogService dialogs, IShellService shell, IServerHost host)
    {
        Server = server;
        _manager = manager;
        _uiLog = uiLog;
        _dialogs = dialogs;
        _shell = shell;
        _host = host;
        Log = new LogViewModel(includeScope: false, shell);
        _name = server.Name;
        _gameName = server.Game.DisplayName;
        Refresh();
    }

    /// <summary>화면 낭독기 등 접근성 이름</summary>
    public override string ToString() => Name;

    public ServerInstance Server { get; }
    public string Id => Server.Id;
    public LogViewModel Log { get; }
    public ObservableCollection<PlayerRow> Players { get; } = new();

    // ── 머리글 / 사이드바 ──
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _gameName;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _stateText = "중지됨";
    [ObservableProperty] private Tone _stateTone = Tone.Neutral;
    [ObservableProperty] private string _navStatus = "";
    [ObservableProperty] private Tone _navTone = Tone.Neutral;
    [ObservableProperty] private string? _playerBadge;

    // ── 상태 ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isIdle = true;
    [ObservableProperty] private bool _showAnnounce;
    [ObservableProperty] private bool _canToggleUpdate;

    // ── 상태 타일 ──
    [ObservableProperty] private StatInfo _memoryStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _playersStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _uptimeStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _apiStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _nextRestartStat = StatInfo.Empty;
    [ObservableProperty] private StatInfo _lastBackupStat = StatInfo.Empty;

    // ── 카드 (전체 현황) ──
    [ObservableProperty] private string _memoryText = "-";
    [ObservableProperty] private double _memoryRatio;
    [ObservableProperty] private Tone _memoryTone = Tone.Purple;
    [ObservableProperty] private string _cardSummary = "";

    // ── 접속자 목록 ──
    [ObservableProperty] private bool _hasPlayerRows;
    [ObservableProperty] private string _emptyPlayersText = "";

    /// <summary>"시작 전 업데이트" 토글. 바꾸면 바로 설정에 저장한다.</summary>
    public bool UpdateBeforeStart
    {
        get => Server.Profile.UpdateBeforeStart;
        set
        {
            if (Server.Profile.UpdateBeforeStart == value) return;
            var updated = Server.Profile.Clone();
            updated.UpdateBeforeStart = value;
            try { _manager.UpdateServer(updated); }
            catch (Exception ex) { _uiLog.Error("UI", "설정 저장 실패", ex); }
            OnPropertyChanged();
        }
    }

    // ═════════════ 상태 반영 ═════════════

    public void Refresh()
    {
        var s = Server.LatestStatus;
        if (s == null) return;

        // 가동 시간은 매번 갱신
        TimeSpan? uptime = s.ProcessRunning && s.StartTime is DateTime st ? DateTime.Now - st : null;
        UptimeStat = uptime is { } up
            ? new StatInfo(Format.Uptime(up), $"{s.StartTime:MM-dd HH:mm} 시작")
            : new StatInfo("-", "서버가 꺼져 있어요");
        CardSummary = $"{CardPlayers(s)}   ·   {(uptime is { } u ? $"가동 {Format.Uptime(u)}" : "꺼져 있음")}";

        if (ReferenceEquals(s, _rendered)) return;
        _rendered = s;

        var caps = s.Capabilities;
        bool hasPlayers = caps.HasFlag(GameCapabilities.PlayerList);

        // 머리글 · 사이드바
        Name = s.Name;
        GameName = s.GameName;
        var (stateText, tone) = Format.State(s.State);
        if (s.CurrentOperation != null) { StateText = $"{s.CurrentOperation} 진행 중"; StateTone = Tone.Warning; }
        else { StateText = stateText; StateTone = tone; }
        NavStatus = $"{s.GameName} · {s.CurrentOperation ?? stateText}";
        NavTone = s.CurrentOperation != null ? Tone.Warning : tone;
        PlayerBadge = s.Players != null ? $"{s.Players.Count}명" : null;
        Subtitle = s.ProcessRunning ? $"{s.GameName}   ·   PID {s.Pid}" : $"{s.GameName}   ·   서버가 꺼져 있어요";

        // 메모리
        if (s.WorkingSetBytes is long ws)
        {
            double ratio = (double)ws / Math.Max(1, s.MemoryThresholdBytes);
            MemoryStat = new StatInfo(Format.Gb(ws), $"기준 {Format.Gb(s.MemoryThresholdBytes)}의 {ratio:P0}", Progress: ratio, ProgressTone: Format.MemoryTone(ratio));
            MemoryText = $"{Format.Gb(ws)} / {Format.Gb(s.MemoryThresholdBytes)}";
            MemoryRatio = Math.Min(1, ratio);
            MemoryTone = Format.MemoryTone(ratio);
        }
        else
        {
            MemoryStat = new StatInfo("-", $"기준 {Format.Gb(s.MemoryThresholdBytes)}", Progress: 0);
            MemoryText = "-";
            MemoryRatio = 0;
            MemoryTone = Tone.Purple;
        }

        // 타일
        PlayersStat = !hasPlayers ? new StatInfo("지원 안 함", "이 게임은 접속자 조회 불가", Tone.Faint)
            : s.Players != null ? new StatInfo($"{s.Players.Count}명")
            : s.ProcessRunning ? new StatInfo("확인 중", "서버 응답을 기다리는 중", Tone.Warning)
            : new StatInfo("-", "서버가 꺼져 있어요");

        ApiStat = !hasPlayers ? new StatInfo("없음", "이 게임은 관리 API 미지원", Tone.Faint)
            : !s.ProcessRunning ? new StatInfo("-", "서버가 꺼져 있어요")
            : s.ApiOnline ? new StatInfo("연결됨", "공지·저장 가능", Tone.Success)
            : new StatInfo("응답 없음", "시작 중이거나 API 설정 확인", Tone.Warning);

        NextRestartStat = s.NextScheduledRestart is DateTime next
            ? new StatInfo(Format.Upcoming(next), "안전 종료 → 백업 → 업데이트")
            : new StatInfo("사용 안 함", "서버 설정에서 켤 수 있어요", Tone.Faint);

        LastBackupStat = s.LastBackup is DateTime lb
            ? new StatInfo(Format.Relative(lb), lb.ToString("yyyy-MM-dd HH:mm"))
            : new StatInfo("없음", "아직 백업이 없어요", Tone.Faint);

        // 접속자 목록
        Players.Clear();
        foreach (var p in (s.Players ?? Array.Empty<PlayerInfo>()).OrderBy(p => p.Name))
            Players.Add(new PlayerRow(p.Name, p.Level?.ToString() ?? "", p.Ping is double ping ? $"{ping:0}" : "", p.Id));
        HasPlayerRows = Players.Count > 0;
        EmptyPlayersText = !hasPlayers ? "이 게임은 접속자 조회를 지원하지 않아요"
            : !s.ProcessRunning ? "서버가 꺼져 있어요"
            : s.Players == null ? "서버 응답을 기다리는 중이에요" : "접속한 플레이어가 없어요";

        // 버튼
        IsRunning = s.ProcessRunning;
        IsIdle = s.CurrentOperation == null;
        ShowAnnounce = caps.HasFlag(GameCapabilities.Announce);
        _canAnnounce = ShowAnnounce && s.ProcessRunning && (s.ApiOnline || !hasPlayers);
        CanToggleUpdate = Server.Profile.AppId > 0;
        UpdateCommands();
    }

    private static string CardPlayers(ServerStatus s) =>
        !s.Capabilities.HasFlag(GameCapabilities.PlayerList) ? "접속자 조회 불가"
        : s.Players != null ? $"접속자 {s.Players.Count}명"
        : s.ProcessRunning ? "접속자 확인 중" : "접속자 -";

    private void UpdateCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        BackupCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        AnnounceCommand.NotifyCanExecuteChanged();
    }

    // ═════════════ 명령 ═════════════

    private bool _canAnnounce;
    private bool CanStart() => IsIdle && !IsRunning && !_working;
    private bool CanStopOrRestart() => IsIdle && IsRunning && !_working;
    private bool CanBackup() => IsIdle && !_working;
    private bool CanUpdate() => IsIdle && !IsRunning && !_working && Server.Profile.AppId > 0;
    private bool CanAnnounce() => _canAnnounce;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunAsync(() => Server.StartServerAsync(UpdateBeforeStart));

    [RelayCommand(CanExecute = nameof(CanStopOrRestart))]
    private async Task StopAsync()
    {
        var countdown = AskCountdown("안전 종료");
        if (countdown != null) await RunAsync(() => Server.StopServerAsync(countdown.Value));
    }

    [RelayCommand(CanExecute = nameof(CanStopOrRestart))]
    private async Task RestartAsync()
    {
        var countdown = AskCountdown("재시작");
        if (countdown != null)
            await RunAsync(() => Server.RestartServerAsync(countdown.Value, backup: false, update: UpdateBeforeStart));
    }

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private Task BackupAsync() => RunAsync(Server.BackupNowAsync);

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private Task UpdateAsync() => RunAsync(Server.UpdateNowAsync);

    [RelayCommand(CanExecute = nameof(CanAnnounce))]
    private async Task AnnounceAsync()
    {
        var caption = $"{Name}에 접속한 플레이어에게 보낼 메시지를 입력하세요." +
                      (Server.AnnounceSupportsUnicode ? "" : "\n이 게임은 한글이 깨져서 영어로 입력해 주세요.");
        var msg = _dialogs.Prompt("인게임 공지", caption, "보내기");
        if (string.IsNullOrWhiteSpace(msg)) return;
        var ok = await Server.AnnounceAsync(msg);
        _uiLog.Info("UI", ok ? $"[{Name}] 공지 전송: {msg}" : $"[{Name}] 공지 전송 실패");
    }

    [RelayCommand]
    private void OpenBackupFolder() => _shell.OpenFolder(Server.Profile.BackupDir);

    [RelayCommand]
    private void Open() => _host.Open(this);

    [RelayCommand]
    private void Edit() => _host.Edit(this);

    [RelayCommand]
    private void Remove() => _host.Remove(this);

    private async Task RunAsync(Func<Task<bool>> action)
    {
        _working = true;
        UpdateCommands();
        try { await action(); }
        catch (Exception ex) { _uiLog.Error("UI", "작업 실행 오류", ex); }
        finally
        {
            _working = false;
            _rendered = null;
            Refresh();
        }
    }

    /// <summary>true=카운트다운, false=즉시, null=취소</summary>
    private bool? AskCountdown(string action)
    {
        var caps = Server.Capabilities;
        if (!caps.HasFlag(GameCapabilities.Announce)) return false; // 공지 불가 → 묻지 않음
        var count = Server.LastKnownPlayerCount;
        if (count == 0) return false; // 아무도 없으면 즉시

        var countText = caps.HasFlag(GameCapabilities.PlayerList) ? $"{count?.ToString() ?? "알 수 없음"}명" : "확인 불가";
        var choice = _dialogs.Choose(action, $"[{Name}] 현재 접속자: {countText}",
        [
            new($"카운트다운 후 {action}", $"{Server.Profile.ShutdownCountdown}초 카운트다운 공지를 보낸 뒤 저장하고 {action}해요.", ChoiceKind.Primary),
            new($"바로 {action}", $"공지 없이 바로 저장하고 {action}해요.", ChoiceKind.Danger),
            new("취소"),
        ]);
        return choice switch { 0 => true, 1 => false, _ => null };
    }
}
