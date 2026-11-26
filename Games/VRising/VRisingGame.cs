using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Services;

namespace GameServerKeeper.Games.VRising;

/// <summary>브이라이징 전용 설정: RCON (인게임 공지용)</summary>
public sealed class VRisingSettings : GameSettings
{
    public override string GameId => VRisingGame.Id;

    [DisplayName("RCON 사용"),
     Description("RCON으로 접속자에게 종료 카운트다운 공지를 보내요. 켜두면 서버를 시작할 때 RCON 설정을 실행 인자로 자동으로 넘겨서 " +
                 "ServerHostSettings.json을 따로 고칠 필요가 없어요.")]
    public bool RconEnabled { get; set; } = true;

    [DisplayName("RCON 주소"),
     Description("RCON을 열 주소예요. 127.0.0.1이면 이 PC에서만 접속할 수 있어서 안전해요. 다른 PC에서 관리할 게 아니면 그대로 두세요.")]
    public string RconHost { get; set; } = "127.0.0.1";

    [DisplayName("RCON 포트"), Range(1, 65535),
     Description("RCON TCP 포트예요. 서버를 여러 대 운영하면 서버마다 달라야 해요.")]
    public int RconPort { get; set; } = 25575;

    [DisplayName("RCON 비밀번호"), PasswordPropertyText(true),
     Description("RCON 접속 비밀번호예요. 비워둘 수 없어요. 처음에 무작위로 만들어져 있으니 바꾸지 않아도 돼요.")]
    public string RconPassword { get; set; } = NewPassword();

    public static string NewPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return string.Concat(Enumerable.Range(0, 20).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]));
    }

    public override IEnumerable<string> Validate()
    {
        if (RconEnabled && string.IsNullOrWhiteSpace(RconPassword)) yield return "RCON을 쓰려면 RCON 비밀번호가 있어야 해요.";
        if (RconEnabled && string.IsNullOrWhiteSpace(RconHost)) yield return "RCON 주소가 비어 있어요.";
    }
}

/// <summary>
/// 브이라이징 어댑터.
/// - 종료: 콘솔 앱이라 Ctrl+C 를 받으면 월드를 저장하고 종료한다. (RCON 에는 저장 명령이 없음)
/// - 공지: RCON "announce" 명령 (RCON 사용 시)
/// V Rising RCON 은 접속자 목록·저장 명령을 지원하지 않는다 (공식 문서 기준).
/// </summary>
public sealed class VRisingAdapter : GameServerAdapterBase
{
    private const string Src = "VRising";

    public VRisingAdapter(IAppLog log) : base(log) { }

    private VRisingSettings Settings => (VRisingSettings)Profile.Game;

    private bool RconReady => Settings.RconEnabled && !string.IsNullOrWhiteSpace(Settings.RconPassword);

    public override GameCapabilities Capabilities =>
        GameCapabilities.GracefulShutdown | (RconReady ? GameCapabilities.Announce : GameCapabilities.None);

    public override string BuildArguments(string baseArguments)
    {
        if (!RconReady) return baseArguments;
        var bind = Settings.RconHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : Settings.RconHost.Trim();
        return $"{baseArguments} -rconEnabled true -rconPort {Settings.RconPort} -rconPassword \"{Settings.RconPassword}\" -rconBindAddress \"{bind}\"".Trim();
    }

    /// <summary>V Rising RCON 은 본문을 UTF-8 이 아닌 Latin-1 로 읽어서 한글이 깨진다 (실서버 로그로 확인)</summary>
    public override bool AnnounceSupportsUnicode => false;

    public override async Task<bool> AnnounceAsync(string message, CancellationToken ct)
    {
        if (!RconReady) return false;
        if (message.Any(c => c > 127))
            Log.Warn(Src, "V Rising RCON은 한글 등 영어 외 문자를 제대로 표시하지 못해요. 공지는 영어로 적어 주세요.");
        var r = await SourceRcon.ExecuteAsync(Settings.RconHost, Settings.RconPort, Settings.RconPassword,
            $"announce {message}", TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        if (!r.Success) Log.Warn(Src, $"공지 실패: {r.Error}");
        return r.Success;
    }

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
        if (Settings.RconEnabled && string.IsNullOrWhiteSpace(Settings.RconPassword))
            yield return "RCON 비밀번호가 비어 있어서 인게임 공지를 보낼 수 없어요.";
    }
}

public static class VRisingGame
{
    public const string Id = "vrising";

    public static readonly GameDefinition Definition = new()
    {
        GameId = Id,
        DisplayName = "V Rising",
        Description = "RCON으로 종료 카운트다운을 공지하고, Ctrl+C로 월드를 저장한 뒤 안전하게 종료해요. 크래시 복구·메모리 재시작·백업·업데이트·정기 재시작을 지원해요.",
        SettingsNote = "공지는 영어로 적어 주세요(V Rising RCON은 한글이 깨져요). 서버 이름·게임 포트는 세이브 폴더의 Settings\\ServerHostSettings.json에서 바꿔요.",
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
            ShutdownCountdown = "60,30,10",
            // V Rising RCON 은 한글이 깨지므로 영어 문구 ({1} 사유는 한글이라 넣지 않음)
            AnnounceFormat = "Server will go down in {0} seconds. Please find a safe spot.",
        };
    }
}
