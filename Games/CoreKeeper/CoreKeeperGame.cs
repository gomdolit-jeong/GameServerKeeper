using System.Diagnostics;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Games.CoreKeeper;

/// <summary>코어키퍼 전용 설정 (현재는 추가 항목 없음)</summary>
public sealed class CoreKeeperSettings : GameSettings
{
    public override string GameId => CoreKeeperGame.Id;
}

/// <summary>
/// 코어키퍼 어댑터. CoreKeeperServer.exe 는 콘솔이 없는 GUI 앱이라 Ctrl+C 가 통하지 않는다.
/// 공식 Launch.ps1 과 같이 taskkill(/F 없이)로 종료 요청을 보내면 저장 후 종료한다.
/// </summary>
public sealed class CoreKeeperAdapter : GameServerAdapterBase
{
    private const string Src = "CoreKeeper";

    public CoreKeeperAdapter(IAppLog log) : base(log) { }

    public override GameCapabilities Capabilities => GameCapabilities.GracefulShutdown;

    public override async Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("taskkill", $"/pid {serverPid}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0) Log.Warn(Src, $"종료 요청 실패 (taskkill 종료 코드 {p.ExitCode})");
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(Src, $"종료 요청 실패: {ex.Message}");
            return false;
        }
    }

    public override IEnumerable<string> CheckConfiguration()
    {
        if (!File.Exists(Profile.ServerExePath))
            yield return $"서버 실행 파일이 없습니다: {Profile.ServerExePath}";
    }
}

public static class CoreKeeperGame
{
    public const string Id = "corekeeper";

    public static readonly GameDefinition Definition = new()
    {
        GameId = Id,
        DisplayName = "Core Keeper",
        Description = "종료 요청을 보내 월드를 저장하고 안전하게 종료해요. 크래시 복구·메모리 재시작·백업·업데이트·정기 재시작을 지원해요.",
        SettingsNote = "접속용 Game ID는 서버를 켠 뒤 설치 폴더의 GameInfo.txt에서 확인할 수 있어요. " +
                       "IP로 접속받으려면 실행 인자에 -port 27015 처럼 포트를 추가하고 포트를 열어 주세요.",
        SettingsType = typeof(CoreKeeperSettings),
        CreateAdapter = log => new CoreKeeperAdapter(log),
        ExeFileNames = ["CoreKeeperServer.exe"],
        DefaultAppId = 1963720,
        CreateProfile = CreateProfile,
    };

    private static ServerProfile CreateProfile(string? exePath)
    {
        var install = string.IsNullOrWhiteSpace(exePath)
            ? @"D:\Steam_Dedicated_Server\CoreKeeper"
            : Path.GetDirectoryName(Path.GetFullPath(exePath))!.TrimEnd('\\');
        var parent = Path.GetDirectoryName(install) ?? install;
        var save = Path.Combine(install, "save-data");

        return new ServerProfile
        {
            Name = "Core Keeper",
            Game = new CoreKeeperSettings(),
            AppId = 1963720,
            ServerInstallDir = install,
            ServerExePath = Path.Combine(install, "CoreKeeperServer.exe"),
            ServerArguments = $"-batchmode -logfile \"{Path.Combine(install, "CoreKeeperServerLog.txt")}\" " +
                              $"-datapath \"{save}\" -worldname \"Core Keeper Server\" -maxplayers 8",
            SaveDir = save,
            BackupDir = Path.Combine(parent, "Backups"),
            BackupPrefix = "CoreKeeper_Backup",
            MemoryThresholdGB = 6.0,
            ShutdownCountdown = "",
        };
    }
}
