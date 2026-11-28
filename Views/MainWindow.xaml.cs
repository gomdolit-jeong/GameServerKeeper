using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

/// <summary>
/// 메인 창. 화면 갱신 타이머, 트레이로 숨기기, 닫기 동작처럼 창 자체의 동작만 맡는다.
/// (내용은 전부 <see cref="MainViewModel"/> 바인딩)
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _exitRequested;
    private bool _exiting;
    private bool _firstRender = true;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Icon = AppIcon.ImageSource;

        _timer.Tick += (_, _) => _vm.Tick(IsVisible && WindowState != WindowState.Minimized);
        _vm.ShowWindowRequested += ShowFromTray;

        Loaded += (_, _) =>
        {
            _timer.Start();
            _vm.Initialize();
        };
        ContentRendered += (_, _) =>
        {
            if (!_firstRender) return;
            _firstRender = false;
            if (_vm.Config.StartMinimized) HideToTray();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && _vm.Config.MinimizeToTray) HideToTray();
        };
        // Windows 종료/로그오프 시에는 묻지 않고 그대로 닫는다
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    /// <summary>트레이 메뉴의 "프로그램 종료"</summary>
    public void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    public void HideToTray() => Hide();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_exiting)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (!_exitRequested && _vm.Config.CloseToTray)
        {
            HideToTray();
            return;
        }

        _exitRequested = false;
        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        if (!await _vm.PrepareExitAsync()) return;
        _exiting = true;
        _timer.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        Application.Current.Shutdown();
    }
}
