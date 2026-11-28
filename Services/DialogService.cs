using System.Windows;
using GameServerKeeper.ViewModels;
using GameServerKeeper.Views;

namespace GameServerKeeper.Services;

/// <summary><see cref="IDialogService"/> 의 WPF 구현</summary>
public sealed class DialogService : IDialogService
{
    /// <summary>지금 활성 창(없으면 메인 창). 트레이로 숨겨져 있으면 소유 창 없이 화면 가운데에 띄운다.</summary>
    private static Window? Owner()
    {
        var app = Application.Current;
        var w = app.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? app.MainWindow;
        return w is { IsVisible: true } ? w : null;
    }

    private static bool ShowModal(Window window)
    {
        var owner = Owner();
        if (owner != null && !ReferenceEquals(owner, window)) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }

    public int Choose(string title, string message, IReadOnlyList<DialogChoice> choices)
    {
        var vm = new ChoiceViewModel(title, message, choices);
        ShowModal(new ChoiceWindow(vm));
        return vm.Result;
    }

    public void Alert(string title, string message) =>
        Choose(title, message, [new DialogChoice("확인", null, ChoiceKind.Primary)]);

    public string? Prompt(string title, string caption, string okText)
    {
        var vm = new PromptViewModel(title, caption, okText);
        return ShowModal(new PromptWindow(vm)) ? vm.Text.Trim() : null;
    }

    public bool ShowSettings(SettingsViewModel vm) => ShowModal(new SettingsWindow(vm));

    public bool ShowAddServer(AddServerViewModel vm) => ShowModal(new AddServerWindow(vm));

    public string? PickFile(string title, string filter, string? initialPath)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = false };
        try
        {
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                if (Directory.Exists(initialPath)) dlg.InitialDirectory = initialPath;
                else
                {
                    var dir = Path.GetDirectoryName(initialPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                    dlg.FileName = Path.GetFileName(initialPath);
                }
            }
        }
        catch { /* 잘못된 경로 문자열 무시 */ }
        return dlg.ShowDialog(Owner()) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title, string? initialPath)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        try
        {
            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)) dlg.InitialDirectory = initialPath;
        }
        catch { /* 무시 */ }
        return dlg.ShowDialog(Owner()) == true ? dlg.FolderName : null;
    }
}
