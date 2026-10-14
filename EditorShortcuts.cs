using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Designer.Modules.SelectionProbe;

/// <summary>Additional window key bindings; no preview/global keyboard interception.</summary>
internal sealed class EditorShortcuts : IDisposable
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const ModifierKeys Modifiers = ModifierKeys.Control | ModifierKeys.Alt;
    private readonly Window window;
    private readonly List<InputBinding> inputBindings = new();
    private readonly List<CommandBinding> commandBindings = new();
    private readonly List<(MenuItem Menu, string Original)> hints = new();
    private readonly List<(ItemsControl Parent, object Item)> menuItems = new();
    private readonly Action<string, object> receipt;
    private readonly Action<Exception> error;

    private EditorShortcuts(Window window, Action<string, object> receipt, Action<Exception> error)
    { this.window = window; this.receipt = receipt; this.error = error; }

    private void Report(string kind, object detail)
    {
        try { receipt(kind, detail); }
        catch (Exception) { /* Diagnostics must not disable or interrupt editor actions. */ }
    }

    internal static EditorShortcuts Install(Window window, Action<string, object> receipt, Action<Exception> error)
    {
        var result = new EditorShortcuts(window, receipt, error);
        try { result.Attach(); return result; }
        catch { result.Dispose(); throw; }
    }

    private static object Field(object owner, string name) => owner.GetType().GetField(name, Instance)?.GetValue(owner)
        ?? throw new MissingFieldException(owner.GetType().FullName, name);
    private static object Property(object owner, string name) => owner.GetType().GetProperty(name, Instance)?.GetValue(owner)
        ?? throw new MissingMemberException(owner.GetType().FullName, name);
    private static void Call(object owner, string name) => (owner.GetType().GetMethod(name, Instance, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException(owner.GetType().FullName, name)).Invoke(owner, null);

    private void Attach()
    {
        var host = Field(window, "_DocHost");
        var panels = new[] { Field(host, "_toolWindow_Inventory"), Field(host, "_toolWindow_Properties"), Field(host, "_toolWindow_Library") };
        var programmer = (MenuItem)Field(window, "mnuScriptProgrammerMode");
        var appInfo = SelectionMover.HostType("QSC.QSys.App").GetField("AppInfo", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var programmerProperty = appInfo.GetType().GetProperty("ScriptProgrammerMode")
            ?? throw new MissingMemberException("AppInfo.ScriptProgrammerMode");
        var info = (RoutedCommand)SelectionMover.HostType("QSC.QSys.Commands.NamedComponentInfo")
            .GetField("Command", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var theme = (RoutedCommand)SelectionMover.HostType("QSC.QSys.Commands.ColorSchemeToggle")
            .GetField("Command", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        foreach (var native in new[] { info, theme })
            if (!window.CommandBindings.Cast<CommandBinding>().Any(binding => ReferenceEquals(binding.Command, native)))
                throw new InvalidOperationException($"Native {native.Name} command is not ready.");

        var keys = new[] { Key.D1, Key.D2, Key.D3, Key.I, Key.P, Key.D0, Key.D };
        var elements = VisualElements(window).ToArray();
        var gestures = elements.SelectMany(element => element.InputBindings.Cast<InputBinding>().Select(binding => binding.Gesture))
            .Concat(elements.SelectMany(element => element.CommandBindings.Cast<CommandBinding>())
                .Select(binding => binding.Command).OfType<RoutedCommand>().SelectMany(command => command.InputGestures.Cast<InputGesture>()))
            .OfType<KeyGesture>().ToArray();
        foreach (var key in keys)
            if (gestures.Any(gesture => gesture.Key == key && gesture.Modifiers == Modifiers))
                throw new InvalidOperationException($"Ctrl+Alt+{key} is already bound; editor shortcuts left unchanged.");

        var labels = new[] { "Toggle Design Elements", "Toggle Properties", "Toggle Schematic Elements", "Component Controls Info", "Script Programmer Mode", "Toggle All Sidebars", "Toggle Light/Dark Mode" };
        var names = new[] { "DesignElements", "Properties", "SchematicElements", "ComponentControlsInfo", "ScriptProgrammerMode", "AllSidebars", "LightDarkMode" };
        var menu = (Menu)Field(window, "mainMenu");
        var allMenus = Menus(menu).ToArray();
        var view = allMenus.FirstOrDefault(item => item.Items.OfType<MenuItem>().Any(child => child.Command is RoutedCommand command && command.OwnerType.FullName == "QSC.QSys.MainWindow" && command.Name.Contains("Zoom In", StringComparison.OrdinalIgnoreCase)))
            ?? menu.Items.OfType<MenuItem>().FirstOrDefault(item => (item.Header as string)?.Replace("_", "") == "View");

        for (int i = 0; i < keys.Length; i++)
        {
            int index = i;
            string text = "Ctrl+Alt+" + (keys[i] is >= Key.D0 and <= Key.D9 ? ((int)keys[i] - (int)Key.D0).ToString() : keys[i].ToString());
            var command = new RoutedUICommand(labels[i], "QsysTools" + names[i], typeof(EditorShortcuts));
            var binding = new CommandBinding(command, (_, e) =>
            {
                e.Handled = true;
                try
                {
                    if (index < 3) TogglePanel(panels[index]);
                    else if (index == 3) info.Execute(null, window);
                    else if (index == 4) programmerProperty.SetValue(appInfo, !(bool)programmerProperty.GetValue(appInfo)!);
                    else if (index == 5) ToggleAllPanels(panels);
                    else theme.Execute(null, window);
                    Report("editor-shortcut-executed", new { action = names[index] });
                }
                catch (Exception ex) { error(ex); }
            }, (_, e) =>
            {
                e.CanExecute = window.IsEnabled && (index < 3 ? (bool)Property(panels[index], "IsOpen")
                    : index == 3 ? info.CanExecute(null, window)
                    : index == 4 ? programmer.IsEnabled
                    : index == 5 ? SidebarPanels(panels).Any() : theme.CanExecute(null, window));
                e.Handled = true;
            });
            var input = new KeyBinding(command, keys[i], Modifiers) { CommandTarget = window };
            window.CommandBindings.Add(binding); commandBindings.Add(binding);
            window.InputBindings.Add(input); inputBindings.Add(input);
            if ((index < 3 || index >= 5) && view is not null)
            {
                var entry = new MenuItem { Header = labels[i], Command = command, CommandTarget = window, InputGestureText = text };
                view.Items.Add(entry); menuItems.Add((view, entry));
            }
            else
            {
                var entry = index == 4 ? programmer : index == 3 ? allMenus.FirstOrDefault(item => ReferenceEquals(item.Command, info)) : null;
                if (entry is not null) { hints.Add((entry, entry.InputGestureText)); entry.InputGestureText = text; }
            }
        }
        Report("editor-shortcuts-installed", new { shortcuts = labels.Select((label, i) => new { label, key = keys[i].ToString(), modifiers = Modifiers.ToString() }).ToArray(),
            checkedNativeGestures = gestures.Length, scope = "Main Designer window; normal routed input, no global/preview key interception" });
    }

    private static void TogglePanel(object panel)
    {
        bool expand = Property(panel, "State").ToString() == "AutoHide";
        // Match QDS's own arrow button, including tabbed panels sharing a container.
        Call(PanelTarget(panel), expand ? "Dock" : "AutoHide");
    }

    private static object PanelTarget(object panel) => panel is FrameworkElement { Parent: { } parent }
        && parent.GetType().FullName == "ActiproSoftware.Windows.Controls.Docking.ToolWindowContainer" ? parent : panel;

    private static IEnumerable<object> SidebarPanels(IEnumerable<object> panels) => panels.Where(panel =>
        (bool)Property(panel, "IsOpen") && Property(panel, "State").ToString() is "Docked" or "AutoHide");

    private static void ToggleAllPanels(IEnumerable<object> panels)
    {
        // Choose one direction from current state; in a mixed layout, collapse.
        // Resolve and deduplicate containers before moving any of their tabs.
        var sidebars = SidebarPanels(panels).ToArray();
        bool expand = sidebars.All(panel => Property(panel, "State").ToString() == "AutoHide");
        var targets = sidebars.Where(panel => (Property(panel, "State").ToString() == "AutoHide") == expand)
            .Select(PanelTarget).Distinct(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var target in targets) Call(target, expand ? "Dock" : "AutoHide");
    }

    internal static void BindControlsInfo(UIElement owner)
    {
        if (Application.Current.MainWindow is not { } main) return;
        // The native info tool normally floats in its own Window, outside the
        // main window's input route. Forward our editor gestures there too.
        foreach (var binding in main.InputBindings.OfType<KeyBinding>().Where(binding => binding.Command is RoutedCommand command
            && command.OwnerType == typeof(EditorShortcuts)))
        {
            if (owner.InputBindings.OfType<KeyBinding>().Any(existing => existing.Key == binding.Key && existing.Modifiers == binding.Modifiers)) continue;
            owner.InputBindings.Add(new KeyBinding(binding.Command, binding.Key, binding.Modifiers) { CommandTarget = main });
        }
    }

    private static IEnumerable<UIElement> VisualElements(DependencyObject root)
    {
        if (root is UIElement element) yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in VisualElements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static IEnumerable<MenuItem> Menus(ItemsControl root)
    {
        foreach (var item in root.Items.OfType<MenuItem>())
        { yield return item; foreach (var child in Menus(item)) yield return child; }
    }
    public void Dispose()
    {
        foreach (var input in inputBindings) window.InputBindings.Remove(input);
        foreach (var command in commandBindings) window.CommandBindings.Remove(command);
        foreach (var (menu, text) in hints) menu.InputGestureText = text;
        foreach (var (parent, item) in menuItems) parent.Items.Remove(item);
        inputBindings.Clear(); commandBindings.Clear(); hints.Clear(); menuItems.Clear();
    }
}
