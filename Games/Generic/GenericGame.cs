using System.ComponentModel;
using System.Diagnostics;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Games;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Services;

namespace GameServerKeeper.Games.Generic;

public enum GenericStopMethod
{
    [Description("Ctrl+C 보내기 (권장 · 대부분 저장 후 종료)")]
    CtrlC,
    [Description("창 닫기 요청")]
    CloseWindow,
    [Description("바로 강제 종료 (저장 안 됨)")]
    Kill,
}

/// <summary>범용 서버 설정: 전용 어댑터가 없는 게임도 프로세스 감시/백업/업데이트/스케줄은 그대로 사용 가능</summary>
public sealed class GenericSettings : GameSettings
{
    public override string GameId => GenericGame.Id;

    [DisplayName("감시할 프로세스 이름"),
     Description("메모리·크래시 감시 대상인 실제 서버 프로세스 이름이에요(확장자 제외). 비우면 실행 파일 이름을 써요. " +
                 "bat이나 런처로 실행하는 게임은 작업 관리자 → 세부 정보 탭에서 실제 서버 프로세스 이름을 확인해 입력하세요.")]
    public string TrackedProcessName { get; set; } = "";

    [DisplayName("종료 방법"),
     Description("서버를 끌 때 쓰는 방법이에요. 대부분의 데디 서버는 Ctrl+C를 받으면 저장하고 종료해요. " +
                 "정해진 시간 안에 안 꺼지면 강제 종료해요.")]
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
        Description = "전용 연동이 없는 아무 서버나 등록할 수 있어요. 크래시 복구·메모리 재시작·백업·업데이트·정기 재시작을 지원하고, 종료는 Ctrl+C로 해요.",
        SettingsNote = "인게임 공지·접속자 조회는 지원하지 않아서 관련 항목은 숨겨져 있어요.",
        SettingsType = typeof(GenericSettings),
        CreateAdapter = log => new GenericAdapter(log),
        // 자동 감지 대상 아님: 다른 게임으로 인식되지 않은 모든 실행 파일의 기본값
        CreateProfile = CreateProfile,
    };

    private static ServerProfile CreateProfile(string? exePath)
    {
        string install = "", exe = "", name = "새 서버";
        string backup = @"D:\Steam_Dedicated_Server\Backups";
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            exe = Path.GetFullPath(exePath);
            install = Path.GetDirectoryName(exe)!;
            name = Path.GetFileNameWithoutExtension(exe);
            backup = Path.Combine(Path.GetDirectoryName(install) ?? install, "Backups");
        }

        return new ServerProfile
        {
            Name = name,
            Game = new GenericSettings(),
            AppId = 0,
            ServerInstallDir = install,
            ServerExePath = exe,
            BackupDir = backup,
            BackupPrefix = "Server_Backup",
            MemoryThresholdGB = 8.0,
            ShutdownCountdown = "",
        };
    }
}
