using System.ComponentModel;

namespace GameServerKeeper.Core.Config;

/// <summary>config.json 루트: 전역 설정 + 서버 프로필 목록</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 2;

    private const string CatCommon = "1. 공통";
    private const string CatUi = "2. UI";

    [Browsable(false)]
    public int Version { get; set; } = CurrentVersion;

    [Category(CatCommon), DisplayName("SteamCMD 경로"), Description("모든 서버의 기본 steamcmd.exe (서버별로 덮어쓸 수 있음)")]
    public string SteamCmdPath { get; set; } = @"D:\Steam_Dedicated_Server\SteamCmd\steamcmd.exe";

    [Category(CatCommon), DisplayName("디스코드 웹훅 URL"), Description("서버별 웹훅이 비어 있으면 이 URL 사용. 비워두면 알림 비활성화")]
    public string DiscordWebhookUrl { get; set; } = "";

    [Category(CatUi), DisplayName("최소화 시 트레이로")]
    public bool MinimizeToTray { get; set; } = true;

    [Category(CatUi), DisplayName("닫기(X) 시 트레이로"), Description("끄면 X 버튼이 매니저 종료로 동작")]
    public bool CloseToTray { get; set; } = true;

    [Category(CatUi), DisplayName("트레이 상태로 시작")]
    public bool StartMinimized { get; set; } = false;

    [Browsable(false)]
    public List<ServerProfile> Servers { get; set; } = new();

    /// <summary>전역 항목만 검증</summary>
    public List<string> ValidateGlobal()
    {
        var e = new List<string>();
        if (!string.IsNullOrWhiteSpace(DiscordWebhookUrl) && !Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out _))
            e.Add("디스코드 웹훅 URL 형식이 올바르지 않습니다.");
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
            e.Add($"서버 이름 중복: {g.Key}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.ServerInstallDir))
                     .GroupBy(s => Norm(s.ServerInstallDir)).Where(g => g.Count() > 1))
            e.Add($"설치 폴더를 여러 서버가 공유합니다: {string.Join(", ", g.Select(s => s.Name))}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.SaveDir))
                     .GroupBy(s => Norm(s.SaveDir)).Where(g => g.Count() > 1))
            e.Add($"세이브 폴더를 여러 서버가 공유합니다: {string.Join(", ", g.Select(s => s.Name))}");
        foreach (var g in servers.Where(s => !string.IsNullOrWhiteSpace(s.BackupDir))
                     .GroupBy(s => Norm(s.BackupDir) + "|" + s.BackupPrefix.ToUpperInvariant()).Where(g => g.Count() > 1))
            e.Add($"같은 백업 폴더에서 백업 접두어가 겹칩니다 (보관 정리 시 서로 삭제됨): {string.Join(", ", g.Select(s => s.Name))}");
        return e;
    }

    public AppConfig Clone() => ConfigManager.DeepClone(this);
}
