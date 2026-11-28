using System.Windows;
using System.Windows.Input;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

/// <summary>MessageBox 대신 쓰는 선택 대화상자. Esc 는 "닫기"(-1).</summary>
public partial class ChoiceWindow : Window
{
    public ChoiceWindow(ChoiceViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Icon = AppIcon.ImageSource;
        vm.CloseRequested += result => DialogResult = result;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        };
    }
}
