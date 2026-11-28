using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GameServerKeeper.ViewModels;

namespace GameServerKeeper.Views.Controls;

/// <summary>
/// 로그 창. 맨 아래를 보고 있을 때만 새 줄을 따라 자동 스크롤한다
/// (위로 올려서 지난 로그를 읽는 중에는 화면이 튀지 않음). 순수한 화면 동작이라 코드 비하인드에 둔다.
/// </summary>
public partial class LogView : UserControl
{
    private LogViewModel? _vm;
    private ScrollViewer? _scroll;
    private bool _scrollQueued;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as LogViewModel);
        Loaded += (_, _) =>
        {
            Attach(DataContext as LogViewModel);
            _scroll ??= FindChild<ScrollViewer>(List);
            ScrollToEnd();
        };
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(LogViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm != null) _vm.Lines.CollectionChanged -= OnLinesChanged;
        _vm = vm;
        if (_vm != null) _vm.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollQueued) return;
        bool atBottom = _scroll == null || _scroll.VerticalOffset >= _scroll.ScrollableHeight - 4;
        if (!atBottom) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollQueued = false;
            ScrollToEnd();
        });
    }

    private void ScrollToEnd()
    {
        if (_scroll != null) _scroll.ScrollToEnd();
        else if (List.Items.Count > 0) List.ScrollIntoView(List.Items[^1]);
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }
}
