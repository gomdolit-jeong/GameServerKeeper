using System.ComponentModel;
using GameServerKeeper.Core.Logging;
using GameServerKeeper.ViewModels;
using GameServerKeeper.Views;
using Forms = System.Windows.Forms;

namespace GameServerKeeper.Services;

/// <summary>
/// 시스템 트레이 아이콘. WPF 에는 트레이 아이콘이 없어서 WinForms NotifyIcon 을 쓴다 (WinForms 는 여기서만 사용).
/// 메뉴는 열 때마다 서버 목록으로 다시 만든다.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _icon = new();
    private readonly Forms.ContextMenuStrip _menu = new();

    public TrayService(MainViewModel vm, MainWindow window)
    {
        _vm = vm;
        _window = window;

        _icon.Icon = AppIcon.Icon;
        _icon.Text = MainViewModel.AppTitle;
        _icon.ContextMenuStrip = _menu;
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => _window.ShowFromTray();
        _menu.Opening += (_, _) => BuildMenu();
        BuildMenu();

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ErrorLogged += OnErrorLogged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TrayText)) _icon.Text = _vm.TrayText;
    }

    private void OnErrorLogged(LogEntry e)
    {
        if (!_window.IsVisible)
            _icon.ShowBalloonTip(5000, e.ScopeName ?? MainViewModel.AppTitle, e.Message, Forms.ToolTipIcon.Error);
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add("열기", null, (_, _) => _window.ShowFromTray());
        _menu.Items.Add(new Forms.ToolStripSeparator());

        foreach (var vm in _vm.Servers)
        {
            var s = vm.Server;
            var (stateText, _) = Format.State(s.State);
            var sub = new Forms.ToolStripMenuItem($"{s.Name}  —  {stateText}");
            sub.DropDownItems.Add("시작", null, async (_, _) => await s.StartServerAsync()).Enabled = !s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("안전 종료 (카운트다운)", null, async (_, _) => await s.StopServerAsync(true)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("재시작 (카운트다운)", null, async (_, _) => await s.RestartServerAsync(true, false, s.Profile.UpdateBeforeStart)).Enabled = s.IsServerRunning && !s.IsBusy;
            sub.DropDownItems.Add("즉시 백업", null, async (_, _) => await s.BackupNowAsync()).Enabled = !s.IsBusy;
            sub.DropDownItems.Add(new Forms.ToolStripSeparator());
            sub.DropDownItems.Add("화면 열기", null, (_, _) => { _window.ShowFromTray(); _vm.Open(vm); });
            _menu.Items.Add(sub);
        }

        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("프로그램 종료", null, (_, _) => _window.RequestExit());
    }

    public void Dispose()
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.ErrorLogged -= OnErrorLogged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
