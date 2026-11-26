using System.ComponentModel;

namespace GameServerKeeper.Core.Config;

/// <summary>config.json 루트: 전역 설정 + 서버 프로필 목록</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 2;

    private const string CatCommon = "공통";
    private const string CatUi = "화면 / 트레이";

    [Browsable(false)]
    public int Version { get; set; } = CurrentVersion;

    [Category(CatCommon), DisplayName("SteamCMD 경로"), PathEditor(PathKind.File, "steamcmd.exe|steamcmd.exe|모든 파일|*.*"),
     Description("서버 설치·업데이트에 쓰는 steamcmd.exe 위치예요. 서버 설정에서 따로 지정하지 않으면 모든 서버가 이 경로를 사용해요.")]
    public string SteamCmdPath { get; set; } = @"D:\Steam_Dedicated_Server\SteamCmd\steamcmd.exe";

    [Category(CatCommon), DisplayName("디스코드 웹훅"),
     Description("서버 시작·종료·크래시·백업 알림을 보낼 디스코드 웹훅 주소예요. 디스코드 채널 설정 → 연동 → 웹후크에서 만들 수 있어요. 비워두면 알림을 보내지 않아요.")]
    public string DiscordWebhookUrl { get; set; } = "";

    [Category(CatUi), DisplayName("최소화하면 트레이로"),
     Description("창을 최소화하면 작업 표시줄 대신 시스템 트레이(시계 옆)로 숨겨요.")]
    public bool MinimizeToTray { get; set; } = true;

    [Category(CatUi), DisplayName("X 버튼을 누르면 트레이로"),
     Description("X 버튼을 눌러도 프로그램이 꺼지지 않고 트레이로 숨겨져요. 서버 감시는 계속돼요. 끄면 X 버튼이 프로그램 종료로 동작해요.")]
    public bool CloseToTray { get; set; } = true;

    [Category(CatUi), DisplayName("트레이 상태로 시작"),
     Description("프로그램을 실행하면 창을 띄우지 않고 바로 트레이로 들어가요. 윈도우 시작 프로그램에 등록해 둘 때 유용해요.")]
    public bool StartMinimized { get; set; } = false;

    [Browsable(false)]
    public List<ServerProfile> Servers { get; set; } = new();

    /// <summary>전역 항목만 검증</summary>
    public List<string> ValidateGlobal()
    {
        var e = new List<string>();
        if (!string.IsNullOrWhiteSpace(DiscordWebhookUrl) && !Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out _))
            e.Add("디스코드 웹훅 주소 형식이 올바르지 않아요.");
        return e;
    }

    /// <summary>
    /// 서버 간 충돌 검사. 같은 설치 폴더/세이브 폴더를 두 서버가 쓰면
    /// 프로세스 식별·업데이트·백업이 꼬이므로 막는다.
    /// </summary>
    public static List<string> ValidateServerSet(IReadOnlyList<ServerProfile> servers)
    {
        var e = new List<string>();
        static string Norm(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\').ToUpperInvariant(); }
            catch { return p.ToUpperInvariant(); }
        }

        foreach (var g in servers.GroupBy(s => s.Id).Where(g => g.Count() > 1))
            e.Add($"Id 중복: {g.Key}");
        foreach (var g in servers.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            e.Add($"서버 이름이 겹쳐요: {g.Key}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.ServerInstallDir))
                     .GroupBy(s => Norm(s.ServerInstallDir)).Where(g => g.Count() > 1))
            e.Add($"설치 폴더를 여러 서버가 같이 쓰고 있어요: {string.Join(", ", g.Select(s => s.Name))}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.SaveDir))
                     .GroupBy(s => Norm(s.SaveDir)).Where(g => g.Count() > 1))
            e.Add($"세이브 폴더를 여러 서버가 같이 쓰고 있어요: {string.Join(", ", g.Select(s => s.Name))}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.BackupDir))
                     .GroupBy(s => Norm(s.BackupDir) + "|" + s.BackupPrefix.ToUpperInvariant()).Where(g => g.Count() > 1))
            e.Add($"같은 백업 폴더에서 백업 파일 이름 앞부분이 겹쳐요 (오래된 백업 정리 때 서로 지워져요): {string.Join(", ", g.Select(s => s.Name))}");
        return e;
    }

    public AppConfig Clone() => ConfigManager.DeepClone(this);
}
