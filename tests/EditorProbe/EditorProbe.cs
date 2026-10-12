using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.EditorLabProbe;

public sealed class EditorProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string OutputDirectory);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public string Description => "Isolated native editor shortcut/filter check";
    public string MenuTitle => "Editor lab probe";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
    public EditorProbe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ".editor-lab-probe.json");
        if (!File.Exists(path) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(config.OutputDirectory, "result.json"))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += async (_, _) => await Run(); timer.Start();
    }
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Instance)!.GetValue(owner)!;
    private static object Property(object owner, string name) => owner.GetType().GetProperty(name, Instance)!.GetValue(owner)!;
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Task Idle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void SendKey(UIElement target, System.Windows.Input.Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private void Render(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 2), (int)Math.Ceiling(element.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(config!.OutputDirectory, name + ".png")); encoder.Save(file);
    }
    private async Task Run()
    {
        var checks = new List<object>(); Window? testWindow = null;
        try
        {
            if (++attempts < 12 || Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true } main) return;
            var bindings = main.InputBindings.OfType<KeyBinding>().Where(b => b.Command is RoutedCommand c && c.Name.StartsWith("QsysTools")).ToArray();
            if (bindings.Length != 5 && attempts < 30) return;
            timer!.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            Require(bindings.Length == 5, "Editor shortcuts did not install; inspect startup receipts.");
            var host = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Q-Sys Designer");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(host.Location))).ToLowerInvariant()
                == "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60", "Unexpected host.");
            Require(main.Title.StartsWith("untitled", StringComparison.OrdinalIgnoreCase), "Requires blank disposable lab session.");
            var selection = host.GetType("QSC.QSys.Util.Selection")!.GetField("Global")!.GetValue(null)!;
            selection.GetType().GetMethod("Clear", Type.EmptyTypes)!.Invoke(selection, null);
            RoutedCommand Command(string name) => (RoutedCommand)bindings.Single(b => ((RoutedCommand)b.Command).Name == "QsysTools" + name).Command;
            void Execute(string name) { var command = Command(name); Require(command.CanExecute(null, main), "Command disabled: " + name); command.Execute(null, main); }
            var nativeGestures = main.CommandBindings.Cast<CommandBinding>().Select(b => b.Command).OfType<RoutedCommand>()
                .Where(c => !c.Name.StartsWith("QsysTools")).SelectMany(c => c.InputGestures.OfType<KeyGesture>().Select(g => new { command = c.Name, key = g.Key.ToString(), modifiers = g.Modifiers.ToString() })).ToArray();
            File.WriteAllText(Path.Combine(config.OutputDirectory, "native-gestures.json"), JsonSerializer.Serialize(nativeGestures, new JsonSerializerOptions { WriteIndented = true }));
            checks.Add(new { registered = bindings.Select(b => new { name = ((RoutedCommand)b.Command).Name, key = b.Key.ToString(), modifiers = b.Modifiers.ToString() }).ToArray(), nativeGestures = nativeGestures.Length });
            int inputCount = main.InputBindings.Count, commandCount = main.CommandBindings.Count;
            var extension = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe");
            var install = extension.GetType("Designer.Modules.SelectionProbe.EditorShortcuts")!.GetMethod("Install", Static)!;
            bool rejected = false;
            try { install.Invoke(null, new object[] { main, new Action<string, object>((_, _) => { }), new Action<Exception>(_ => { }) }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException conflict && conflict.Message.Contains("already bound")) { rejected = true; }
            Require(rejected && main.InputBindings.Count == inputCount && main.CommandBindings.Count == commandCount, "Conflict detection changed existing bindings.");
            checks.Add(new { duplicateGestureRejectedWithoutMutation = true });

            var document = Field(main, "_DocHost");
            var names = new[] { "DesignElements", "Properties", "SchematicElements" };
            var fields = new[] { "_toolWindow_Inventory", "_toolWindow_Properties", "_toolWindow_Library" };
            for (int i = 0; i < names.Length; i++)
            {
                var panel = Field(document, fields[i]); string before = Property(panel, "State").ToString()!;
                Require(before is "Docked" or "AutoHide", "Unexpected lab sidebar state: " + before);
                var size = Property(panel, "ContainerDockedSize");
                try
                {
                    Execute(names[i]); await Idle(); string middle = Property(panel, "State").ToString()!;
                    Require(middle == (before == "Docked" ? "AutoHide" : "Docked"), "Sidebar did not toggle: " + names[i]);
                    Execute(names[i]); await Idle(); Require(Property(panel, "State").ToString() == before, "Sidebar did not restore: " + names[i]);
                    checks.Add(new { sidebar = names[i], before, middle, after = Property(panel, "State").ToString(), sizeBefore = size.ToString(), sizeAfter = Property(panel, "ContainerDockedSize").ToString() });
                }
                finally { if (Property(panel, "State").ToString() != before) { Execute(names[i]); await Idle(); } }
            }
            var appInfo = host.GetType("QSC.QSys.App")!.GetField("AppInfo", Static)!.GetValue(null)!;
            var mode = appInfo.GetType().GetProperty("ScriptProgrammerMode")!; bool initial = (bool)mode.GetValue(appInfo)!;
            var menu = (MenuItem)Field(main, "mnuScriptProgrammerMode");
            try
            {
                Execute("ScriptProgrammerMode"); await Idle(); Require((bool)mode.GetValue(appInfo)! != initial && menu.IsChecked != initial, "Programmer mode/menu did not toggle together.");
                Execute("ScriptProgrammerMode"); await Idle(); Require((bool)mode.GetValue(appInfo)! == initial && menu.IsChecked == initial, "Programmer mode did not restore.");
                checks.Add(new { programmerModeNativeBinding = true });
            }
            finally { mode.SetValue(appInfo, initial); }

            var commandType = host.GetType("QSC.QSys.Commands.NamedComponentInfo")!;
            Execute("ComponentControlsInfo"); await Idle();
            var infoWindow = commandType.GetField("_ToolWindow", Static)!.GetValue(null)!;
            Require((bool)Property(infoWindow, "IsOpen"), "Native Controls Info did not open.");
            var nativeInfo = (UserControl)commandType.GetField("_ComponentInfo", Static)!.GetValue(null)!;
            if (Window.GetWindow(nativeInfo) is { } floating) { floating.Left = -32000; floating.Top = -32000; }
            Require(Tree(nativeInfo).OfType<TextBox>().Any(t => t.Name == "ComponentInfoSearchText"), "Native command window has no search.");
            Require(nativeInfo.InputBindings.OfType<KeyBinding>().Count() == 5, "Floating info did not retain the five editor shortcuts.");
            var localInfoBinding = nativeInfo.InputBindings.OfType<KeyBinding>().Single(binding => ((RoutedCommand)binding.Command).Name == "QsysToolsComponentControlsInfo");
            ((RoutedCommand)localInfoBinding.Command).Execute(null, localInfoBinding.CommandTarget);
            await Idle(); Require(!(bool)Property(infoWindow, "IsOpen"), "Native Controls Info did not close from its local shortcut route.");
            checks.Add(new { nativeControlsInfoToggleAndSearch = true, floatingInfoShortcutRoute = true });

            var info = (UserControl)Activator.CreateInstance(host.GetType("QSC.QSys.windows.ComponentInfo")!)!;
            var source = (TextBox)Field(info, "txt");
            testWindow = new Window { Content = info, Width = 650, Height = 380, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            testWindow.Show(); await Idle();
            var search = Tree(info).OfType<TextBox>().Single(t => t.Name == "ComponentInfoSearchText");
            var display = Tree(info).OfType<TextBox>().Single(t => t.Name == "ComponentInfoFilteredText");
            string[] rows = { "input.1.gain ( Input 1 Gain ) ( Float )", "input.2.gain ( Input 2 Gain ) ( Float )", "input.1.mute ( Input 1 Mute ) ( Boolean )", "meter.1 ( Signal Meter ) ( Float )" };
            string full = string.Join("\r\n", rows); source.Text = full; search.Focus(); await Idle();
            Require(display.Text == full && source.Text == full, "Initial display differs from native source.");
            search.Text = "GAIN 1"; Require(display.Text == rows[0] && source.Text == full, "Name/label fragments did not filter safely."); Render(info, "filtered");
            search.Text = "boolean"; Require(display.Text == rows[2], "Type search failed.");
            search.Text = "not-present"; Require(display.Text == "" && source.Text == full, "No-match search damaged native source.");
            search.Text = "GAIN 1"; source.Text = rows[0]; search.Clear();
            Require(display.Text == rows[0], "New component matching the old filtered text restored stale rows.");
            source.Text = full; search.Text = "mute"; source.Text = "gain ( Gain ) ( Float )";
            Require(display.Text == "", "Selection refresh failed to apply the retained query.");
            search.Clear(); Require(display.Text == source.Text, "Clear lost the new component list.");
            source.Text = full; search.Text = "mute"; SendKey(search, System.Windows.Input.Key.Escape);
            Require(search.Text == "" && display.Text == full, "Escape did not restore all rows.");
            display.Focus();
            var composition = new TextComposition(InputManager.Current, display, "gain");
            display.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Require(search.Text == "gain" && search.IsKeyboardFocusWithin && display.Text == string.Join("\r\n", rows.Take(2)), "Typing from list did not start filtering.");
            source.Text = "Please select a single component"; Require(display.Text == source.Text, "Host selection guidance was hidden.");
            source.Text = full; search.Clear(); display.SelectAll(); Require(display.SelectedText == full && display.IsReadOnly && source.Text == full, "Selectable read-only copy surface changed.");
            search.Text = "mute"; Tree(info).OfType<Button>().Single(b => b.Content as string == "Clear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(search.Text == "" && display.Text == full, "Clear button failed.");
            string large = string.Join("\r\n", Enumerable.Range(1, 2000).Select(i => $"input.{i}.gain ( Input {i} Gain ) ( Float )"));
            source.Text = large; var stopwatch = System.Diagnostics.Stopwatch.StartNew(); search.Text = "input.1999.gain"; stopwatch.Stop();
            Require(display.Text == "input.1999.gain ( Input 1999 Gain ) ( Float )" && source.Text == large, "Large native list filtering failed.");
            checks.Add(new { largeListRows = 2000, filterMilliseconds = stopwatch.Elapsed.TotalMilliseconds });
            checks.Add(new { sourcePreserved = true, nameLabelTypeSearch = true, sourceReplacement = true, retainedQueryOnSelection = true,
                staleRowsRegression = true, typingFromList = true, clearEscape = true, selectableText = true, localizedPromptPreserved = true });
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "complete", pid = Environment.ProcessId, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            timer?.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "error", error = ex.ToString(), checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { testWindow?.Close(); }
    }
}
