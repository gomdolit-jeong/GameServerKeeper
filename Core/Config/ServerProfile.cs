using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using GameServerKeeper.Core.Games;

namespace GameServerKeeper.Core.Config;

/// <summary>
/// 서버 1개의 설정. 게임 공통 항목 + 게임 전용 설정(<see cref="Game"/>).
/// Category / DisplayName / Description / Range 특성이 설정 화면에 그대로 쓰인다.
/// (선언 순서 = 화면 표시 순서)
/// </summary>
public sealed class ServerProfile
{
    private const string CatBasic = "기본";
    private const string CatGame = "게임 전용";
    private const string CatPath = "경로 / 실행";
    private const string CatControl = "종료 / 업데이트";
    private const string CatCrash = "크래시 / 메모리";
    private const string CatSchedule = "정기 재시작 / 백업";

    // ───────────── 기본 ─────────────
    [Browsable(false)]
    public string Id { get; set; } = NewId();

    [Category(CatBasic), DisplayName("서버 이름"),
     Description("사이드바, 로그, 디스코드 알림에 표시되는 이름이에요. 다른 서버와 겹치지 않게 지어주세요.")]
    public string Name { get; set; } = "새 서버";

    [Category(CatBasic), DisplayName("프로그램 실행 시 자동 시작"),
     Description("GameServerKeeper를 켜면 이 서버를 자동으로 시작해요. PC를 재부팅한 뒤 서버를 자동으로 살리고 싶을 때 켜두세요.")]
    public bool AutoStartOnLaunch { get; set; } = false;

    [Category(CatBasic), DisplayName("디스코드 웹훅 (이 서버만)"),
     Description("이 서버의 알림만 다른 채널로 보내고 싶을 때 입력하세요. 비워두면 전역 설정의 웹훅을 사용해요.")]
    public string DiscordWebhookUrl { get; set; } = "";

    // ───────────── 게임 전용 ─────────────
    [Category(CatGame), DisplayName("게임 설정")]
    public GameSettings Game { get; set; } = null!;

    [JsonIgnore, Browsable(false)]
    public string GameId => Game.GameId;

    // ───────────── 경로 / 실행 ─────────────
    [Category(CatPath), DisplayName("서버 설치 폴더"), PathEditor(PathKind.Folder),
     Description("서버 파일이 설치된 폴더예요. SteamCMD가 여기에 설치·업데이트하고, 프로그램을 다시 켰을 때 실행 중인 서버를 찾는 기준이 돼요. 서버마다 달라야 해요.")]
    public string ServerInstallDir { get; set; } = "";

    [Category(CatPath), DisplayName("서버 실행 파일"), PathEditor(PathKind.File, "실행 파일|*.exe;*.bat;*.cmd|모든 파일|*.*"),
     Description("서버를 실행하는 파일(exe 또는 bat)이에요.")]
    public string ServerExePath { get; set; } = "";

    [Category(CatPath), DisplayName("실행 인자"),
     Description("서버 실행 파일에 넘기는 옵션이에요. 예: -port=8211. 서버를 여러 대 운영하면 포트를 서로 다르게 지정하세요.")]
    public string ServerArguments { get; set; } = "";

    [Category(CatPath), DisplayName("서버 콘솔 창 숨기기"),
     Description("서버의 검은 콘솔 창을 띄우지 않아요. 콘솔에 나오던 내용은 서버 자체 로그 파일에서 확인해야 해요.")]
    public bool HideServerConsole { get; set; } = false;

    [Category(CatPath), DisplayName("세이브 폴더"), PathEditor(PathKind.Folder),
     Description("백업할 세이브 데이터 폴더예요. 이 폴더 전체가 zip으로 압축돼요.")]
    public string SaveDir { get; set; } = "";

    [Category(CatPath), DisplayName("백업 저장 폴더"), PathEditor(PathKind.Folder),
     Description("백업 zip 파일을 저장할 폴더예요. 여러 서버가 같은 폴더를 써도 괜찮아요.")]
    public string BackupDir { get; set; } = "";

    [Category(CatPath), DisplayName("백업 파일 이름 앞부분"),
     Description("예: Palworld_Backup → Palworld_Backup_20261126_040000.zip. 같은 백업 폴더를 쓰는 서버끼리는 달라야 해요. 오래된 백업을 정리할 때 이 이름으로 구분해요.")]
    public string BackupPrefix { get; set; } = "Server_Backup";

