using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HarmonyLib;

namespace Designer.Modules.SelectionProbe;

internal static class SnapshotTools
{
    private const string PatchId = "qsys.schematic-tools.snapshot-membership";
    private static readonly Brush Fill = new SolidColorBrush(Color.FromRgb(0xf3, 0x9e, 0x77));
    private static readonly Pen Stroke = new(new SolidColorBrush(Color.FromRgb(0xff, 0x8c, 0x00)), 0.5);
    private static readonly ConditionalWeakTable<object, object> seen = new();
    private static readonly List<WeakReference<object>> graphics = new();
    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached("SnapshotActions", typeof(bool), typeof(SnapshotTools), new PropertyMetadata(false));
    private static readonly SnapshotModel model = new(QueueRefresh);
    private static Dictionary<SnapshotKey, SnapshotRow[]> index = new();
    private static DispatcherTimer? documentWatcher;
    private static object? document;
    private static Action<string, object>? receipt;
    private static bool pending, installed, sidebarScanned;
    private static SnapshotInspector? inspector;
    private static readonly HashSet<string> reported = new();
    internal static SnapshotModel Model => model;
    internal static event Action? Changed;

    internal static void Install(Action<string, object> report)
    {
        if (installed) return;
        receipt = report;
        string hostPath = SnapshotHost.Type("Graphic.Controls.ControlBase").Assembly.Location;
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hostPath))).ToLowerInvariant();
        if (hash != HostInstallation.DesignerHash) throw new InvalidOperationException("Snapshot indicators require the inspected Designer 10.5 build.");
        var control = SnapshotHost.Type("Graphic.Controls.ControlBase");
        var draw = control.GetMethod("DrawIsNamed", SnapshotHost.Flags)!;
        var sidebarConstructor = SnapshotHost.Type("windows.DesignElementViewers.SnapshotViewer").GetConstructor(Type.EmptyTypes)!;
        var hover = control.Assembly.GetTypes().Where(t => control.IsAssignableFrom(t))
            .Select(t => t.GetProperty("HoverText", SnapshotHost.Flags | BindingFlags.DeclaredOnly)?.GetMethod).OfType<MethodInfo>().ToArray();
        foreach (var target in hover.Cast<MethodBase>().Append(draw).Append(sidebarConstructor))
            if (Harmony.GetPatchInfo(target)?.Owners.Any() is true) throw new InvalidOperationException("Snapshot drawing/hover hook is already modified: " + target);
        Fill.Freeze(); Stroke.Freeze();
        var harmony = new Harmony(PatchId);
        try
        {
            harmony.Patch(draw, postfix: new HarmonyMethod(typeof(SnapshotTools), nameof(Draw)));
            foreach (var target in hover) harmony.Patch(target, postfix: new HarmonyMethod(typeof(SnapshotTools), nameof(Hover)));
            harmony.Patch(sidebarConstructor, postfix: new HarmonyMethod(typeof(SnapshotTools), nameof(SidebarConstructed)));
            EventManager.RegisterClassHandler(SnapshotHost.Type("windows.DesignElementViewers.SnapshotViewer"), FrameworkElement.LoadedEvent, new RoutedEventHandler(SidebarLoaded), true);
            documentWatcher = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
            documentWatcher.Tick += (_, _) =>
            {
                var current = SnapshotHost.Document;
                if (!ReferenceEquals(document, current)) { document = current; QueueRefresh(); }
                if (!sidebarScanned && Application.Current.MainWindow is { IsLoaded: true } main)
                {
                    ScanSidebar(main); sidebarScanned = true;
                }
            };
            documentWatcher.Start(); installed = true; QueueRefresh();
            Report("snapshot-tools-installed", new { fill = "#f39e77", stroke = "#ff8c00", radius = 3, strokeWidth = 0.5, globalExcluded = true, hoverHooks = hover.Length });
        }
        catch { harmony.UnpatchAll(PatchId); throw; }
    }

    private static void Report(string kind, object detail) { try { receipt?.Invoke(kind, detail); } catch { } }
    private static void Error(Exception error)
    {
        string message = error.GetBaseException().Message;
        if (reported.Add(message)) Report("snapshot-tools-error", new { error = error.ToString() });
    }
    internal static void QueueRefresh()
    {
        if (pending || Application.Current is null) return;
        pending = true;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(Refresh));
    }
    internal static void Refresh()
    {
        pending = false;
        try
        {
            document = SnapshotHost.Document; model.Refresh(document);
            index = model.Rows.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToArray());
        }
        catch (Exception error) { model.Dispose(); index.Clear(); Error(error); }
        foreach (var weak in graphics.ToArray())
            if (weak.TryGetTarget(out var graphic))
            {
                try { SnapshotHost.Call(graphic, "UpdateControlVisual"); } catch (Exception error) { Error(error); }
            }
        graphics.RemoveAll(w => !w.TryGetTarget(out _)); Changed?.Invoke();
    }
    internal static SnapshotRow[] ForStyle(object? style)
    {
        var value = SnapshotHost.Get(style, "Value"); var id = SnapshotHost.Get(value, "Id");
        return id is not null && index.TryGetValue(new(id, SnapshotHost.Text(value, "Name")), out var rows) ? rows : Array.Empty<SnapshotRow>();
    }
    private static void Draw(object __instance, DrawingContext __0)
    {
        try
        {
            if (!seen.TryGetValue(__instance, out _)) { seen.Add(__instance, new object()); graphics.Add(new(__instance)); }
            var style = SnapshotHost.Get(__instance, "Style");
            if (ForStyle(style).Length == 0) return;
            var size = (Size)SnapshotHost.Get(__instance, "Size")!;
            bool named = SnapshotHost.Get(__instance, "IsNamed") is true && SnapshotHost.Get(SnapshotHost.Type("Tools.Set"), "IsEditToolActive") is true;
            // Keep the native Named Control dot at its original location. This is only
            // decoration: no model size, control value, input target or exported style changes.
            __0.DrawEllipse(Fill, Stroke, new Point(size.Width - (named ? 11 : 3), size.Height - 3), 3, 3);
        }
        catch (Exception error) { Error(error); }
    }
    private static void Hover(object __instance, ref string __result)
    {
        try
        {
            var rows = ForStyle(SnapshotHost.Get(__instance, "Style"));
            if (rows.Length == 0) return;
            string suffix = "Snapshot banks: " + string.Join("; ", rows.Select(r => r.Bank + " (" + r.IncludedBy.ToLowerInvariant() + ")"));
            if (!(__result ?? "").Contains(suffix, StringComparison.Ordinal)) __result = string.IsNullOrEmpty(__result) ? suffix : __result + "\n" + suffix;
        }
        catch (Exception error) { Error(error); }
    }
    internal static void Show(object? bank = null)
    {
        try
        {
            Refresh();
            if (inspector is null) { inspector = new SnapshotInspector(); inspector.Closed += (_, _) => inspector = null; inspector.Show(); }
            inspector.SelectBank(bank); inspector.Activate();
        }
        catch (Exception error) { Error(error); MessageBox.Show(error.GetBaseException().Message, "Snapshot Inspector"); }
    }
    internal static bool Navigate(object component, string? control)
    {
        // A floating inspector can outlive its original document. Never route an old
        // row into a newly opened design that happens to reuse the same identifiers.
        if (!ReferenceEquals(SnapshotHost.Call(SnapshotHost.Type("Design.ComponentCache"), "GetComponent", SnapshotHost.Get(component, "Id")), component)) return false;
        if (control is null)
        {
            SnapshotHost.Call(SnapshotHost.Type("Util.Find"), "FindAndSelect", component); return true;
        }
        var choices = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(SnapshotHost.Type("windows.Find.FindChoice")))!;
        if (SnapshotHost.Call(SnapshotHost.Type("windows.Find.ControlFindHelper"), "AddFind", choices, SnapshotHost.Get(component, "Id"), control) is not true || choices.Count == 0) return false;
        SnapshotHost.Call(choices[0]!, "Find", new object?[] { null });
        Report("snapshot-control-navigated", new { control, component = SnapshotHost.Text(component, "Label") }); return true;
    }
    internal static bool NavigateEntry(object entry)
    {
        if (SnapshotHost.Type("Design.Snapshot.Component").IsInstanceOfType(entry))
            return SnapshotHost.Get(entry, "DesignComponent") is { } component && Navigate(component, null);
        foreach (var bank in SnapshotHost.Items(SnapshotHost.Get(SnapshotHost.Document, "Snapshots")))
            foreach (var member in SnapshotHost.Items(SnapshotHost.Get(bank, "Components")))
                if (SnapshotHost.Items(SnapshotHost.Get(member, "Controls")).Any(c => ReferenceEquals(c, entry)))
                    return SnapshotHost.Get(member, "DesignComponent") is { } source && Navigate(source, SnapshotHost.Text(entry, "CodeName"));
        return false;
    }
    private static object? Entry(TreeView tree, DependencyObject? source)
    {
        for (var node = source; node is not null && node != tree; node = node is Visual ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent)
        {
            if (node is ToggleButton) return null;
            if (node is TreeViewItem item) return item.DataContext;
        }
        return null;
    }
    private static bool IsEntry(object? entry) => entry is not null && (SnapshotHost.Type("Design.Snapshot.Control").IsInstanceOfType(entry)
        || SnapshotHost.Type("Design.Snapshot.Component").IsInstanceOfType(entry));
    private static void SidebarConstructed(object __instance) => SidebarLoaded(__instance, new RoutedEventArgs());
    private static void ScanSidebar(DependencyObject root)
    {
        if (SnapshotHost.Type("windows.DesignElementViewers.SnapshotViewer").IsInstanceOfType(root)) SidebarLoaded(root, new RoutedEventArgs());
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ScanSidebar(VisualTreeHelper.GetChild(root, i));
    }
    private static void SidebarLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var viewer = SnapshotHost.Get(sender, "_Viewer");
            if (SnapshotHost.Get(viewer, "_TreeView") is not TreeView tree || (bool)tree.GetValue(AttachedProperty)) return;
            tree.SetValue(AttachedProperty, true);
            tree.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, args) =>
            {
                var entry = Entry(tree, args.OriginalSource as DependencyObject);
                if (args.ClickCount == 2) Report("snapshot-sidebar-double-click", new { type = entry?.GetType().FullName, source = args.OriginalSource?.GetType().FullName, button = args.ChangedButton.ToString() });
                if (args.ChangedButton != MouseButton.Left || args.ClickCount != 2 || !IsEntry(entry)) return;
                try { args.Handled = NavigateEntry(entry!); } catch (Exception error) { Error(error); }
            }), true);
            tree.ContextMenuOpening += (_, args) =>
            {
                var entry = Entry(tree, args.OriginalSource as DependencyObject) ?? tree.SelectedItem;
                var menu = tree.ContextMenu ??= new ContextMenu();
                foreach (var old in menu.Items.OfType<MenuItem>().Where(m => Equals(m.Tag, PatchId)).ToArray()) menu.Items.Remove(old);
                if (!IsEntry(entry)) return;
                var go = new MenuItem { Header = "Go to " + (SnapshotHost.Type("Design.Snapshot.Control").IsInstanceOfType(entry) ? "control" : "component"), Tag = PatchId };
                go.Click += (_, _) => { try { NavigateEntry(entry!); } catch (Exception error) { Error(error); } };
                menu.Items.Insert(0, go);
                var inspect = new MenuItem { Header = "Snapshot Inspector...", Tag = PatchId }; inspect.Click += (_, _) => Show(); menu.Items.Insert(1, inspect);
            };
            // A non-null menu allows the first ContextMenuOpening event to display it.
            tree.ContextMenu ??= new ContextMenu();
            Report("snapshot-sidebar-attached", new { type = tree.GetType().FullName });
        }
        catch (Exception error) { Error(error); }
    }
}
