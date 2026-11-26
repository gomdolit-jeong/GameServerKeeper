using System.ComponentModel;
using System.Text.RegularExpressions;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;

namespace GameServerKeeper.Games.Palworld;

/// <summary>팰월드 전용 설정 (ServerProfile.Game)</summary>
public sealed class PalworldSettings : GameSettings
{
    public override string GameId => PalworldGame.Id;

    [DisplayName("REST API 주소"),
     Description("팰월드 서버의 REST API 주소예요. 서버가 같은 PC에서 돌아가면 127.0.0.1 그대로 두세요.")]
    public string RestApiHost { get; set; } = "127.0.0.1";

    [DisplayName("REST API 포트"), System.ComponentModel.DataAnnotations.Range(1, 65535),
     Description("PalWorldSettings.ini의 RESTAPIPort와 같아야 해요. 서버를 여러 대 운영하면 서버마다 달라야 해요.")]
    public int RestApiPort { get; set; } = 8212;

    [DisplayName("관리자 비밀번호 (AdminPassword)"), PasswordPropertyText(true),
     Description("PalWorldSettings.ini의 AdminPassword와 같아야 해요. 공지·저장·종료 같은 관리자 명령을 보낼 때 인증에 쓰여요.")]
    public string AdminPassword { get; set; } = "";

    public override IEnumerable<string> Validate()
    {
        if (RestApiPort is < 1 or > 65535) yield return "REST API 포트는 1~65535 사이여야 합니다.";
        if (string.IsNullOrWhiteSpace(RestApiHost)) yield return "REST API 호스트가 비어 있습니다.";
    }
}

/// <summary>팰월드 어댑터: REST API 로 접속자/공지/저장/종료를 처리한다.</summary>
public sealed class PalworldAdapter : GameServerAdapterBase
{
    private const string Src = "Palworld";
    private readonly PalworldRestClient _api;

    public PalworldAdapter(IAppLog log) : base(log) => _api = new PalworldRestClient(log);

    public override GameCapabilities Capabilities =>
        GameCapabilities.PlayerList | GameCapabilities.Announce | GameCapabilities.Save | GameCapabilities.GracefulShutdown;

    /// <summary>PalServer.exe 는 런처이고 실제 서버는 이 자식 프로세스</summary>
    public override string TrackedProcessName => "PalServer-Win64-Shipping-Cmd";

    private PalworldSettings Settings => (PalworldSettings)Profile.Game;

    public override void Configure(ServerProfile profile)
    {
        base.Configure(profile);
        _api.Configure(Settings.RestApiHost, Settings.RestApiPort, Settings.AdminPassword);
    }

    public override async Task<IReadOnlyList<PlayerInfo>?> GetPlayersAsync(CancellationToken ct)
    {
        var list = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        return list?.Select(p => new PlayerInfo(
            p.UserId ?? p.PlayerId ?? p.Name ?? "?",
            p.Name ?? "?",
            p.Level,
            p.Ping)).ToList();
    }

    public override Task<bool> AnnounceAsync(string message, CancellationToken ct) => _api.AnnounceAsync(message, ct);

    public override Task<bool> SaveAsync(CancellationToken ct) => _api.SaveAsync(ct);

    public override Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct) =>
        _api.ShutdownAsync(1, "Server shutting down...", ct);

    /// <summary>PalWorldSettings.ini 의 REST API 설정이 매니저 설정과 맞는지 점검 (비밀번호는 로그에 남기지 않음)</summary>
    public override IEnumerable<string> CheckConfiguration()
    {
        var ini = Path.Combine(Profile.SaveDir, "Config", "WindowsServer", "PalWorldSettings.ini");
        string text;
        try
        {
            if (!File.Exists(ini))
                return [$"PalWorldSettings.ini 가 없습니다 ({ini}). 서버 최초 실행 전이거나 DefaultPalWorldSettings.ini 를 복사해야 합니다."];
            text = File.ReadAllText(ini);
        }
        catch (Exception ex)
        {
            return [$"PalWorldSettings.ini 점검 실패: {ex.Message}"];
        }

        var warnings = new List<string>();
        if (!Regex.IsMatch(text, @"RESTAPIEnabled\s*=\s*True", RegexOptions.IgnoreCase))
            warnings.Add("PalWorldSettings.ini 에 RESTAPIEnabled=True 가 없습니다. 안전 종료/접속자 조회가 동작하지 않습니다.");

        var port = Regex.Match(text, @"RESTAPIPort\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (port.Success && port.Groups[1].Value != Settings.RestApiPort.ToString())
            warnings.Add($"REST API 포트 불일치: ini={port.Groups[1].Value}, 매니저={Settings.RestApiPort}");

        var pw = Regex.Match(text, @"AdminPassword\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        if (pw.Success && pw.Groups[1].Value != Settings.AdminPassword)
            warnings.Add("AdminPassword 가 PalWorldSettings.ini 와 다릅니다. REST API 인증이 실패합니다.");
        return warnings;
    }

    public override void Dispose() => _api.Dispose();
}

public static class PalworldGame
{
    public const string Id = "palworld";

    public static readonly GameDefinition Definition = new()
    {
        GameId = Id,
        DisplayName = "Palworld",
        Description = "REST API로 접속자 조회·인게임 공지·월드 저장·정상 종료를 모두 지원해요.",
        SettingsNote = "PalWorldSettings.ini에서 RESTAPIEnabled=True로 켜고, 포트·관리자 비밀번호를 아래와 똑같이 맞춰 주세요.",
        SettingsType = typeof(PalworldSettings),
        CreateAdapter = log => new PalworldAdapter(log),
        // 런처(PalServer.exe)뿐 아니라 실제 서버 exe 를 골라도 팰월드로 인식
        ExeFileNames = ["PalServer.exe", "PalServer-Win64-Shipping-Cmd.exe", "PalServer-Win64-Shipping.exe"],
        DefaultAppId = 2394010,
        CreateProfile = CreateProfile,
    };

    private static ServerProfile CreateProfile(string? exePath)
    {
        var install = @"D:\Steam_Dedicated_Server\Palworld";
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exePath))!;
            // Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe 를 고른 경우 → 설치 폴더(3단계 위)
            if (Path.GetFileName(exePath).StartsWith("PalServer-Win64", StringComparison.OrdinalIgnoreCase))
                dir = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
            install = dir.TrimEnd('\\');
        }
        var parent = Path.GetDirectoryName(install) ?? install;

        return new ServerProfile
        {
            Name = "Palworld",
            Game = new PalworldSettings(),
            AppId = 2394010,
            ServerInstallDir = install,
            ServerExePath = Path.Combine(install, "PalServer.exe"),
            ServerArguments = "-port=8211 -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS",
            SaveDir = Path.Combine(install, "Pal", "Saved"),
            BackupDir = Path.Combine(parent, "Backups"),
            BackupPrefix = "Palworld_Backup",
            MemoryThresholdGB = 12.0,
        };
    }
}
