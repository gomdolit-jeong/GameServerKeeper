using System.Runtime.InteropServices;

namespace GameServerKeeper.Core.Services;

/// <summary>
/// 콘솔 서버 프로세스에 Ctrl+C 를 보낸다 (REST/RCON 이 없는 게임의 정상 종료용).
/// 많은 데디케이트 서버가 Ctrl+C 를 받으면 저장 후 종료한다.
/// 프로세스는 한 번에 하나의 콘솔에만 붙을 수 있으므로 전역 lock 으로 직렬화한다.
/// </summary>
public static class ConsoleCtrl
{
    private const uint CtrlCEvent = 0;
    private static readonly object Lock = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    public static bool SendCtrlC(int pid)
    {
        lock (Lock)
        {
            FreeConsole(); // WinForms 앱은 원래 콘솔이 없음 — 혹시 붙어 있으면 해제
            if (!AttachConsole((uint)pid)) return false;
            try
            {
                // 우리 프로세스는 Ctrl+C 를 무시하도록 한 뒤 콘솔 전체에 Ctrl+C 전송
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                return GenerateConsoleCtrlEvent(CtrlCEvent, 0);
            }
            finally
            {
                FreeConsole();
                // 이벤트가 비동기로 전달되므로 잠시 기다린 뒤 무시 설정 해제
                // (해제하지 않으면 이후 띄우는 서버 프로세스가 Ctrl+C 무시 상태를 상속함)
                Thread.Sleep(500);
                SetConsoleCtrlHandler(IntPtr.Zero, false);
            }
        }
    }
}
