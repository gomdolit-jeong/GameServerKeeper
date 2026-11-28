using System.Windows;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

public partial class AddServerWindow : Window
{
    public AddServerWindow(AddServerViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Icon = AppIcon.ImageSource;
        vm.CloseRequested += result => DialogResult = result;
        Loaded += (_, _) => ExeBox.Focus();
    }
}
