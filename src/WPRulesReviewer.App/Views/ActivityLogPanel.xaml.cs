using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using WPRulesReviewer.App.ViewModels;

namespace WPRulesReviewer.App.Views;

public partial class ActivityLogPanel : UserControl
{
    private INotifyCollectionChanged? _entries;

    public ActivityLogPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_entries is not null) _entries.CollectionChanged -= OnEntriesChanged;
        if (DataContext is ActivityLogViewModel vm)
        {
            _entries = vm.Entries;
            _entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not ActivityLogViewModel vm || !vm.AutoScroll) return;
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        // Defer the scroll: the ListBox must process this same CollectionChanged
        // event first, otherwise ScrollIntoView throws "ItemsControl is inconsistent
        // with its items source".
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (EntryList.Items.Count > 0)
                EntryList.ScrollIntoView(EntryList.Items[^1]);
        }));
    }
}
