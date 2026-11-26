using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using GameServerKeeper.Core.Games;

namespace GameServerKeeper.Core.Config;

/// <summary>
/// 서버 1개의 설정. 게임 공통 항목 + 게임 전용 설정(<see cref="Game"/>).
/// Category/DisplayName/Description 은 설정 창(PropertyGrid)에 그대로 표시된다.
/// </summary>
public sealed class ServerProfile
{
    private const string CatBasic = "1. 기본";
    private const string CatGame = "2. 게임 전용";
    private const string CatPath = "3. 경로 / 실행";
    private const string CatControl = "4. 종료 / 업데이트";
    private const string CatCrash = "5. 크래시 / 메모리";
    private const string CatSchedule = "6. 정기 재시작 / 백업";

    // ───────────── 1. 기본 ─────────────
    [Category(CatBasic), DisplayName("Id"), Description("내부 식별자 (변경 불가)"), ReadOnly(true)]
    public string Id { get; set; } = NewId();

    [Category(CatBasic), DisplayName("서버 이름"), Description("탭/로그/디스코드 알림에 표시되는 이름")]
    public string Name { get; set; } = "새 서버";

    [Category(CatBasic), DisplayName("매니저 실행 시 자동 시작")]
    public bool AutoStartOnLaunch { get; set; } = false;

    [Category(CatBasic), DisplayName("디스코드 웹훅 URL (개별)"), Description("비워두면 전역 설정의 웹훅 사용")]
    public string DiscordWebhookUrl { get; set; } = "";

    // ───────────── 2. 게임 전용 ─────────────
    [Category(CatGame), DisplayName("게임 설정"), Description("게임마다 다른 설정 (펼쳐서 편집)")]
    public GameSettings Game { get; set; } = null!;

    [JsonIgnore, Browsable(false)]
    public string GameId => Game.GameId;

    // ───────────── 3. 경로 / 실행 ─────────────
    [Category(CatPath), DisplayName("서버 설치 폴더"), Description("SteamCMD force_install_dir. 서버마다 달라야 함")]
    public string ServerInstallDir { get; set; } = "";

    [Category(CatPath), DisplayName("서버 실행 파일"), Description("exe 또는 bat 전체 경로")]
    public string ServerExePath { get; set; } = "";

    [Category(CatPath), DisplayName("실행 인자")]
    public string ServerArguments { get; set; } = "";

    [Category(CatPath), DisplayName("서버 콘솔 창 숨기기")]
    public bool HideServerConsole { get; set; } = false;

    [Category(CatPath), DisplayName("세이브 폴더"), Description("백업 대상 폴더")]
    public string SaveDir { get; set; } = "";

    [Category(CatPath), DisplayName("백업 저장 폴더")]
    public string BackupDir { get; set; } = "";

    [Category(CatPath), DisplayName("백업 파일 접두어"), Description("{접두어}_yyyyMMdd_HHmmss.zip. 같은 폴더를 여러 서버가 써도 접두어로 구분됨")]
    public string BackupPrefix { get; set; } = "Server_Backup";

    // ───────────── 4. 종료 / 업데이트 ─────────────
    [Category(CatControl), DisplayName("Steam App ID"), Description("0 이면 SteamCMD 업데이트를 하지 않음")]
    public int AppId { get; set; } = 0;

    [Category(CatControl), DisplayName("SteamCMD 경로 (개별)"), Description("비워두면 전역 설정의 SteamCMD 사용")]
    public string SteamCmdPath { get; set; } = "";

    [Category(CatControl), DisplayName("시작 전 자동 업데이트")]
    public bool UpdateBeforeStart { get; set; } = true;

    [Category(CatControl), DisplayName("업데이트 시 validate")]
    public bool ValidateOnUpdate { get; set; } = false;

    [Category(CatControl), DisplayName("종료 카운트다운(초)"), Description("쉼표로 구분. 예: 60,30,10 (공지 기능이 있는 게임만)")]
    public string ShutdownCountdown { get; set; } = "60,30,10";

    [Category(CatControl), DisplayName("공지 메시지 형식"), Description("{0}=남은 초, {1}=사유")]
    public string AnnounceFormat { get; set; } = "[{1}] {0}초 후 서버가 종료됩니다. 안전한 곳에서 대기해 주세요.";

    [Category(CatControl), DisplayName("종료 대기 시간(초)"), Description("종료 요청 후 이 시간 안에 종료되지 않으면 강제 종료(Kill)")]
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    [Category(CatControl), DisplayName("접속자 조회 주기(초)")]
    public int PlayerPollIntervalSeconds { get; set; } = 15;

    // ───────────── 5. 크래시 / 메모리 ─────────────
    [Category(CatCrash), DisplayName("크래시 시 자동 재시작")]
    public bool AutoRestartOnCrash { get; set; } = true;

