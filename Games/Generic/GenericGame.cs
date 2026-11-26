using System.ComponentModel;
using System.Diagnostics;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Services;

namespace GameServerKeeper.Games.Generic;

public enum GenericStopMethod
{
    [Description("콘솔에 Ctrl+C 전송 (대부분의 데디 서버가 저장 후 종료)")]
    CtrlC,
    [Description("메인 창 닫기 요청 (WM_CLOSE)")]
    CloseWindow,
    [Description("정상 종료 없이 바로 강제 종료")]
    Kill,
}

/// <summary>범용 서버 설정: 전용 어댑터가 없는 게임도 프로세스 감시/백업/업데이트/스케줄은 그대로 사용 가능</summary>
public sealed class GenericSettings : GameSettings
{
    public override string GameId => GenericGame.Id;

    [DisplayName("추적 프로세스 이름"),
     Description("메모리/크래시 감시 대상 실제 서버 프로세스 이름 (확장자 제외). 비우면 실행 파일 이름 사용. " +
                 "bat 이나 런처로 실행하는 경우 실제 서버 exe 이름을 입력")]
    public string TrackedProcessName { get; set; } = "";

    [DisplayName("종료 방법")]
    public GenericStopMethod StopMethod { get; set; } = GenericStopMethod.CtrlC;
}

public sealed class GenericAdapter : GameServerAdapterBase
{
    private const string Src = "Generic";

    public GenericAdapter(IAppLog log) : base(log) { }

    private GenericSettings Settings => (GenericSettings)Profile.Game;

    public override GameCapabilities Capabilities =>
        Settings.StopMethod == GenericStopMethod.Kill ? GameCapabilities.None : GameCapabilities.GracefulShutdown;

    public override string TrackedProcessName =>
        string.IsNullOrWhiteSpace(Settings.TrackedProcessName)
            ? Path.GetFileNameWithoutExtension(Profile.ServerExePath)
            : Path.GetFileNameWithoutExtension(Settings.TrackedProcessName.Trim());

    public override Task<bool> RequestShutdownAsync(int serverPid, CancellationToken ct)
    {
        try
        {
            switch (Settings.StopMethod)
            {
                case GenericStopMethod.CtrlC:
                    var ok = ConsoleCtrl.SendCtrlC(serverPid);
                    if (!ok) Log.Warn(Src, "Ctrl+C 전송 실패 (콘솔 프로세스가 아니거나 접근 불가)");
                    return Task.FromResult(ok);

                case GenericStopMethod.CloseWindow:
                    using (var p = Process.GetProcessById(serverPid))
                    {
                        var sent = p.CloseMainWindow();
                        if (!sent) Log.Warn(Src, "메인 창을 찾지 못했습니다 (Windows Terminal 에서 실행 중이면 Ctrl+C 방식을 사용하세요)");
                        return Task.FromResult(sent);
                    }

                default:
                    return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Src, $"종료 요청 실패: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public override IEnumerable<string> CheckConfiguration()
    {
        if (!File.Exists(Profile.ServerExePath))
            yield return $"서버 실행 파일이 없습니다: {Profile.ServerExePath}";
    }
}

public static class GenericGame
{
    public const string Id = "generic";

    public static readonly GameDefinition Definition = new()
    {
        GameId = Id,
        DisplayName = "범용 (기타 데디 서버)",
        SettingsType = typeof(GenericSettings),
        CreateAdapter = log => new GenericAdapter(log),
        CreateDefaultProfile = () => new ServerProfile
        {
            Name = "새 서버",
            Game = new GenericSettings(),
            AppId = 0,
            ServerInstallDir = @"D:\Steam_Dedicated_Server\",
            BackupDir = @"D:\Steam_Dedicated_Server\Backups",
            BackupPrefix = "Server_Backup",
            MemoryThresholdGB = 8.0,
            ShutdownCountdown = "",
        }
    };
}