    // ───────────── 종료 / 업데이트 ─────────────
    [Category(CatControl), DisplayName("Steam App ID"), Range(0, int.MaxValue),
     Description("SteamCMD로 업데이트할 때 쓰는 데디 서버 번호예요. 팰월드는 2394010이에요. 스팀 게임이 아니면 0으로 두세요(업데이트를 건너뛰어요).")]
    public int AppId { get; set; } = 0;

    [Category(CatControl), DisplayName("SteamCMD 경로 (이 서버만)"), PathEditor(PathKind.File, "steamcmd.exe|steamcmd.exe|모든 파일|*.*"),
     Description("이 서버만 다른 steamcmd.exe를 쓸 때 지정하세요. 비워두면 전역 설정의 경로를 사용해요.")]
    public string SteamCmdPath { get; set; } = "";

    [Category(CatControl), DisplayName("시작 전 자동 업데이트"),
     Description("서버를 시작할 때마다 SteamCMD로 최신 버전을 먼저 받아요. 게임이 업데이트된 뒤 접속이 안 되는 문제를 막아줘요.")]
    public bool UpdateBeforeStart { get; set; } = true;

    [Category(CatControl), DisplayName("업데이트 시 파일 검사"),
     Description("업데이트할 때 모든 파일을 검사해서 손상된 파일을 복구해요(SteamCMD validate). 시간이 오래 걸려서 평소에는 꺼두는 걸 권장해요.")]
    public bool ValidateOnUpdate { get; set; } = false;

    [Category(CatControl), DisplayName("종료 카운트다운 (초)"), RequiresCapability(GameCapabilities.Announce),
     Description("종료 전에 인게임 공지를 보낼 시점이에요. 쉼표로 구분해요. 예: 60,30,10 → 60초·30초·10초 전에 공지해요. 접속자가 없으면 기다리지 않고 바로 종료해요.")]
    public string ShutdownCountdown { get; set; } = "60,30,10";

    [Category(CatControl), DisplayName("카운트다운 공지 문구"), RequiresCapability(GameCapabilities.Announce),
     Description("{0}은 남은 초, {1}은 종료 사유(정기 재시작, 메모리 정리 등)로 바뀌어요.")]
    public string AnnounceFormat { get; set; } = "[{1}] {0}초 후 서버가 종료됩니다. 안전한 곳에서 대기해 주세요.";

    [Category(CatControl), DisplayName("종료 대기 시간 (초)"), Range(5, 3600),
     Description("저장·종료 요청을 보낸 뒤 서버가 스스로 꺼지길 기다리는 시간이에요. 이 시간이 지나도 안 꺼지면 강제 종료해요.")]
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    [Category(CatControl), DisplayName("접속자 조회 주기 (초)"), Range(3, 3600), RequiresCapability(GameCapabilities.PlayerList),
     Description("접속자 목록을 서버에 물어보는 간격이에요. 너무 짧으면 서버에 부담이 될 수 있어요.")]
    public int PlayerPollIntervalSeconds { get; set; } = 15;

    // ───────────── 크래시 / 메모리 ─────────────
    [Category(CatCrash), DisplayName("크래시 시 자동 재시작"),
     Description("프로그램이 끄지 않았는데 서버가 꺼지면(크래시) 자동으로 다시 켜요.")]
    public bool AutoRestartOnCrash { get; set; } = true;

    [Category(CatCrash), DisplayName("재시작 전 대기 (초)"), Range(1, 3600),
     Description("크래시가 난 뒤 다시 켜기 전까지 기다리는 시간이에요.")]
    public int CrashRestartDelaySeconds { get; set; } = 10;

    [Category(CatCrash), DisplayName("크래시 허용 횟수"), Range(1, 100),
     Description("아래 시간 동안 크래시가 이 횟수보다 많으면 자동 재시작을 멈춰요. 설정 오류 때문에 끝없이 재시작하는 걸 막아줘요.")]
    public int MaxCrashRestarts { get; set; } = 5;

    [Category(CatCrash), DisplayName("크래시 집계 시간 (분)"), Range(1, 1440),
     Description("크래시 횟수를 세는 시간 범위예요. 예: 30분 동안 5회를 넘으면 자동 재시작을 멈춰요.")]
    public int CrashWindowMinutes { get; set; } = 30;

