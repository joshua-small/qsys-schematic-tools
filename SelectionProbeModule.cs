using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.SelectionProbe;

/// <summary>Experimental selection reader and explicit geometry actions for an opted-in host.</summary>
public sealed class SelectionProbeModule : IDesignerModule
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private Window? reportWindow;
    private TextBox? reportText;
    private SignalColorWindow? colorWindow;
    private System.Windows.Threading.DispatcherTimer? startupRead;
    private PackingCommandRouter? packingCommands;
    private EditorShortcuts? editorShortcuts;
    private DispatcherTimer? startupBindings;
    private int bindingAttempts;
    private string gapText = "20";
    private string shortcutStatus = "Packing shortcuts are initializing.";
    public string Description => "Pack selected components and Layout items with exposed control-pin spacing and an adjustable gap.";
    public string MenuTitle => "Schematic Tools...";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => ShowReport;

    public SelectionProbeModule()
    {
        // This receipt distinguishes module instantiation from successful UI invocation.
        if (HostInstallation.Enabled)
        {
            try { SaveReceipt("module-created", new { module = GetType().Assembly.FullName }); }
            catch (Exception) { /* Evidence I/O must not break Designer startup. */ }
            try { PaletteRows.Install((kind, detail) => { SaveReceipt(kind, detail); }); }
            catch (Exception error) { ShowError(error); }
            try { IconSearch.Install((kind, detail) => { SaveReceipt(kind, detail); }); }
            catch (Exception error) { ShowError(error); }
            try { ControlsInfoSearch.Install((kind, detail) => { SaveReceipt(kind, detail); }); }
            catch (Exception error) { ShowError(error); }
            try { SnapshotTools.Install((kind, detail) => { SaveReceipt(kind, detail); }); }
            catch (Exception error) { ShowError(error); }
            // Designer awaits plugin initialization inside Loaded before adding native commands.
            // Dispatcher idle alone does not mean that asynchronous startup has completed.
            startupBindings = new DispatcherTimer(DispatcherPriority.ContextIdle)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            startupBindings.Tick += (_, _) => InstallPackingCommands();
            startupBindings.Start();
            // Explicit lab-only marker enables one read after the editor has initialized.
            // This permits a runtime API check without synthesizing input or changing selection.
            if (HostInstallation.IsLab && File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-startup-read")))
            {
                startupRead = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                startupRead.Tick += (_, _) =>
                {
                    startupRead.Stop();
                    startupRead = null;
                    RefreshReport();
                };
                startupRead.Start();
            }
        }
    }

    private static string OutputDirectory => HostInstallation.OutputDirectory;

    private void InstallPackingCommands()
    {
        if (packingCommands is not null || !HostInstallation.Enabled) { StopBindingStartup(); return; }
        try
        {
            if (++bindingAttempts > 120)
                throw new InvalidOperationException("Designer did not finish setting up native packing commands within the startup check period.");
            var window = Application.Current.MainWindow;
            if (window is null || !window.IsLoaded || !window.IsEnabled) return;
            var commands = new Dictionary<RoutedCommand, PackAlignment>();
            foreach (var alignment in new[] { PackAlignment.Left, PackAlignment.Right, PackAlignment.Top, PackAlignment.Bottom })
            {
                var type = SelectionMover.HostType("QSC.QSys.Commands.Pack" + alignment);
                var command = type.GetProperty("Command", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as RoutedCommand
                    ?? throw new MissingMemberException(type.FullName, "Command");
                var count = window.CommandBindings.Cast<CommandBinding>().Count(binding => ReferenceEquals(binding.Command, command));
                if (count == 0) return;
                if (count != 1)
                    throw new InvalidOperationException("Expected exactly one native binding for " + command.Name);
                commands.Add(command, alignment);
            }
            packingCommands = new PackingCommandRouter(window, commands,
                () => ReadPackingContext(window),
                canvas => HostInstallation.Enabled && SelectionPacker.CanPackOn(canvas),
                (alignment, canvas, name) => PackSelection(alignment, canvas, "native-command: " + name),
                error => ShowError(error, notify: true));
            SaveReceipt("packing-shortcuts-installed", new
            {
                commands = commands.Select(pair => new
                {
                    command = pair.Key.Name, alignment = pair.Value.ToString(),
                    gestures = pair.Key.InputGestures.OfType<KeyGesture>().Select(g => new { key = g.Key.ToString(), modifiers = g.Modifiers.ToString() }).ToArray()
                }).ToArray(),
                defaultGap = gapText, scope = "Focused root, Container or Channel Group schematic; UCI/control panels retain native packing; text and other focus blocked."
            });
            shortcutStatus = "Schematic shortcuts: Ctrl+Shift+L / R / T / B use this gap, even after this window closes. Gap resets to 20 when Designer restarts.";
            try { editorShortcuts = EditorShortcuts.Install(window, (kind, detail) => { SaveReceipt(kind, detail); }, ex => ShowError(ex, notify: true)); }
            catch (Exception ex) { ShowError(ex); }
            StopBindingStartup();
            window.Closed += (_, _) => { packingCommands?.Dispose(); packingCommands = null; editorShortcuts?.Dispose(); editorShortcuts = null; };
        }
        catch (Exception error)
        {
            StopBindingStartup();
            packingCommands?.Dispose();
            packingCommands = null;
            shortcutStatus = "Packing shortcut replacement is unavailable; native shortcuts remain active. " + error.Message;
            ShowError(error);
        }
    }

    private void StopBindingStartup()
    {
        startupBindings?.Stop();
        startupBindings = null;
    }

    private static PackingCommandContext ReadPackingContext(Window window)
    {
        // GraphicCanvas.FocusedCanvas persists after focus leaves the schematic. Never use it here.
        var focused = Keyboard.FocusedElement as DependencyObject;
        for (var ancestor = focused; ancestor is not null;)
        {
            if (ancestor is MenuItem or ContextMenu)
            {
                // WPF menus have their own focus scope; use the owner's retained logical focus.
                focused = FocusManager.GetFocusedElement(window) as DependencyObject;
                break;
            }
            ancestor = ancestor is Visual ? VisualTreeHelper.GetParent(ancestor)
                : (ancestor as FrameworkContentElement)?.Parent;
        }
        var canvasType = SelectionMover.HostType("QSC.QSys.Graphic.GraphicCanvas");
        // Direct canvas focus deliberately excludes in-place labels and embedded text/control editors.
        if (focused is not UIElement element || !canvasType.IsInstanceOfType(focused)
            || !element.IsVisible || Window.GetWindow(focused) != window)
            return new(PackingFocus.Blocked);
        return SelectionMover.IsSchematicCanvas(focused)
            ? new(PackingFocus.Schematic, focused) : new(PackingFocus.OtherCanvas, focused);
    }

    private void PackSelection(PackAlignment alignment, object? canvas = null, string invocation = "button")
    {
        if (!HostInstallation.Enabled) throw new InvalidOperationException("Schematic Tools is not enabled for this host.");
        if (!int.TryParse(gapText, out var gap) || gap < 0 || gap > 10000)
            throw new ArgumentException("Enter a whole-number gap from 0 to 10000 in Schematic Tools, then try again.");
        var result = SelectionPacker.Pack(alignment, gap, SaveReceipt, canvas, invocation);
        if (reportText is not null) reportText.Text = result;
    }

    private void ShowReport()
    {
        if (!HostInstallation.Enabled)
        {
            MessageBox.Show("Schematic Tools is not enabled for this Designer build. Its installation must match the tested host version.", "Schematic Tools");
            return;
        }
        if (reportWindow is null)
        {
            reportText = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(8)
            };
            var refresh = new Button
            {
                Content = "Read current selection", Margin = new Thickness(8), Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            refresh.Click += (_, _) => RefreshReport();
            var move = new Button
            {
                Content = "Move selection down 20", Margin = new Thickness(8), Padding = new Thickness(10, 5, 10, 5)
            };
            move.Click += (_, _) =>
            {
                try
                {
                    if (!HostInstallation.Enabled) throw new InvalidOperationException("Schematic Tools is not enabled for this host.");
                    reportText!.Text = SelectionMover.MoveDown20(SaveReceipt);
                }
                catch (Exception error) { ShowError(error); }
            };
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(refresh);
            buttons.Children.Add(move);
            var colors = new Button { Content = "Named Signal colors...", Margin = new Thickness(8), Padding = new Thickness(10, 5, 10, 5) };
            colors.Click += (_, _) =>
            {
                try
                {
                    if (colorWindow is null)
                    {
                        colorWindow = new SignalColorWindow(reportWindow!, SaveReceipt);
                        colorWindow.Closed += (_, _) => colorWindow = null;
                        colorWindow.Show();
                    }
                    else colorWindow.Activate();
                }
                catch (Exception error) { ShowError(error); }
            };
            buttons.Children.Add(colors);
            var snapshots = new Button { Content = "Snapshot Inspector...", Margin = new Thickness(8), Padding = new Thickness(10, 5, 10, 5) };
            snapshots.Click += (_, _) => SnapshotTools.Show(); buttons.Children.Add(snapshots);
            var packing = new WrapPanel { Margin = new Thickness(8, 0, 8, 8) };
            packing.Children.Add(new TextBlock
            {
                Text = "Gap (schematic units):", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var gapInput = new TextBox { Text = gapText, Width = 60, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            gapInput.TextChanged += (_, _) => gapText = gapInput.Text;
            packing.Children.Add(gapInput);
            foreach (var alignment in Enum.GetValues<PackAlignment>())
            {
                var title = alignment == PackAlignment.VerticalCenter ? "Vertical Pack Center" : "Pack " + alignment;
                var pack = new Button { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
                pack.Click += (_, _) =>
                {
                    try
                    {
                        PackSelection(alignment);
                    }
                    catch (Exception error) { ShowError(error); }
                };
                packing.Children.Add(pack);
            }
            var hint = new TextBlock
            {
                Text = "Left / Center / Right stack top to bottom. Top / Vertical Center / Bottom pack left to right. All include exposed control pins. Input/output connectors pack with zero gap between them; other pairs use the selected gap. Undo with Ctrl+Z in Design mode; use F7 first when connected.\n" + shortcutStatus,
                Margin = new Thickness(8, 0, 8, 8), TextWrapping = TextWrapping.Wrap
            };
            var panel = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Top);
            DockPanel.SetDock(packing, Dock.Top);
            DockPanel.SetDock(hint, Dock.Top);
            panel.Children.Add(buttons);
            panel.Children.Add(packing);
            panel.Children.Add(hint);
            panel.Children.Add(reportText);
            reportWindow = new Window
            {
                Title = HostInstallation.IsLab ? "Schematic Tools - isolated Designer" : "Schematic Tools", Width = 920, Height = 540,
                Content = panel, Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            reportWindow.Closed += (_, _) => { reportWindow = null; reportText = null; };
            RefreshReport();
            reportWindow.Show();
        }
        else
        {
            RefreshReport();
            reportWindow.Activate();
        }
    }

    private void RefreshReport()
    {
        try
        {
            // Resolve the already loaded host; never load a second copy of its assembly.
            var selectionType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("QSC.QSys.Util.Selection", throwOnError: false))
                .FirstOrDefault(t => t is not null)
                ?? throw new MissingMemberException("The host selection type was not found.");
            var global = selectionType.GetField("Global", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) ?? throw new MissingFieldException(selectionType.FullName, "Global");
            if (global is not IEnumerable selection)
                throw new InvalidOperationException("Selection.Global is not enumerable.");
            var objects = new List<object>();
            foreach (var item in selection)
            {
                if (item is null) continue;
                if (objects.Count == 256) break;
                var position = ReadProperty(item, "Position");
                objects.Add(new
                {
                    type = item.GetType().FullName,
                    sessionIdentity = RuntimeHelpers.GetHashCode(item),
                    label = ReadProperty(item, "Label") as string,
                    position = position is Point p ? new { x = p.X, y = p.Y } : null
                });
            }
            var snapshot = new
            {
                selectionCount = selectionType.GetProperty("Count")?.GetValue(global),
                reportedCount = objects.Count, objects,
                hostState = SelectionMover.ReadHostState(),
                unlockedObjects = SelectionMover.ReadObjects(),
                packingFootprints = SelectionPacker.ReadFootprints()
            };
            var receipt = SaveReceipt("selection", snapshot);
            if (reportText is not null) reportText.Text = receipt;
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void ShowError(Exception error, bool notify = false)
    {
        if (reportText is not null) reportText.Text = error.ToString();
        try { SaveReceipt("error", new { message = error.ToString() }); }
        catch (Exception) { /* Keep errors visible if disk evidence is unavailable. */ }
        if (notify) MessageBox.Show(Application.Current.MainWindow, error.Message, "Schematic Tools", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static object? ReadProperty(object instance, string name)
    {
        // Only the specific report fields above are requested; no generic member execution.
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetIndexParameters().Length == 0 ? property.GetValue(instance) : null;
    }

    private static string SaveReceipt(string kind, object data)
    {
        Directory.CreateDirectory(OutputDirectory);
        var text = JsonSerializer.Serialize(new
        {
            kind, utc = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
            moduleVersion = typeof(SelectionProbeModule).Assembly.GetName().Version?.ToString(),
            applicationDirectory = AppContext.BaseDirectory, data
        }, JsonOptions);
        File.WriteAllText(Path.Combine(OutputDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{kind}.json"), text);
        return text;
    }
}
