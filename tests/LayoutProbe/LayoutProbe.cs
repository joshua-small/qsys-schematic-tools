using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.LayoutLabProbe;

// Never install this acceptance driver in regular Designer. Config is local test input.
public sealed class LayoutProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string Input, string InputHash, string Receipt,
        string? SaveAs, bool CreateAndPack, bool GeometryEnabled, bool TestNested = false);
    private readonly Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private Assembly host = null!;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    public string Description => "Isolated Layout packing and connector persistence acceptance";
    public string MenuTitle => "Layout lab probe (automatic)";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
    private Type T(string n) => host.GetType("QSC.QSys." + n, true)!;
    private static object P(object o, string n) => o.GetType().GetProperty(n, Instance)!.GetValue(o)!;
    private static void Set(object o, string n, object v) => o.GetType().GetProperty(n, Instance)!.SetValue(o, v);
    private static object? Call(object o, string n, params object?[] args) =>
        o.GetType().GetMethods(Instance).Single(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(o, args);
    private static object[] Items(object list) => ((IEnumerable)list).Cast<object>().ToArray();
    private static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public LayoutProbe()
    {
        var file = Path.Combine(AppContext.BaseDirectory, ".layout-lab-probe.json");
        if (!File.Exists(file) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(file))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || !Environment.GetCommandLineArgs().Any(x => x.Equals(config.Input, StringComparison.OrdinalIgnoreCase))
            || Hash(config.Input) != config.InputHash || File.Exists(config.Receipt)) return;
        if (config.SaveAs is not null && (File.Exists(config.SaveAs) || Path.GetDirectoryName(config.SaveAs) != Path.GetDirectoryName(config.Input))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => await Run();
        timer.Start();
    }

    private object[] Graphics()
    {
        var map = T("Graphic.Maps").GetField("Graphics", Static)!.GetValue(null)!;
        var dictionary = map.GetType().GetField("_map", Instance)!.GetValue(map)!;
        return Items(P(dictionary, "Values"));
    }

    private object[] Descendants(object canvas) => Items(P(canvas, "AllChildren"))
        .SelectMany(m => new[] { m }.Concat(m.GetType().GetProperty("ContainedCanvases", Instance) is { } p
            ? Items(p.GetValue(m)!).SelectMany(Descendants) : [])).ToArray();

    private object Snapshot(object m) => new { type = m.GetType().FullName,
        label = P(m, "Label"), position = P(m, "Position").ToString(), size = P(m, "Size").ToString(),
        width = ((Size)P(m, "Size")).Width, height = ((Size)P(m, "Size")).Height, attached = P(m, "IsAttached") };

    private async Task Run()
    {
        var checks = new List<object>();
        try
        {
            if (++attempts > 60) throw new InvalidOperationException("Lab document did not become ready.");
            if (Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true } window
                || !window.Title.StartsWith(Path.GetFileNameWithoutExtension(config!.Input), StringComparison.Ordinal) || attempts < 10) return;
            host = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Q-Sys Designer");
            var graphics = Graphics();
            var componentGraphic = graphics.FirstOrDefault(T("Graphic.Component").IsInstanceOfType);
            if (componentGraphic is null) return;
            timer!.Stop();
            Require(Hash(host.Location) == "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60", "Unexpected vendor DLL.");
            var geometry = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "Designer.Modules.AutomaticGeometry");
            Require((geometry is not null) == config.GeometryEnabled, "Geometry module presence mismatch.");
            if (geometry is not null) Require(geometry.GetType("Designer.Modules.AutomaticGeometry.GeometryHooks")!
                .GetField("Host", Static)!.GetValue(null) is not null, "Geometry hooks not ready.");
            var connection = T("ControlConnection.Manager");
            Require(!(bool)P(connection.GetProperty("Status", Static)!.GetValue(null)!, "IsConnected")
                && !(bool)connection.GetProperty("IsEmulator", Static)!.GetValue(null)!, "Requires disconnected Design mode.");
            var manager = T("App").GetField("Manager", Static)!.GetValue(null)!;
            var canvas = P(componentGraphic, "GraphicCanvas");
            var root = P(canvas, "Root");
            Require(Items(P(P(manager, "Document"), "Pages")).Contains(root), "Requires root schematic.");

            if (config.TestNested)
            {
                await TestNested(manager, root, canvas, checks);
                File.WriteAllText(config.Receipt, JsonSerializer.Serialize(new { status = "complete", pid = Environment.ProcessId,
                    vendorHash = Hash(host.Location), checks, originalUnchanged = Hash(config.Input) == config.InputHash },
                    new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            if (config.CreateAndPack)
            {
                Require(config.GeometryEnabled, "Fixture creation requires geometry mod.");
                var operation = Call(manager, "StartOperation")!;
                var created = new List<object>();
                string[] creators = ["ContainerCreate", "ChannelGroupCreate", "PopupCreate", "SignalSnakeCreate", "SignalSnakeCreate"];
                for (int i = 0; i < creators.Length; i++)
                {
                    var creator = Activator.CreateInstance(T("windows." + creators[i]), nonPublic: true)!;
                    if (creators[i] == "SignalSnakeCreate") Set(creator, "IsInput", i == 3);
                    var model = Call(creator, "Create", new Point(150 + i * 140, 500 + i * 100))!;
                    if (i == 0)
                    {
                        Set(model, "InputCount", 3u); Set(model, "OutputCount", 3u);
                        T("Design.ContainerHelper").GetMethod("UpdateContainer", Static)!.Invoke(null, [model]);
                    }
                    if (i == 1)
                    {
                        Set(model, "ControlInputCount", 1u); Set(model, "ControlOutputCount", 1u);
                        T("Design.ChannelGroupHelper").GetMethod("UpdateChannelGroup", Static)!.Invoke(null, [model]);
                    }
                    Call(model, "Attach"); Call(P(root, "CurrentLayer"), "AddChild", model); Call(model, "AllocateResources", root);
                    created.Add(model);
                }
                Call(manager, "EndOperation", operation);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                var packAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe");
                var packer = packAssembly.GetType("Designer.Modules.SelectionProbe.SelectionPacker")!;
                var alignmentType = packAssembly.GetType("Designer.Modules.SelectionProbe.PackAlignment")!;
                var selection = T("Util.Selection").GetField("Global", Static)!.GetValue(null)!;
                // Explicit array avoids a compiler-generated IEnumerable implementation;
                // Designer scans every class in module assemblies for DI registration.
                var chosen = created.Concat(new[] { P(componentGraphic, "obj") }).ToArray();
                checks.Add(new { kind = "chosen", types = chosen.Select(m => m.GetType().FullName).ToArray() });
                var originals = chosen.Select(m => (Point)P(m, "Position")).ToArray();
                var labels = chosen.Select(m => P(m, "Label")).ToArray();
                var nested = Descendants(root).Where(T("Design.ContainerPin").IsInstanceOfType).ToArray();
                var nestedPositions = nested.Select(m => P(m, "Position").ToString()).ToArray();
                var selectedGraphics = chosen.Select(m => Graphics().Single(g => T("Graphic.Object").IsInstanceOfType(g) && ReferenceEquals(P(g, "obj"), m))).ToArray();
                void Select(IEnumerable<object> selected)
                {
                    Call(selection, "Clear");
                    var add = selection.GetType().GetMethod("Add", Instance, null, [T("Util.ISelectable"), typeof(bool)], null)!;
                    foreach (var g in selected) add.Invoke(selection, [g, false]);
                }
                Func<string, object, string> receipt = (kind, data) => { checks.Add(new { kind, data }); return kind; };
                foreach (var name in Enum.GetNames(alignmentType))
                {
                    Select(selectedGraphics);
                    var sizes = selectedGraphics.Select(g => (Size)P(g, "Size")).ToArray();
                    packer.GetMethod("Pack", Static)!.Invoke(null, [Enum.Parse(alignmentType, name), 10, receipt, canvas, "lab-mixed"]);
                    var after = chosen.Select(m => (Point)P(m, "Position")).ToArray();
                    bool row = name is "Top" or "VerticalCenter" or "Bottom";
                    var order = Enumerable.Range(0, chosen.Length).OrderBy(i => row ? after[i].X : after[i].Y).ToArray();
                    for (int j = 1; j < order.Length; j++)
                    {
                        int a = order[j - 1], b = order[j];
                        var clearance = row ? after[b].X - after[a].X - sizes[a].Width : after[b].Y - after[a].Y - sizes[a].Height;
                        Require(clearance >= 10 && clearance < 11, name + " clearance incorrect.");
                    }
                    Require(chosen.Select(m => P(m, "Label")).SequenceEqual(labels), "Packing changed labels.");
                    Require(selectedGraphics.Select(g => (Size)P(g, "Size")).SequenceEqual(sizes), "Packing changed sizes.");
                    Require(nested.Select(m => P(m, "Position").ToString()).SequenceEqual(nestedPositions), "Packing changed container contents.");
                    Call(manager, "Undo");
                    Require(chosen.Select(m => (Point)P(m, "Position")).SequenceEqual(originals), "One undo did not restore selection.");
                    Call(manager, "Redo");
                    Require(chosen.Select(m => (Point)P(m, "Position")).SequenceEqual(after), "Redo failed.");
                    Call(manager, "Undo");
                    checks.Add(new { name, gap = 10, mixedTypes = chosen.Select(m => m.GetType().FullName), undoRedo = true, unchangedLabelsSizesContents = true });
                }
                Select(selectedGraphics.Take(5));
                packer.GetMethod("Pack", Static)!.Invoke(null, [Enum.Parse(alignmentType, "Left"), 0, receipt, null, "lab-layout-only-button"]);
                Call(manager, "Undo");
                Require(chosen.Select(m => (Point)P(m, "Position")).SequenceEqual(originals), "Layout-only undo failed.");
                Call(selection, "Clear");
            }

            var models = Descendants(root);
            var connectors = models.Where(T("Design.ContainerPin").IsInstanceOfType).ToArray();
            Require(connectors.Length == 11, "Expected six Container and five Channel Group internal connectors.");
            Require(connectors.All(m => ((Size)P(m, "Size")).Height == 10 && (bool)P(m, "IsAttached")), "Connector height did not persist at 10.");
            var snapshots = connectors.Select(Snapshot).ToArray();
            var objects = Items(P(root, "AllChildren")).Where(T("Design.Object").IsInstanceOfType).Select(Snapshot).ToArray();
            bool? saved = null;
            if (config.SaveAs is not null) saved = await (Task<bool>)T("Commands.Save").GetMethod("SaveFile", Static)!.Invoke(null, [config.SaveAs])!;
            Require(config.SaveAs is null || saved == true, "Native save failed.");
            File.WriteAllText(config.Receipt, JsonSerializer.Serialize(new { status = "complete", pid = Environment.ProcessId,
                geometryEnabled = config.GeometryEnabled, vendorHash = Hash(host.Location),
                modules = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("Designer.Modules.") || a.GetName().Name == "0Harmony")
                    .Select(a => new { name = a.GetName().Name, hash = Hash(a.Location) }),
                checks, snapshots, objects, saved, savedHash = saved == true ? Hash(config.SaveAs!) : null,
                originalUnchanged = Hash(config.Input) == config.InputHash }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            timer?.Stop();
            if (config is not null) File.WriteAllText(config.Receipt, JsonSerializer.Serialize(new { status = "error", error = error.ToString(), checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private async Task TestNested(object manager, object root, object mainCanvas, List<object> checks)
    {
        Require(config!.GeometryEnabled && config.SaveAs is null && !config.CreateAndPack, "Nested run must be an unsaved modded fixture test.");
        var packAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe");
        var mover = packAssembly.GetType("Designer.Modules.SelectionProbe.SelectionMover")!;
        var packer = packAssembly.GetType("Designer.Modules.SelectionProbe.SelectionPacker")!;
        var alignmentType = packAssembly.GetType("Designer.Modules.SelectionProbe.PackAlignment")!;
        var selection = T("Util.Selection").GetField("Global", Static)!.GetValue(null)!;
        var addSelection = selection.GetType().GetMethod("Add", Instance, null, [T("Util.ISelectable"), typeof(bool)], null)!;
        var connectorType = T("Design.ContainerPin");
        bool Scope(object c) => (bool)mover.GetMethod("IsSchematicCanvas", Static)!.Invoke(null, [c])!;
        object Graphic(object model) => Graphics().Single(g => T("Graphic.Object").IsInstanceOfType(g) && ReferenceEquals(P(g, "obj"), model));
        void Select(object[] chosen)
        {
            Call(selection, "Clear");
            foreach (var model in chosen) addSelection.Invoke(selection, [Graphic(model), false]);
        }
        void InOperation(Action action)
        {
            var token = Call(manager, "StartOperation")!;
            action(); Call(manager, "EndOperation", token);
        }
        var sourceGain = Items(P(root, "AllChildren")).Single(m => m.GetType() == T("Design.Component"));
        var samplePins = Items(P(sourceGain, "Pins")).Where(p => (bool)p.GetType().GetField("UserCreated")!.GetValue(p)!).ToArray();
        Require(samplePins.Length > 0, "Fixture must have exposed Gain pins.");
        var hosts = Items(P(root, "AllChildren")).Where(m => T("Design.Container").IsInstanceOfType(m) || T("Design.ChannelGroup").IsInstanceOfType(m)).ToList();
        Require(hosts.Count == 2 && Scope(mainCanvas), "Expected both root Layout hosts.");
        // Add a second nesting level and a user panel, proving we recognize the
        // schematic ownership chain without treating every owned canvas as schematic.
        var outer = hosts.Single(T("Design.Container").IsInstanceOfType);
        var outerCanvas = Items(P(outer, "Canvases"))[0];
        InOperation(() =>
        {
            var creator = Activator.CreateInstance(T("windows.ContainerCreate"))!;
            var inner = Call(creator, "Create", new Point(450, 350))!;
            Set(inner, "InputCount", 3u); Set(inner, "OutputCount", 3u);
            T("Design.ContainerHelper").GetMethod("UpdateContainer", Static)!.Invoke(null, [inner]);
            Call(inner, "Attach"); Call(P(outerCanvas, "CurrentLayer"), "AddChild", inner);
            Call(inner, "AllocateResources", outerCanvas); hosts.Add(inner);
            var userPanel = Activator.CreateInstance(T("Design.Canvas"))!;
            Set(userPanel, "Title", "Packing test user panel"); Set(userPanel, "PageIndex", 1);
            var canvases = P(outer, "Canvases");
            canvases.GetType().GetMethod("Add", Instance, null, [T("Design.Canvas")], null)!.Invoke(canvases, [userPanel]);
            Call(userPanel, "Attach");
            var panelGraphic = Activator.CreateInstance(T("Graphic.GraphicCanvas"))!;
            Set(panelGraphic, "Root", userPanel);
            Require(!Scope(panelGraphic), "Container user panel incorrectly classified as schematic.");
            checks.Add(new { kind = "scope", containerUserPanelExcluded = true });
        });
        foreach (var owner in hosts)
        {
            var hostGraphic = Graphic(owner);
            var parentCanvas = P(hostGraphic, "GraphicCanvas");
            var open = T("Tools.TraverseTool").GetMethods(Static).Single(m => m.Name == "OpenWindow" && m.GetParameters().Length == 6);
            Require((bool)open.Invoke(null, [parentCanvas, hostGraphic, false, null, null, null])!, "Native nested panel did not open.");
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var page = Items(P(owner, "Canvases"))[0];
            var connectors = Items(P(page, "AllChildren")).Where(connectorType.IsInstanceOfType).ToArray();
            var nestedCanvas = P(Graphic(connectors[0]), "GraphicCanvas");
            Require(Scope(nestedCanvas), "Nested schematic not recognized.");
            var gains = new List<object>();
            InOperation(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    var creator = Activator.CreateInstance(T("Drag.ComponentCreator`1").MakeGenericType(T("Design.Component")))!;
                    Set(creator, "ClassName", P(sourceGain, "ClassName"));
                    var gain = Call(creator, "Create", new Point(100 + 200 * i, 200 + 160 * i))!;
                    Call(P(page, "CurrentLayer"), "AddChild", gain); Call(gain, "Attach");
                    foreach (var sample in samplePins)
                    {
                        var spec = Activator.CreateInstance(T("windows.Editors.PinSpec"))!;
                        Set(spec, "CodeName", sample.GetType().GetField("CodeName")!.GetValue(sample)!);
                        Set(spec, "PrettyName", sample.GetType().GetField("PrettyName")!.GetValue(sample)!);
                        T("Design.ComponentHelper").GetMethod("CreateControlPinDirect", Static)!.Invoke(null,
                            [gain, sample.GetType().GetField("Direction")!.GetValue(sample), spec]);
                    }
                    Set(gain, "ProviderUpdatedTrigger", true); gains.Add(gain);
                }
            });
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Require(gains.All(m => ((Size)P(Graphic(m), "Size")).Height > ((Size)P(m, "Size")).Height), "Exposed drawer was not drawn.");
            var nestedObjects = Items(P(page, "AllChildren")).Where(T("Design.Object").IsInstanceOfType).ToArray();
            var labels = nestedObjects.Select(m => P(m, "Label")).ToArray();
            var bodySizes = nestedObjects.Select(m => (Size)P(m, "Size")).ToArray();
            var wireCounts = nestedObjects.Where(T("Design.PinProvider").IsInstanceOfType)
                .SelectMany(m => Items(P(m, "Pins"))).Select(p => Items(P(p, "Wires")).Length).ToArray();
            var cases = new[] { gains.ToArray(), connectors, connectors.Take(2).Concat(gains).ToArray() };
            for (int c = 0; c < cases.Length; c++)
            {
                var chosen = cases[c];
                // Deliberate separation ensures connector zero-gap packing must move items.
                InOperation(() => { for (int i = 0; i < chosen.Length; i++) Set(chosen[i], "Position", new Point(150 + i * 160, 150 + i * 180)); });
                var before = chosen.Select(m => (Point)P(m, "Position")).ToArray();
                var sizes = chosen.Select(m => (Size)P(Graphic(m), "Size")).ToArray();
                foreach (var direction in Enum.GetNames(alignmentType))
                {
                    Select(chosen);
                    Require((bool)packer.GetMethod("CanPackOn", Static)!.Invoke(null, [nestedCanvas])!, "Nested selection disabled.");
                    var receipts = new List<object>();
                    Func<string, object, string> receipt = (kind, data) => { receipts.Add(new { kind, data }); return kind; };
                    packer.GetMethod("Pack", Static)!.Invoke(null, [Enum.Parse(alignmentType, direction), 30, receipt,
                        c == 0 ? null : nestedCanvas, "lab-nested"]);
                    var after = chosen.Select(m => (Point)P(m, "Position")).ToArray();
                    bool row = direction is "Top" or "VerticalCenter" or "Bottom";
                    var order = Enumerable.Range(0, chosen.Length).OrderBy(i => row ? after[i].X : after[i].Y).ToArray();
                    var clearances = new List<double>();
                    for (int j = 1; j < order.Length; j++)
                    {
                        int a = order[j - 1], b = order[j];
                        var clearance = row ? after[b].X - after[a].X - sizes[a].Width : after[b].Y - after[a].Y - sizes[a].Height;
                        var expected = connectorType.IsInstanceOfType(chosen[a]) && connectorType.IsInstanceOfType(chosen[b]) ? 0 : 30;
                        Require(clearance >= expected && clearance < expected + 1, "Nested spacing mismatch."); clearances.Add(clearance);
                    }
                    Call(manager, "Undo"); Require(chosen.Select(m => (Point)P(m, "Position")).SequenceEqual(before), "Nested one-step undo failed.");
                    Call(manager, "Redo"); Require(chosen.Select(m => (Point)P(m, "Position")).SequenceEqual(after), "Nested redo failed.");
                    Call(manager, "Undo");
                    checks.Add(new { owner = owner.GetType().FullName, nestedDepth = ReferenceEquals(owner, hosts[2]) ? 2 : 1,
                        selection = c == 0 ? "components" : c == 1 ? "connectors" : "mixed", direction, clearances, undoRedo = true, receipts });
                }
            }
            // Execute the real routed commands with native nested-canvas focus.
            // This checks the installed router and host focus classifier, not a mock context.
            var canvasElement = (UIElement)nestedCanvas;
            canvasElement.Focus();
            Keyboard.Focus((IInputElement)nestedCanvas);
            Require(ReferenceEquals(Keyboard.FocusedElement, nestedCanvas), "Native nested canvas did not take keyboard focus.");
            var module = packAssembly.GetType("Designer.Modules.SelectionProbe.SelectionProbeModule")!;
            var context = module.GetMethod("ReadPackingContext", Static)!.Invoke(null, [Application.Current.MainWindow])!;
            Require(P(context, "Focus").ToString() == "Schematic" && ReferenceEquals(P(context, "Canvas"), nestedCanvas), "Nested focus route misclassified.");
            foreach (var direction in new[] { "Left", "Right", "Top", "Bottom" })
            {
                Select(gains.ToArray());
                var before = gains.Select(m => (Point)P(m, "Position")).ToArray();
                var command = (RoutedCommand)T("Commands.Pack" + direction).GetProperty("Command", Static)!.GetValue(null)!;
                Require(command.CanExecute(null, canvasElement), "Nested native command disabled.");
                command.Execute(null, canvasElement);
                bool row = direction is "Top" or "Bottom";
                var ordered = gains.OrderBy(m => row ? ((Point)P(m, "Position")).X : ((Point)P(m, "Position")).Y).ToArray();
                var first = (Point)P(ordered[0], "Position"); var second = (Point)P(ordered[1], "Position");
                var size = (Size)P(Graphic(ordered[0]), "Size");
                var clearance = row ? second.X - first.X - size.Width : second.Y - first.Y - size.Height;
                Require(clearance >= 20 && clearance < 21, "Native command did not use default-gap replacement and drawn bounds.");
                Call(manager, "Undo"); Require(gains.Select(m => (Point)P(m, "Position")).SequenceEqual(before), "Native routed command undo failed.");
                checks.Add(new { kind = "native-routed-command", owner = owner.GetType().FullName,
                    nestedDepth = ReferenceEquals(owner, hosts[2]) ? 2 : 1, direction, clearance, undo = true, focus = P(context, "Focus").ToString() });
            }
            Require(nestedObjects.Select(m => P(m, "Label")).SequenceEqual(labels), "Labels changed.");
            Require(nestedObjects.Select(m => (Size)P(m, "Size")).SequenceEqual(bodySizes), "Body sizes changed.");
            Require(nestedObjects.Where(T("Design.PinProvider").IsInstanceOfType).SelectMany(m => Items(P(m, "Pins")))
                .Select(p => Items(P(p, "Wires")).Length).SequenceEqual(wireCounts), "Connection count changed.");
        }
        Call(selection, "Clear");
    }
}