    [Category(CatCrash), DisplayName("메모리 확인 주기 (초)"), Range(2, 3600),
     Description("서버가 쓰는 메모리 양을 확인하는 간격이에요.")]
    public int MemoryCheckIntervalSeconds { get; set; } = 10;

    [Category(CatCrash), DisplayName("메모리 초과 시 재시작"),
     Description("메모리 사용량이 아래 기준을 넘으면 안전하게 재시작해요. 오래 켜두면 메모리가 계속 늘어나 렉이 걸리는 게임에 유용해요.")]
    public bool MemoryRestartEnabled { get; set; } = true;

    [Category(CatCrash), DisplayName("메모리 기준 (GB)"), Range(0.5, 1024.0),
     Description("이 용량을 넘으면 재시작해요. PC 전체 메모리보다 넉넉히 낮게 잡으세요. 접속자가 있으면 카운트다운 공지 후, 없으면 바로 재시작해요.")]
    public double MemoryThresholdGB { get; set; } = 12.0;

    [Category(CatCrash), DisplayName("메모리 재시작 때도 백업"),
     Description("메모리 초과로 재시작할 때도 세이브를 백업해요.")]
    public bool BackupOnMemoryRestart { get; set; } = false;

    // ───────────── 정기 재시작 / 백업 ─────────────
    [Category(CatSchedule), DisplayName("매일 정기 재시작"),
     Description("매일 정해진 시각에 안전 종료 → 백업 → 업데이트 → 재시작을 자동으로 해요.")]
    public bool ScheduledRestartEnabled { get; set; } = true;

    [Category(CatSchedule), DisplayName("재시작 시각"),
     Description("24시간제로 입력하세요. 예: 04:00. 서버가 여러 대면 시각을 엇갈리게 두면 PC 부하가 분산돼요.")]
    public string ScheduledRestartTime { get; set; } = "04:00";

    [Category(CatSchedule), DisplayName("정기 재시작 때 백업"),
     Description("서버가 완전히 꺼진 상태에서 세이브를 백업해요. 가장 안전한 백업 시점이에요.")]
    public bool BackupOnScheduledRestart { get; set; } = true;

    [Category(CatSchedule), DisplayName("정기 재시작 때 업데이트"),
     Description("정기 재시작 때 SteamCMD로 게임 서버 업데이트도 해요.")]
    public bool UpdateOnScheduledRestart { get; set; } = true;

    [Category(CatSchedule), DisplayName("백업 보관 기간 (일)"), Range(0, 3650),
     Description("이 일수보다 오래된 백업 파일을 자동으로 지워요. 0이면 지우지 않아요.")]
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
        if (string.IsNullOrWhiteSpace(Name)) e.Add("서버 이름이 비어 있어요.");
        if (Game == null) e.Add("게임 설정이 없어요.");
        if (string.IsNullOrWhiteSpace(ServerExePath)) e.Add("서버 실행 파일을 지정해 주세요.");
        if (string.IsNullOrWhiteSpace(ServerInstallDir)) e.Add("서버 설치 폴더를 지정해 주세요.");
        if (string.IsNullOrWhiteSpace(SaveDir)) e.Add("세이브 폴더를 지정해 주세요.");
        if (string.IsNullOrWhiteSpace(BackupDir)) e.Add("백업 저장 폴더를 지정해 주세요.");
        if (string.IsNullOrWhiteSpace(BackupPrefix) || BackupPrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            e.Add("백업 파일 이름 앞부분이 비어 있거나 파일 이름에 쓸 수 없는 문자가 있어요.");
        if (!TryGetScheduledTime(out _)) e.Add("재시작 시각은 HH:mm 형식이어야 해요. (예: 04:00)");
        if (!string.IsNullOrWhiteSpace(DiscordWebhookUrl) && !Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out _))
            e.Add("디스코드 웹훅 주소 형식이 올바르지 않아요.");
        try { _ = string.Format(AnnounceFormat ?? "", 60, "test"); }
        catch (FormatException) { e.Add("카운트다운 공지 문구에는 {0}, {1}만 쓸 수 있어요."); }
        if (Game != null) e.AddRange(Game.Validate());
        return e;
    }

    public ServerProfile Clone() => ConfigManager.DeepClone(this);

    public override string ToString() => Name;
}
