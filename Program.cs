using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;
using GameServerKeeper.UI;

namespace GameServerKeeper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 중복 실행 방지 (두 매니저가 같은 서버를 동시에 제어하면 꼬임)
        using var mutex = new Mutex(true, @"Local\GameServerKeeper_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("GameServerKeeper가 이미 실행 중입니다.\n시스템 트레이를 확인하세요.",
                "GameServerKeeper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var logger = new Logger(Path.Combine(AppContext.BaseDirectory, "logs"));

        Application.ThreadException += (_, e) => logger.Error("App", "처리되지 않은 UI 예외", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Error("App", "처리되지 않은 예외", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error("App", "관찰되지 않은 Task 예외", e.Exception);
            e.SetObserved();
        };

        logger.Info("App", "===== GameServerKeeper 시작 =====");
        var config = ConfigManager.Load(logger);

        using var manager = new ServerManager(config, logger);
        Application.Run(new MainForm(manager, logger));

        logger.Info("App", "===== GameServerKeeper 종료 =====");
    }
}
