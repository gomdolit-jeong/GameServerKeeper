using System.Windows;
using System.Windows.Controls;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views;

/// <summary>설정 창. 목차를 누르면 해당 섹션으로 스크롤하는 동작만 코드 비하인드에 있다.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Icon = AppIcon.ImageSource;
        vm.CloseRequested += result => DialogResult = result;
        Scroll.ScrollChanged += (_, e) => { if (e.VerticalChange != 0) SyncNavToScroll(); };
    }

    private bool _syncing;

    /// <summary>스크롤하면 목차에서 지금 보고 있는 섹션을 표시</summary>
    private void SyncNavToScroll()
    {
        SettingSectionViewModel? current = null;
        foreach (var item in SectionList.Items)
        {
            if (SectionList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c) continue;
            if (c.TranslatePoint(new Point(0, 0), Scroll).Y <= 60) current = (SettingSectionViewModel)item;
            else break;
        }
        // 맨 아래까지 내렸으면 마지막 섹션
        if (Scroll.VerticalOffset >= Scroll.ScrollableHeight - 1 && SectionList.Items.Count > 0)
            current = (SettingSectionViewModel)SectionList.Items[^1];
        if (current == null || ReferenceEquals(NavList.SelectedItem, current)) return;

        _syncing = true;
        NavList.SelectedItem = current;
        _syncing = false;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 사용자가 목차를 눌렀을 때만 (스크롤 동기화나 게임 변경으로 다시 만들 때는 스크롤 유지)
        if (_syncing || (!NavList.IsMouseOver && !NavList.IsKeyboardFocusWithin)) return;
        if (NavList.SelectedItem is not SettingSectionViewModel section) return;
        if (SectionList.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement container) return;

        var top = container.TranslatePoint(new Point(0, 0), Scroll).Y;
        Scroll.ScrollToVerticalOffset(Math.Max(0, Scroll.VerticalOffset + top - 4));
    }
}
