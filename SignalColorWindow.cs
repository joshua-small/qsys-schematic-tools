using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ActiproSoftware.Windows.Controls.DataGrid;

namespace Designer.Modules.SelectionProbe;

internal sealed class SignalColorWindow : Window
{
    private readonly Func<string, object, string> receipt;
    private Dictionary<SignalRole, string> palette;
    private SignalColoring.Preview? preview;
    private readonly DataGrid grid;
    private readonly TextBlock status;
    private readonly ComboBox role;
    private readonly Button apply;
    private readonly CheckBox defaultColorsOnly;
    private static string PalettePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QsysSchematicTools", "Designer-10.5", "signal-palette.json");

    internal SignalColorWindow(Window owner, Func<string, object, string> saveReceipt)
    {
        receipt = saveReceipt;
        palette = LoadPalette();
        Owner = owner; Title = "Named Signal colors"; Width = 1080; Height = 580;
        // Implicit styles are keyed by the concrete type; this derived window
        // must explicitly use the host's Window style and track its replacements.
        SetResourceReference(StyleProperty, typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(12) };
        var hint = new TextBlock { Text = "Preview selected signal tags, pins, or components, or use Color all named signals to preview the entire design. Review the proposed colors, then Apply preview. Matching tags across pages change together.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(new TextBlock { Text = "Role:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        role = new ComboBox { Width = 205, Margin = new Thickness(0, 0, 8, 0) };
        role.Items.Add("Automatic suggestion");
        foreach (var r in Enum.GetValues<SignalRole>()) role.Items.Add(SignalColorRules.Title(r));
        role.SelectedIndex = 0;
        toolbar.Children.Add(role);
        var read = Button("Preview selection"); read.Click += (_, _) => PreviewColors(SignalColorScope.Selection); toolbar.Children.Add(read);
        var all = Button("Color all named signals...");
        all.ToolTip = "Preview every Named Signal group across the design, using the selected Role and palette. Nothing changes until Apply preview.";
        all.Click += (_, _) => PreviewColors(SignalColorScope.EntireDesign); toolbar.Children.Add(all);
        apply = Button("Apply preview"); apply.IsEnabled = false; apply.Click += (_, _) => Apply(); toolbar.Children.Add(apply);
        var edit = Button("Edit palette..."); edit.Click += (_, _) => EditPalette(); toolbar.Children.Add(edit);
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        defaultColorsOnly = new CheckBox { Content = "Only change default #D5DCEF (preserve other colors)", IsChecked = false,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Off by default: apply role colors regardless of current color. When checked, skip any matching-name group containing a non-default color, including with an explicit Role." };
        DockPanel.SetDock(defaultColorsOnly, Dock.Top); panel.Children.Add(defaultColorsOnly);
        status = new TextBlock { Text = "Nothing changed. Select names and preview their colors.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        // Designer already ships this theme-aware DataGrid. Keep its headers,
        // rows, selection, and scrollbars on the host's live light/dark theme.
        grid = new ThemedDataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, EnableRowVirtualization = true };
        foreach (var c in new[] { ("Signal name", "Name", 190d), ("Role", "Role", 145d), ("Current", "Current", 115d),
            ("Proposed", "Proposed", 90d), ("Pins", "AffectedPins", 45d), ("Outside selection", "OutsideSelection", 85d), ("Reason", "Reason", 380d) })
            grid.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2), Width = c.Item3 });
        panel.Children.Add(grid); Content = panel;
        role.SelectionChanged += (_, _) => Invalidate("Role changed. Preview again to see the proposed colors.");
        defaultColorsOnly.Checked += (_, _) => Invalidate("Color filter changed. Preview again.");
        defaultColorsOnly.Unchecked += (_, _) => Invalidate("Color filter changed. Preview again.");
    }

    private static Button Button(string title) => new() { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
    private void Invalidate(string message) { preview = null; apply.IsEnabled = false; grid.ItemsSource = null; status.Text = message; }
    private void Error(Exception error)
    {
        Invalidate(error.GetBaseException().Message);
        try { receipt("signal-color-error", new { error = error.ToString() }); } catch { }
    }
    private void PreviewColors(SignalColorScope scope)
    {
        try
        {
            Invalidate(scope == SignalColorScope.EntireDesign ? "Reading all Named Signals in the design..." : "Reading selection...");
            var forced = role.SelectedIndex > 0 ? Enum.GetValues<SignalRole>()[role.SelectedIndex - 1] : (SignalRole?)null;
            var plan = SignalColoring.Read(palette, forced, scope, defaultColorsOnly.IsChecked == true);
            receipt("signal-color-preview", new { hostState = SelectionMover.ReadHostState(), plan = SignalColoring.Describe(plan) });
            preview = plan; grid.ItemsSource = plan.Groups;
            grid.Columns[5].Visibility = scope == SignalColorScope.Selection ? Visibility.Visible : Visibility.Collapsed;
            var changes = plan.Groups.Count(g => g.Changes);
            apply.IsEnabled = changes > 0;
            var scopeName = scope == SignalColorScope.EntireDesign ? "Entire design" : "Selected groups";
            status.Text = plan.Groups.Length == 0
                ? scope == SignalColorScope.EntireDesign ? "No Named Signals found in this design. Nothing changed."
                    : "No named pins found. Select a signal tag or a component with Named Signals."
                : $"{scopeName}: {plan.Groups.Length} signal group(s); {changes} would change. Review the reasons and pin counts, then Apply preview. No colors changed yet.";
        }
        catch (Exception error) { Error(error); }
    }
    private void Apply()
    {
        try
        {
            var plan = preview ?? throw new InvalidOperationException("Preview the selection first.");
            Invalidate("Applying preview...");
            status.Text = SignalColoring.Apply(plan, receipt);
        }
        catch (Exception error) { Error(error); }
    }

    private static Dictionary<SignalRole, string> LoadPalette()
    {
        var result = SignalColorRules.Defaults();
        if (!File.Exists(PalettePath)) return result;
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PalettePath))
            ?? throw new InvalidDataException("The saved signal palette is empty.");
        foreach (var pair in saved)
        {
            if (!Enum.TryParse<SignalRole>(pair.Key, out var r) || !Enum.IsDefined(r))
                throw new InvalidDataException("Unknown saved palette role: " + pair.Key);
            result[r] = SignalColorRules.Normalize(pair.Value);
        }
        return result;
    }

    private void EditPalette()
    {
        var window = new Window { Title = "Signal palette", Owner = this, Width = 500, Height = 680,
            MaxHeight = SystemParameters.WorkArea.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = "Editable starting conventions from the reference designs. Enter #RRGGBB colors. Saved choices apply across designs; saving this palette does not recolor a design.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var inputs = new Dictionary<SignalRole, TextBox>();
        foreach (var pair in palette)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            row.Children.Add(new TextBlock { Text = SignalColorRules.Title(pair.Key), Width = 240, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBox { Text = pair.Value, Width = 110 }; inputs[pair.Key] = text; row.Children.Add(text); body.Children.Add(row);
        }
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = Button("Save palette"); save.Margin = new Thickness(0, 8, 0, 8);
        save.Click += (_, _) =>
        {
            try
            {
                var updated = inputs.ToDictionary(x => x.Key, x => SignalColorRules.Normalize(x.Value.Text));
                Directory.CreateDirectory(Path.GetDirectoryName(PalettePath)!);
                var temporary = PalettePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(updated.ToDictionary(x => x.Key.ToString(), x => x.Value), new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temporary, PalettePath, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                palette = updated;
                Invalidate("Palette saved. Preview again to use the new colors.");
                window.Close();
            }
            catch (Exception error) { message.Text = error.Message; }
        };
        body.Children.Add(save); body.Children.Add(message);
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
    }
}
