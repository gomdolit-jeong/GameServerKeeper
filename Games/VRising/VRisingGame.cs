using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Services;

namespace GameServerKeeper.Games.VRising;

/// <summary>브이라이징 전용 설정 (현재는 추가 항목 없음. RCON 공지 연동 시 여기에 추가)</summary>
public sealed class VRisingSettings : GameSettings
{
    public override string GameId => VRisingGame.Id;
}

/// <summary>브이라이징 어댑터: 콘솔 앱이라 Ctrl+C 를 받으면 월드를 저장하고 종료한다.</summary>
public sealed class VRisingAdapter : GameServerAdapterBase
{
    private const string Src = "VRising";

    public VRisingAdapter(IAppLog log) : base(log) { }

    public override GameCapabilities Capabilities => GameCapabilities.GracefulShutdown;

    public override Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct)
    {
        var ok = ConsoleCtrl.SendCtrlC(serverPid);
        if (!ok) Log.Warn(Src, "Ctrl+C 전송 실패 (서버 콘솔에 접근할 수 없습니다)");
        return Task.FromResult(ok);
    }

    public override IEnumerable<string> CheckConfiguration()
    {
        if (!File.Exists(Profile.ServerExePath))
            yield return $"서버 실행 파일이 없습니다: {Profile.ServerExePath}";
    }
}

public static class VRisingGame
{
    public const string Id = "vrising";

    public static readonly GameDefinition Definition = new()
    {
        GameId = Id,
        DisplayName = "V Rising",
        Description = "Ctrl+C로 월드를 저장하고 안전하게 종료해요. 크래시 복구·메모리 재시작·백업·업데이트·정기 재시작을 지원해요.",
        SettingsNote = "서버 이름·포트·비밀번호는 세이브 폴더의 Settings\\ServerHostSettings.json에서 바꿔요(없으면 서버를 한 번 켜면 생겨요). 기본 포트는 9876·9877(UDP)이에요.",
        SettingsType = typeof(VRisingSettings),
        CreateAdapter = log => new VRisingAdapter(log),
        ExeFileNames = ["VRisingServer.exe"],
        DefaultAppId = 1829350,
        CreateProfile = CreateProfile,
    };

    private static ServerProfile CreateProfile(string? exePath)
    {
        var install = string.IsNullOrWhiteSpace(exePath)
            ? @"D:\Steam_Dedicated_Server\VRising"
            : Path.GetDirectoryName(Path.GetFullPath(exePath))!.TrimEnd('\\');
        var parent = Path.GetDirectoryName(install) ?? install;
        var save = Path.Combine(install, "save-data");

        return new ServerProfile
        {
            Name = "V Rising",
            Game = new VRisingSettings(),
            AppId = 1829350,
            ServerInstallDir = install,
            ServerExePath = Path.Combine(install, "VRisingServer.exe"),
            ServerArguments = $"-persistentDataPath \"{save}\" -serverName \"My V Rising Server\" -saveName \"world1\" " +
                              $"-logFile \"{Path.Combine(install, "logs", "VRisingServer.log")}\"",
            SaveDir = save,
            BackupDir = Path.Combine(parent, "Backups"),
            BackupPrefix = "VRising_Backup",
            MemoryThresholdGB = 8.0,
            ShutdownCountdown = "",
        };
    }
}
