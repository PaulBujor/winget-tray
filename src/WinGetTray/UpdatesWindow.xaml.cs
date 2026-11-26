// Updates window: filterable list of pending upgrades, update one/many/all.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinGetTray;

public partial class UpdatesWindow : Window
{
    private readonly Action<IReadOnlyList<Upgrade>> _onUpdate;
    private readonly Action _onRefresh;
    private List<Upgrade> _all = new();

    public UpdatesWindow(Action<IReadOnlyList<Upgrade>> onUpdate, Action onRefresh)
    {
        _onUpdate = onUpdate;
        _onRefresh = onRefresh;

        InitializeComponent();
        ApplyTheme();

        SourceInitialized += (_, _) => Theme.ApplyWindowHints(this);
        Loaded += (_, _) => { ApplyFilter(); SearchBox.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); e.Handled = true; }
        };
    }

    private void ApplyTheme()
    {
        var p = Theme.Current;
        Background = Theme.Brush(p.Window);
        Resources["WindowBg"] = Theme.Brush(p.Window);
        Resources["CardBg"] = Theme.Brush(p.Card);
        Resources["StrokeBrush"] = Theme.Brush(p.Stroke);
        Resources["TextPrimaryBrush"] = Theme.Brush(p.Text);
        Resources["TextSecondaryBrush"] = Theme.Brush(p.TextSecondary);
        Resources["HoverBrush"] = Theme.Brush(p.Hover);
        Resources["AccentBrush"] = Theme.Brush(p.Accent);
        Resources["AccentTextBrush"] = Theme.Brush(p.AccentText);

        // Selection fill: accent washed over the card colour.
        var selected = Color.FromArgb(0x38, p.Accent.R, p.Accent.G, p.Accent.B);
        Resources["SelectedBrush"] = Theme.Brush(selected);
        Resources["ScrollThumbBrush"] = Theme.Brush(Color.FromArgb(0x66, p.TextSecondary.R, p.TextSecondary.G, p.TextSecondary.B));
    }

    /// <summary>New data from a check; keeps the current filter text.</summary>
    public void SetData(IReadOnlyList<Upgrade> upgrades)
    {
        _all = upgrades.ToList();
        ApplyFilter();
    }

    public void SetBusy(bool busy, string? message = null)
    {
        CheckButton.IsEnabled = !busy;
        AllButton.IsEnabled = !busy && _all.Count > 0;
        SelectedButton.IsEnabled = !busy && List.SelectedItems.Count > 0;
        if (message is not null)
        {
            StatusText.Text = message;
        }
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var filtered = UpgradeFilter.Apply(_all, q);

        List.ItemsSource = filtered;
        SearchPlaceholder.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        EmptyText.Text = _all.Count == 0 ? "Nothing to update" : "No matches";
        EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = _all.Count == 0
            ? "Up to date"
            : q.Length == 0
                ? $"{_all.Count} update{(_all.Count == 1 ? "" : "s")} available"
                : $"{filtered.Count} of {_all.Count}";

        AllButton.IsEnabled = _all.Count > 0;
        SelectedButton.IsEnabled = List.SelectedItems.Count > 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && List.Items.Count > 0)
        {
            List.SelectedIndex = 0;
            List.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && List.SelectedItem is Upgrade single)
        {
            _onUpdate(new[] { single });
            e.Handled = true;
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedButton.IsEnabled = List.SelectedItems.Count > 0;
        SelectedButton.Content = List.SelectedItems.Count > 1
            ? $"Update {List.SelectedItems.Count} selected"
            : "Update selected";
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is Upgrade one)
        {
            _onUpdate(new[] { one });
        }
    }

    private void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var picked = List.SelectedItems.Cast<Upgrade>().ToList();
        if (picked.Count > 0)
        {
            _onUpdate(picked);
        }
    }

    private void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_all.Count > 0)
        {
            _onUpdate(_all);
        }
    }

    private void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Checking…");
        _onRefresh();
    }

    /// <summary>Keeps the window from keeping the app alive once the tray exits.</summary>
    public void CloseQuietly()
    {
        try
        {
            Close();
        }
        catch
        {
            // already gone
        }
    }
}
