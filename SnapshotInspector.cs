using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Designer.Modules.SelectionProbe;

internal sealed class SnapshotInspector : Window
{
    private sealed record BankChoice(object? Bank, string Name) { public override string ToString() => Name; }
    private readonly ComboBox banks = new() { MinWidth = 200, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox search = new() { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid rows = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, EnableRowVirtualization = true };
    private bool updating;
    internal SnapshotInspector()
    {
        Title = "Snapshot Inspector"; Width = 1080; Height = 620; MinWidth = 680; MinHeight = 320;
        Owner = Application.Current.MainWindow; Background = Brushes.White; Foreground = Brushes.Black;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var go = new Button { Content = "Go to control", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(go, Dock.Right); top.Children.Add(go); top.Children.Add(banks); top.Children.Add(search);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        var gather = new Button { Content = "Create control page from results", Padding = new Thickness(10, 4, 10, 4) }; actions.Children.Add(gather);
        var compare = new Button { Content = "Compare values...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12, 0, 0, 0) }; actions.Children.Add(compare);
        compare.Click += (_, _) => new SnapshotComparison((banks.SelectedItem as BankChoice)?.Bank).Show();
        gather.Click += (_, _) =>
        {
            try
            {
                SnapshotWorkspace.Create(rows.Items.OfType<SnapshotRow>().ToArray(), (banks.SelectedItem as BankChoice)?.Name ?? "Controls");
                status.Text = "Created a page of linked controls. Save the design to keep it. Undo removes the new page.";
            }
            catch (Exception ex) { status.Text = ex.GetBaseException().Message; }
        };
        search.ToolTip = "Filter by bank, component, location, label or control ID";
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(rows);
        foreach (var column in new[] { ("Bank", "Bank", 140.0), ("Location", "Path", 140.0), ("Component", "Component", 190.0), ("Control", "Control", 190.0), ("Control ID", "ControlId", 120.0), ("Included by", "IncludedBy", 140.0) })
            rows.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = column.Item3 });
        go.Click += (_, _) => Go(); rows.MouseDoubleClick += (_, args) => { if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(rows, source) is DataGridRow) Go(); };
        rows.PreviewKeyDown += (_, args) => { if (args.Key == Key.Enter) { Go(); args.Handled = true; } };
        search.TextChanged += (_, _) => Filter(); banks.SelectionChanged += (_, _) => Filter();
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); args.Handled = true; } else if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); args.Handled = true; } };
        SnapshotTools.Changed += Reload; Closed += (_, _) => SnapshotTools.Changed -= Reload;
        Reload();
    }
    internal void SelectBank(object? bank) => banks.SelectedItem = banks.Items.OfType<BankChoice>().FirstOrDefault(b => ReferenceEquals(b.Bank, bank)) ?? banks.Items[0];
    private void Reload()
    {
        var selected = (banks.SelectedItem as BankChoice)?.Bank; updating = true;
        banks.Items.Clear(); banks.Items.Add(new BankChoice(null, "All user-defined banks"));
        foreach (var group in SnapshotTools.Model.Rows.GroupBy(r => r.BankObject)) banks.Items.Add(new BankChoice(group.Key, group.First().Bank));
        SelectBank(selected); updating = false; Filter();
    }
    private void Filter()
    {
        if (updating) return;
        var bank = (banks.SelectedItem as BankChoice)?.Bank;
        string[] terms = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var filtered = SnapshotTools.Model.Rows.Where(r => (bank is null || ReferenceEquals(bank, r.BankObject))
            && terms.All(t => $"{r.Bank} {r.Path} {r.Component} {r.Control} {r.ControlId} {r.IncludedBy}".Contains(t, StringComparison.OrdinalIgnoreCase))).ToArray();
        var selected = rows.SelectedItem as SnapshotRow; rows.ItemsSource = filtered;
        rows.SelectedItem = filtered.FirstOrDefault(r => r.Key == selected?.Key && ReferenceEquals(r.BankObject, selected.BankObject));
        status.Text = $"{filtered.Length} memberships · {filtered.Select(r => r.Key).Distinct().Count()} controls. Global is implicit and excluded.\nDouble-click or press Enter to open and highlight the source control.";
        if (SnapshotTools.Model.Warnings.Length > 0) status.Text += "\n" + string.Join("\n", SnapshotTools.Model.Warnings);
    }
    private void Go()
    {
        if (rows.SelectedItem is not SnapshotRow row) return;
        try { if (!SnapshotTools.Navigate(row.ComponentObject, row.ControlId)) status.Text = "This control is no longer available. The membership list will refresh when the design changes."; }
        catch (Exception ex) { status.Text = ex.GetBaseException().Message; }
    }
}
