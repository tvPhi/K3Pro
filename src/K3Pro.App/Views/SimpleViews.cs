using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using K3Pro.App.ViewModels;

namespace K3Pro.App.Views;

public partial class LightingView : UserControl
{
    public LightingView() => InitializeComponent();
}

public partial class DeviceView : UserControl
{
    public DeviceView() => InitializeComponent();
}

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogViewModel vm) vm.Entries.CollectionChanged += ScrollToEnd;
        };
    }

    private void ScrollToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems?[^1] is not { } last) return;
        Dispatcher.UIThread.Post(() => List.ScrollIntoView(last), DispatcherPriority.Background);
    }
}
