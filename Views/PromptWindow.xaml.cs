using System.Windows;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

public partial class PromptWindow : Window
{
    public PromptWindow(PromptViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Icon = AppIcon.ImageSource;
        vm.CloseRequested += result => DialogResult = result;
        Loaded += (_, _) => Input.Focus();
    }
}
