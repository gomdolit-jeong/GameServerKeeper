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

    [DisplayName("REST API 호스트")]
    public string RestApiHost { get; set; } = "127.0.0.1";

    [DisplayName("REST API 포트"), Description("PalWorldSettings.ini 의 RESTAPIPort 와 같아야 함. 서버마다 달라야 함")]
    public int RestApiPort { get; set; } = 8212;

    [DisplayName("AdminPassword"), Description("PalWorldSettings.ini 의 AdminPassword (Basic Auth: admin:{AdminPassword})"),
     PasswordPropertyText(true)]
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
        SettingsType = typeof(PalworldSettings),
        CreateAdapter = log => new PalworldAdapter(log),
        CreateDefaultProfile = () => new ServerProfile
        {
            Name = "Palworld",
            Game = new PalworldSettings(),
            AppId = 2394010,
            ServerInstallDir = @"D:\Steam_Dedicated_Server\Palworld",
            ServerExePath = @"D:\Steam_Dedicated_Server\Palworld\PalServer.exe",
            ServerArguments = "-port=8211 -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS",
            SaveDir = @"D:\Steam_Dedicated_Server\Palworld\Pal\Saved",
            BackupDir = @"D:\Steam_Dedicated_Server\Backups",
            BackupPrefix = "Palworld_Backup",
            MemoryThresholdGB = 12.0,
        }
    };
}