    [Category(CatCrash), DisplayName("크래시 재시작 지연(초)")]
    public int CrashRestartDelaySeconds { get; set; } = 10;

    [Category(CatCrash), DisplayName("크래시 허용 횟수"), Description("아래 시간 창 안에서 이 횟수를 넘으면 자동 재시작 중단 (무한 루프 방지)")]
    public int MaxCrashRestarts { get; set; } = 5;

    [Category(CatCrash), DisplayName("크래시 집계 시간 창(분)")]
    public int CrashWindowMinutes { get; set; } = 30;

    [Category(CatCrash), DisplayName("메모리 체크 주기(초)")]
    public int MemoryCheckIntervalSeconds { get; set; } = 10;

    [Category(CatCrash), DisplayName("메모리 임계치 재시작 사용")]
    public bool MemoryRestartEnabled { get; set; } = true;

    [Category(CatCrash), DisplayName("메모리 임계치(GB)"), Description("서버 프로세스 WorkingSet 이 이 값을 넘으면 안전 재시작")]
    public double MemoryThresholdGB { get; set; } = 12.0;

    [Category(CatCrash), DisplayName("메모리 재시작 시 백업")]
    public bool BackupOnMemoryRestart { get; set; } = false;

    // ───────────── 6. 정기 재시작 / 백업 ─────────────
    [Category(CatSchedule), DisplayName("정기 재시작 사용")]
    public bool ScheduledRestartEnabled { get; set; } = true;

    [Category(CatSchedule), DisplayName("정기 재시작 시각"), Description("HH:mm (24시간제). 서버마다 시각을 엇갈리게 두면 부하가 분산됨")]
    public string ScheduledRestartTime { get; set; } = "04:00";

    [Category(CatSchedule), DisplayName("정기 재시작 시 백업")]
    public bool BackupOnScheduledRestart { get; set; } = true;

    [Category(CatSchedule), DisplayName("정기 재시작 시 업데이트")]
    public bool UpdateOnScheduledRestart { get; set; } = true;

    [Category(CatSchedule), DisplayName("백업 보관 일수"), Description("이 일수가 지난 백업 zip 자동 삭제. 0 = 삭제 안 함")]
    public int BackupRetentionDays { get; set; } = 7;

    // ───────────── helpers ─────────────

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public List<int> GetCountdownSeconds() =>
        (ShutdownCountdown ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .OrderByDescending(v => v)
            .ToList();

    public bool TryGetScheduledTime(out TimeSpan time) =>
        TimeSpan.TryParseExact(ScheduledRestartTime?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time)
        && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);

    [JsonIgnore, Browsable(false)]
    public long MemoryThresholdBytes => (long)(MemoryThresholdGB * 1024 * 1024 * 1024);

    public List<string> Validate()
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) e.Add("서버 이름이 비어 있습니다.");
        if (Game == null) e.Add("게임 설정이 없습니다.");
        if (string.IsNullOrWhiteSpace(ServerExePath)) e.Add("서버 실행 파일 경로가 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(ServerInstallDir)) e.Add("서버 설치 폴더가 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(SaveDir)) e.Add("세이브 폴더가 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(BackupDir)) e.Add("백업 저장 폴더가 비어 있습니다.");
        if (string.IsNullOrWhiteSpace(BackupPrefix) || BackupPrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            e.Add("백업 파일 접두어가 비어 있거나 파일 이름에 쓸 수 없는 문자가 있습니다.");
        if (AppId < 0) e.Add("Steam App ID 는 0 이상이어야 합니다.");
        if (!TryGetScheduledTime(out _)) e.Add("정기 재시작 시각은 HH:mm 형식이어야 합니다. (예: 04:00)");
        if (MemoryThresholdGB <= 0) e.Add("메모리 임계치는 0보다 커야 합니다.");
        if (ShutdownTimeoutSeconds < 5) e.Add("종료 대기 시간은 5초 이상이어야 합니다.");
        if (MemoryCheckIntervalSeconds < 2) e.Add("메모리 체크 주기는 2초 이상이어야 합니다.");
        if (PlayerPollIntervalSeconds < 3) e.Add("접속자 조회 주기는 3초 이상이어야 합니다.");
        if (BackupRetentionDays < 0) e.Add("백업 보관 일수는 0 이상이어야 합니다.");
        if (!string.IsNullOrWhiteSpace(DiscordWebhookUrl) && !Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out _))
            e.Add("디스코드 웹훅 URL 형식이 올바르지 않습니다.");
        try { _ = string.Format(AnnounceFormat ?? "", 60, "test"); }
        catch (FormatException) { e.Add("공지 메시지 형식이 잘못되었습니다. ({0}, {1}만 사용 가능)"); }
        if (Game != null) e.AddRange(Game.Validate());
        return e;
    }

    public ServerProfile Clone() => ConfigManager.DeepClone(this);

    public override string ToString() => Name;
}
