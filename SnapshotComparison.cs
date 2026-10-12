using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Designer.Modules.SelectionProbe;

internal sealed class SnapshotComparison : Window
{
    private sealed record BankChoice(object Bank, string Name) { public override string ToString() => Name; }
    internal sealed class Cell : INotifyPropertyChanged
    {
        private string text = "Unavailable", detail = ""; private bool different;
        public string Text => text; public string Detail => detail; public bool Different => different;
        internal SnapshotDatum? Value;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Update(string display, string explanation, bool differs = false)
        {
            if (text == display && detail == explanation && different == differs) return;
            text = display; detail = explanation; different = differs; PropertyChanged?.Invoke(this, new(null));
        }
    }
    internal sealed class Row
    {
        internal SnapshotRow Source;
        public string Component => Source.Component; public string Control => string.IsNullOrWhiteSpace(Source.Control) ? Source.ControlId : Source.Control;
        public Cell Current { get; } = new(); public Cell[] Saved { get; }
        internal bool Different => Saved.Any(c => c.Different);
        internal Row(SnapshotRow source, int count) { Source = source; Saved = Enumerable.Range(0, count).Select(_ => new Cell()).ToArray(); }
    }
    private readonly ComboBox banks = new() { MinWidth = 220, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox search = new() { MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Filter component, path, control name or ID" };
    private readonly Button refresh = new() { Content = "Refresh saved values", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12, 0, 0, 0) };
    private readonly CheckBox differences = new() { Content = "Differences only", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, FrozenColumnCount = 3, EnableRowVirtualization = true, EnableColumnVirtualization = true, ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader };
    private readonly DispatcherTimer timer = new(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
    private SnapshotComparisonSource? source;
    private Row[] data = Array.Empty<Row>();
    private bool busy, closed, rebuilding, savedRead;
    private string savedStatus = "";
    internal SnapshotComparison(object? selectedBank)
    {
        Title = "Snapshot values"; Width = 1180; Height = 650; MinWidth = 720; MinHeight = 340;
        Owner = Application.Current.MainWindow; Background = Brushes.White; Foreground = Brushes.Black;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var top = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(refresh, Dock.Right); top.Children.Add(refresh); top.Children.Add(banks);
        top.Children.Add(new TextBlock { Text = "Filter", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); top.Children.Add(search);
        var options = new DockPanel(); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options); options.Children.Add(differences);
        options.Children.Add(new TextBlock { Text = "Orange cells differ from Current. Hover a cell for its full value and source.", Margin = new Thickness(16, 8, 0, 8) });
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(grid);
        refresh.Click += async (_, _) => await Reload(); banks.SelectionChanged += async (_, _) => { if (!rebuilding) await Reload(); };
        search.TextChanged += (_, _) => Filter(); differences.Checked += (_, _) => Filter(); differences.Unchecked += (_, _) => Filter();
        grid.MouseDoubleClick += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject node && ItemsControl.ContainerFromElement(grid, node) is DataGridRow row && row.Item is Row item)
                try { SnapshotTools.Navigate(item.Source.ComponentObject, item.Source.ControlId); }
                catch (Exception error) { MessageBox.Show(this, error.GetBaseException().Message, "Go to control"); }
        };
        PreviewKeyDown += (_, args) => { if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); args.Handled = true; } else if (args.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); args.Handled = true; } };
        timer.Tick += (_, _) => Tick(); Closed += (_, _) => { closed = true; timer.Stop(); source?.Dispose(); source = null; };
        RebuildBanks(selectedBank); Loaded += async (_, _) => { timer.Start(); await Reload(); };
    }
    private void RebuildBanks(object? selected)
    {
        rebuilding = true; banks.Items.Clear();
        foreach (var bank in SnapshotHost.Items(SnapshotHost.Get(SnapshotHost.Document, "Snapshots")).OrderBy(b => SnapshotHost.Type("Design.Snapshot.GlobalBank").IsInstanceOfType(b)))
            banks.Items.Add(new BankChoice(bank, SnapshotHost.Text(bank, "Name")));
        banks.SelectedItem = banks.Items.OfType<BankChoice>().FirstOrDefault(b => ReferenceEquals(b.Bank, selected)) ?? banks.Items.OfType<BankChoice>().FirstOrDefault(); rebuilding = false;
    }
    private async Task Reload()
    {
        if (busy || closed) return; busy = true; banks.IsEnabled = refresh.IsEnabled = false;
        source?.Dispose(); source = null; savedRead = false; savedStatus = "Reading saved values…"; data = Array.Empty<Row>(); grid.ItemsSource = data;
        try
        {
            var previous = (banks.SelectedItem as BankChoice)?.Bank; SnapshotTools.Refresh(); RebuildBanks(previous);
            if (banks.SelectedItem is not BankChoice chosen) { status.Text = "No snapshot bank is available."; return; }
            var membership = Members(chosen.Bank);
            if (membership.Length > 5000) throw new InvalidOperationException("This bank exceeds the 5,000-control comparison limit.");
            int count = Convert.ToInt32(SnapshotHost.Get(chosen.Bank, "Count"));
            if (count < 1 || count > 24) throw new InvalidOperationException("Snapshot count is outside the supported 1–24 range.");
            source = new(SnapshotReadContext.Capture(), membership, SnapshotHost.Text(chosen.Bank, "FileName")); var active = source;
            grid.Columns.Clear(); AddColumn("Component", "Component", 180); AddColumn("Control", "Control", 170); AddValueColumn(active.Context.CurrentLabel, "Current", 160);
            for (int i = 0; i < count; i++) AddValueColumn("Snapshot " + (i + 1), $"Saved[{i}]", 155);
            data = membership.Select(r => new Row(r, count)).ToArray(); Filter(); status.Text = "Reading saved values without recall…";
            await Task.Yield();
            if (SnapshotHost.Get(chosen.Bank, "IsJson") is true) throw new InvalidOperationException("This bank uses the JSON snapshot variant, which is not supported by the comparison reader yet.");
            await active.ReadAsync().WaitAsync(TimeSpan.FromSeconds(30)); if (closed || !ReferenceEquals(active, source)) return;
            for (int i = 0; i < count; i++)
            {
                string name = "controlset." + SnapshotHost.Text(chosen.Bank, "FileName") + ".snapshot." + (i + 1);
                active.Files.TryGetValue(name, out var file);
                foreach (var row in data)
                {
                    var cell = row.Saved[i];
                    if (file is null) { cell.Update("Not saved", "No file for this slot exists in the settings read from this source."); continue; }
                    if (file.Error is not null) { cell.Update("Unavailable", file.Error); continue; }
                    if (row.Source.SnapshotId != row.Source.ControlId) { cell.Update("Shared state", "This control uses snapshot proxy " + row.Source.SnapshotId + "; its individual value cannot be inferred from the shared state."); continue; }
                    if (file.Values!.TryGetValue(SnapshotComparisonSource.Key(row.Source, row.Source.SnapshotId), out var value))
                    { cell.Value = value; cell.Update(Short(Format(value, row.Source)), value.Display + "\n" + name); }
                    else cell.Update("Not stored", "This control is absent from the saved slot. QDS may restore its default on recall.");
                }
            }
            savedRead = true; savedStatus = active.Context.SavedLabel + " · read " + DateTime.Now.ToString("HH:mm:ss") + ". Saved columns refresh on request; Current follows QDS feedback.";
            Tick();
        }
        catch (Exception error)
        {
            savedStatus = "Saved values unavailable: " + error.GetBaseException().Message;
            foreach (var row in data) foreach (var cell in row.Saved) cell.Update("Unavailable", savedStatus);
            Tick(); if (source is null || data.Length == 0) status.Text = savedStatus;
        }
        finally { busy = false; banks.IsEnabled = refresh.IsEnabled = !closed; }
    }
    private static SnapshotRow[] Members(object bank)
    {
        if (!SnapshotHost.Type("Design.Snapshot.GlobalBank").IsInstanceOfType(bank)) return SnapshotTools.Model.Rows.Where(r => ReferenceEquals(r.BankObject, bank)).ToArray();
        var result = new List<SnapshotRow>();
        foreach (var component in SnapshotHost.Items(SnapshotHost.Get(SnapshotHost.Type("Design.ComponentCache"), "Components")))
            foreach (var descriptor in SnapshotHost.Descriptors(component))
            {
                string id = SnapshotHost.Text(descriptor, "ControlId"), proxy = SnapshotHost.Text(descriptor, "SnapshotProxy");
                if (SnapshotHost.Get(descriptor, "IsPresetable") is not true && proxy.Length == 0) continue;
                result.Add(new(bank, component, descriptor, new(SnapshotHost.Get(component, "Id")!, id), SnapshotHost.Text(bank, "Name"),
                    string.IsNullOrWhiteSpace(SnapshotHost.Text(component, "UserLabel")) ? SnapshotHost.Text(component, "Label") : SnapshotHost.Text(component, "UserLabel"),
                    SnapshotHost.Path(component), SnapshotHost.Text(descriptor, "PrettyName").Replace('~', ' '), id, "Global", proxy.Length == 0 ? id : proxy));
            }
        return result.DistinctBy(r => r.Key).OrderBy(r => r.Path).ThenBy(r => r.Component).ThenBy(r => r.Control).ToArray();
    }
    private void Tick()
    {
        if (closed || source is null) return;
        try
        {
            if (!source.Context.IsCurrent())
            {
                source.Dispose(); source = null; data = Array.Empty<Row>(); grid.ItemsSource = data;
                status.Text = "The design or connection changed. Refresh to read values in the new context."; return;
            }
            foreach (var row in data)
            {
                var current = source.Current(row.Source); row.Current.Value = current.Value;
                row.Current.Update(current.Value is null ? "Unavailable" : Short(Format(current.Value, row.Source)), current.Detail + (current.Value is null ? "" : "\nValue: " + current.Value.Display));
                foreach (var saved in row.Saved)
                    saved.Update(saved.Text, saved.Detail, current.Value is not null && saved.Value is not null && !current.Value.SameValue(saved.Value));
            }
            if (differences.IsChecked is true) Filter();
            status.Text = source.Context.CurrentLabel + ". " + savedStatus + "\n" + data.Count(r => r.Different) + " controls differ from at least one saved value. "
                + (savedRead ? "Not stored and unavailable cells are not counted as equal or different. " : "")
                + "Numbers use control values, not normalized positions. Double-click a row to find its source.";
        }
        catch (Exception error) { status.Text = "Comparison paused: " + error.GetBaseException().Message; }
    }
    internal static string Format(SnapshotDatum value, SnapshotRow row) => !value.IsText && SnapshotHost.Text(row.Descriptor, "ControlType") == "Boolean" && value.Number is 0 or 1
        ? value.Number == 1 ? "On" : "Off" : value.Display;
    private static string Short(string value) { value = value.Replace("\r", "\\r").Replace("\n", "\\n"); return value.Length > 100 ? value[..100] + "…" : value; }
    private void Filter()
    {
        string[] terms = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var visible = data.Where(r => (differences.IsChecked is not true || r.Different) && terms.All(t => $"{r.Source.Component} {r.Source.Control} {r.Source.ControlId} {r.Source.Path}".Contains(t, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (grid.ItemsSource is not Row[] previous || !previous.SequenceEqual(visible)) grid.ItemsSource = visible;
    }
    private void AddColumn(string header, string path, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width });
    private void AddValueColumn(string header, string path, double width)
    {
        var style = new Style(typeof(DataGridCell)); style.Setters.Add(new Setter(ToolTipProperty, new Binding(path + ".Detail")));
        var changed = new DataTrigger { Binding = new Binding(path + ".Different"), Value = true }; changed.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 228, 198)))); style.Triggers.Add(changed);
        grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path + ".Text"), CellStyle = style, Width = width });
    }
}
