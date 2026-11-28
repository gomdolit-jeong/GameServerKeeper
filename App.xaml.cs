using System.Windows;
using System.Windows.Controls;
using GameServerKeeper.Core.Config;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.Core.Server;
using GameServerKeeper.Services;
using GameServerKeeper.ViewModels;
using GameServerKeeper.Views;

namespace GameServerKeeper;

/// <summary>
/// 시작점. 객체를 조립만 한다:
/// Core(ServerManager) → Services(대화상자·트레이) → ViewModel(MainViewModel) → View(MainWindow)
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private Logger? _logger;
    private ServerManager? _manager;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 중복 실행 방지 (두 매니저가 같은 서버를 동시에 제어하면 꼬임)
        _mutex = new Mutex(true, @"Local\GameServerKeeper_SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("GameServerKeeper가 이미 실행 중입니다.\n시스템 트레이를 확인하세요.",
                "GameServerKeeper", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles(); // 트레이 메뉴 모양
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(350));
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(30000));

        var logger = _logger = new Logger(Path.Combine(AppContext.BaseDirectory, "logs"));
        DispatcherUnhandledException += (_, ev) =>
        {
            logger.Error("App", "처리되지 않은 UI 예외", ev.Exception);
            ev.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            logger.Error("App", "처리되지 않은 예외", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            logger.Error("App", "관찰되지 않은 Task 예외", ev.Exception);
            ev.SetObserved();
        };

        logger.Info("App", "===== GameServerKeeper 시작 =====");
        var config = ConfigManager.Load(logger);
        _manager = new ServerManager(config, logger);

        var vm = new MainViewModel(_manager, logger, new DialogService(), new ShellService(logger));
        var window = new MainWindow(vm);
        MainWindow = window;
        _tray = new TrayService(vm, window);
        window.Show();
#if DEBUG
        DebugSnapshot.StartIfRequested();
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _manager?.Dispose();
        _logger?.Info("App", "===== GameServerKeeper 종료 =====");
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
