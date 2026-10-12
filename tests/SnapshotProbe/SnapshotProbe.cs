using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.SnapshotLabProbe;

public sealed class SnapshotProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string OutputDirectory);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private readonly List<object> checks = new();
    private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public string Description => "Disposable snapshot membership/navigation acceptance";
    public string MenuTitle => "Snapshot lab probe";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
    private static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("QSC.QSys." + name)).First(t => t is not null)!;
    private static Type Extension => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe").GetType("Designer.Modules.SelectionProbe.SnapshotTools")!;
    private static object? P(object? owner, string name) { if (owner is null) return null; var type = owner as Type ?? owner.GetType(); return type.GetProperty(name, F)?.GetValue(owner is Type ? null : owner) ?? type.GetField(name, F)?.GetValue(owner is Type ? null : owner); }
    private static void Set(object owner, string name, object value) { var p = owner.GetType().GetProperty(name, F); if (p is not null) p.SetValue(owner, value); else owner.GetType().GetField(name, F)!.SetValue(owner, value); }
    private static object? Call(object owner, string name, params object?[] args) { var type = owner as Type ?? owner.GetType(); return type.GetMethods(F).Single(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == args.Length && m.GetParameters().Select((p,i) => args[i] is null || p.ParameterType.IsInstanceOfType(args[i])).All(b=>b)).Invoke(owner is Type ? null : owner, args); }
    private static object[] Items(object? items) => items is IEnumerable seq ? seq.Cast<object>().ToArray() : Array.Empty<object>();
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Task Idle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private void Check(object result) { checks.Add(result); File.WriteAllText(Path.Combine(config!.OutputDirectory, "progress.json"), JsonSerializer.Serialize(checks)); }
    public SnapshotProbe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ".snapshot-lab-probe.json");
        if (!File.Exists(path) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
        if (config is null || Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\') != AppContext.BaseDirectory.TrimEnd('\\') || File.Exists(Path.Combine(config.OutputDirectory, "result.json"))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) }; timer.Tick += async (_,_) => await Run(); timer.Start();
    }
    private static object New(string type) => Activator.CreateInstance(T(type))!;
    private static object CreateComponent(object layer)
    {
        var creator = Activator.CreateInstance(T("Drag.ComponentCreator`1").MakeGenericType(T("Design.Component")))!;
        Set(creator, "ClassName", "gain");
        var component = Call(creator, "Create", new Point(150, 100))!;
        Set(component, "UserLabel", "Snapshot Test Gain"); Call(layer, "AddChild", component); Call(component, "Attach");
        return component;
    }
    private static (object bank, object member, object? control) Bank(object doc, object component, string name, bool inclusive)
    {
        object bank = New("Design.Snapshot.Bank"), member = New("Design.Snapshot.Component");
        Set(bank, "Name", name); Set(member, "DesignComponent", component); Set(member, "IsInclusive", inclusive);
        object? control = null;
        if (!inclusive) { control = New("Design.Snapshot.Control"); Set(control, "CodeName", "mute"); Set(control, "PrettyName", "Mute"); Call(P(member, "Controls")!, "Add", control); }
        Call(P(bank, "Components")!, "Add", member); Call(P(doc, "Snapshots")!, "Add", bank); return (bank, member, control);
    }
    private static object[] Rows() => Items(P(P(Extension, "Model"), "Rows"));
    private static IEnumerable<DependencyObject> Tree(DependencyObject root) { yield return root; for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Tree(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private async Task Run()
    {
        try
        {
            if (++attempts < 12 || Application.Current.MainWindow is not { IsEnabled:true, IsLoaded:true } main) return;
            timer!.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            Require(main.Title.StartsWith("untitled", StringComparison.OrdinalIgnoreCase), "Requires blank disposable Designer lab.");
            Require(P(Extension,"installed") is true, "Snapshot hooks did not install.");
            var manager = P(T("App"), "Manager")!; var doc = P(manager, "Document")!;
            Require(P(P(T("ControlConnection.Manager"),"Status"),"IsConnected") is not true, "Must be disconnected.");
            var page = Items(P(doc,"Pages")).First(); var layer = P(page,"CurrentLayer")!;
            Call(Extension,"Refresh"); Require(Rows().Length == 0, "Global must not create user membership rows.");
            var component = CreateComponent(layer); Call(manager,"EndOperation"); await Idle();
            var (one, member, control) = Bank(doc,component,"Selected controls",false);
            var (two, all, _) = Bank(doc,component,"Whole component",true);
            Call(manager,"EndOperation"); await Idle();
            Require(Rows().Count(r => P(r,"ControlId") as string == "mute") == 2, "Mute must belong to both banks.");
            Require(Rows().Any(r=>P(r,"ControlId") as string == "gain" && P(r,"IncludedBy") as string == "Entire component"), "Whole-component expansion missing gain.");
            Require(!Rows().Any(r=>(P(r,"ControlId") as string ?? "").Contains("meter")), "Meter should not get membership.");
            Check(new { membershipRows=Rows().Select(r=>new{bank=P(r,"Bank"),control=P(r,"ControlId"),reason=P(r,"IncludedBy")}).ToArray(),globalExcluded=true });
            Set(one,"Name","Renamed bank"); await Idle(); Require(Rows().Any(r=>P(r,"Bank") as string == "Renamed bank"),"Bank rename stale.");
            Call(manager,"EndOperation"); Call(manager,"Undo"); await Idle(); Require(Rows().Any(r=>P(r,"Bank") as string == "Selected controls"),"Bank rename undo stale.");
            Call(manager,"Redo"); await Idle(); Require(Rows().Any(r=>P(r,"Bank") as string == "Renamed bank"),"Bank rename redo stale.");
            Call(control!,"Detach"); Call(manager,"EndOperation"); await Idle(); Require(Rows().Count(r=>P(r,"ControlId") as string=="mute")==1,"Removed member stale.");
            Call(manager,"Undo"); await Idle(); Require(Rows().Count(r=>P(r,"ControlId") as string=="mute")==2,"Membership undo stale.");
            Check(new{renameUndoRedo=true,membershipRemovalUndo=true});
            Require(Call(Extension,"NavigateEntry",control) is true,"Snapshot entry navigation declined.");
            object? graphic = null;
            for(int i=0;i<50 && graphic is null;i++) { await Task.Delay(100); var selection=Items(P(T("Util.Selection"),"Global")); graphic=selection.FirstOrDefault(x=>T("Graphic.Controls.ControlBase").IsInstanceOfType(x)&&P(P(x,"Style"),"Name") as string=="mute"); }
            Require(graphic is not null,"Native navigation did not open/select the mute control.");
            var style=P(graphic,"Style")!; var size=(Size)P(graphic,"Size")!;
            string hover=(string)P(graphic,"HoverText")!; Require(hover.Contains("Renamed bank")&&hover.Contains("Whole component"),"Hover does not list both banks.");
            DrawingGroup Draw() {var group=new DrawingGroup(); using(var dc=group.Open()) T("Graphic.Controls.ControlBase").GetMethod("DrawIsNamed",F)!.Invoke(graphic,new object[]{dc}); return group;}
            var single=Draw(); Require(single.Children.Count==1,"Expected one snapshot dot.");
            var dot=(GeometryDrawing)single.Children[0]; Require(((SolidColorBrush)dot.Brush).Color==Color.FromRgb(0xf3,0x9e,0x77)&&((SolidColorBrush)dot.Pen.Brush).Color==Color.FromRgb(0xff,0x8c,0x00)&&dot.Pen.Thickness==0.5,"Dot colors/stroke differ.");
            var ellipse=(EllipseGeometry)dot.Geometry; Require(ellipse.RadiusX==3&&ellipse.Center==new Point(size.Width-3,size.Height-3),"Dot size/position differs.");
            var named=New("Design.ExternalControlId"); Set(named,"Component",component); Set(named,"BaseControlId","mute"); Set(named,"PrettyName","Mute"); Set(named,"Id","SnapshotTestMute"); Call(P(doc,"ExternalControlIds")!,"Add",named); await Idle();
            var both=Draw(); Require(both.Children.Count==2,"Named and snapshot dots must coexist.");
            Require(((EllipseGeometry)((GeometryDrawing)both.Children[1]).Geometry).Center.X==size.Width-11,"Dual dots overlap.");
            Call(P(T("Util.Selection"),"Global")!,"Clear"); Call(P(T("Util.Selection"),"Global")!,"OnSelectionChanged"); Call(graphic!,"UpdateControlVisual");
            var sample = new DrawingVisual(); using (var dc=sample.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40,43,46)),null,new Rect(0,0,240,120)); dc.PushTransform(new TranslateTransform(30,30)); dc.PushTransform(new ScaleTransform(3,3)); dc.DrawDrawing(((DrawingVisual)graphic!).Drawing); dc.Pop(); dc.Pop(); }
            var dotImage=new RenderTargetBitmap(240,120,96,96,PixelFormats.Pbgra32); dotImage.Render(sample); var dotEncoder=new PngBitmapEncoder(); dotEncoder.Frames.Add(BitmapFrame.Create(dotImage)); using(var file=File.Create(Path.Combine(config.OutputDirectory,"dots.png"))) dotEncoder.Save(file);
            Check(new{navigationSelectedMute=true,hoverBanks=true,dotColors=true,dotRadius=3,namedCoexistence=true});
            var sidebar=(UserControl)New("windows.DesignElementViewers.SnapshotViewer");
            Call(T("windows.AccordionPanel"),"SetIsExpanded",sidebar,true);
            var sidebarWindow=new Window{Content=sidebar,Width=450,Height=500,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false};
            sidebarWindow.Show(); await Idle();
            var nativeTree=(TreeView)P(P(sidebar,"_Viewer"),"_TreeView")!;
            for(int pass=0;pass<4;pass++){foreach(var item in Tree(nativeTree).OfType<TreeViewItem>().ToArray()) item.IsExpanded=true; nativeTree.UpdateLayout(); await Idle();}
            var controlItem=Tree(nativeTree).OfType<TreeViewItem>().FirstOrDefault(i=>ReferenceEquals(i.DataContext,control));
            File.WriteAllText(Path.Combine(config.OutputDirectory,"sidebar.json"),JsonSerializer.Serialize(new{items=nativeTree.Items.Count,width=nativeTree.ActualWidth,height=nativeTree.ActualHeight,nodes=Tree(nativeTree).OfType<TreeViewItem>().Select(i=>new{data=i.DataContext?.GetType().FullName,name=P(i.DataContext,"Name"),code=P(i.DataContext,"CodeName"),expanded=i.IsExpanded,items=i.Items.Count})},new JsonSerializerOptions{WriteIndented=true}));
            Require(controlItem is not null,"Snapshot sidebar did not materialize the control entry.");
            var mouse=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent,Source=controlItem};
            typeof(MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(mouse,2);
            controlItem!.RaiseEvent(mouse); await Task.Delay(500);
            Check(new{sidebarAttached=nativeTree.GetValue((DependencyProperty)P(Extension,"AttachedProperty")!),clickCount=mouse.ClickCount,handled=mouse.Handled,entry=P(Call(Extension,"Entry",nativeTree,controlItem),"CodeName")});
            Require(mouse.Handled,"Sidebar double-click was not routed.");
            Require(Items(P(T("Util.Selection"),"Global")).Any(x=>T("Graphic.Controls.ControlBase").IsInstanceOfType(x)&&P(P(x,"Style"),"Name") as string=="mute"),"Sidebar double-click did not select control.");
            var opening=(ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),F,null,new object[]{controlItem,true},null)!;
            opening.RoutedEvent=ContextMenuService.ContextMenuOpeningEvent; controlItem.RaiseEvent(opening);
            Require(nativeTree.ContextMenu.Items.OfType<MenuItem>().Any(i=>i.Header as string=="Go to control"),"Sidebar context action missing.");
            var bankItem=Tree(nativeTree).OfType<TreeViewItem>().First(i=>ReferenceEquals(i.DataContext,one));
            var bankOpening=(ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),F,null,new object[]{bankItem,true},null)!;
            bankOpening.RoutedEvent=ContextMenuService.ContextMenuOpeningEvent;bankItem.RaiseEvent(bankOpening);
            Require(!nativeTree.ContextMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Tag,"qsys.schematic-tools.snapshot-membership")),"Previous control action leaked into bank menu.");
            sidebarWindow.Close(); Check(new{nativeSidebarDoubleClick=true,nativeSidebarContextAction=true});
            Call(Extension,"Show",new object?[]{null}); await Idle();
            var inspector=(Window)P(Extension,"inspector")!; inspector.Left=-32000; inspector.Top=-32000;
            var grid=Tree(inspector).OfType<DataGrid>().Single(); var search=Tree(inspector).OfType<TextBox>().First(); search.Text="renamed mute"; Require(grid.Items.Count==1,"Inspector multi-term search failed.");
            search.Clear(); inspector.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)inspector.ActualWidth,(int)inspector.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(inspector); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using(var file=File.Create(Path.Combine(config.OutputDirectory,"inspector.png"))) png.Save(file);
            inspector.Close();
            var workspace=Extension.Assembly.GetType("Designer.Modules.SelectionProbe.SnapshotWorkspace")!;
            var stale=P(P(Extension,"Model"),"Rows")!; Call(control!,"Detach"); await Idle(); bool staleRejected=false;
            try { Call(workspace,"Create",stale,"Stale results"); } catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { staleRejected=true; }
            Require(staleRejected,"Workspace did not reject stale membership."); Call(manager,"EndOperation"); Call(manager,"Undo"); await Idle();
            int pagesBefore=Items(P(doc,"Pages")).Length;
            var generated=Call(workspace,"Create",P(P(Extension,"Model"),"Rows"),"Lab banks")!; await Idle();
            Require(P(generated,"PageBackgroundColor") is uint color&&color==0xff282b2e,"Generated page must retain its contrast setting.");
            var gathered=Items(P(generated,"AllChildren")).Where(T("Design.ControlStyle").IsInstanceOfType).ToArray();
            Require(gathered.Length==4,"Generated page must deduplicate shared Mute.");
            Require(gathered.All(s=>Equals(P(s,"Id"),P(component,"Id")) && P(s,"IsInControlPanel") is false),"Generated controls lost original binding or source/copy distinction.");
            Require(Items(P(doc,"Pages")).Length==pagesBefore+1,"Generated page not attached.");
            Call(manager,"Undo"); await Idle(); Require(Items(P(doc,"Pages")).Length==pagesBefore,"Workspace undo did not remove page.");
            Call(manager,"Redo"); await Idle(); Require(Items(P(doc,"Pages")).Length==pagesBefore+1,"Workspace redo failed.");
            Check(new{gatheredControls=gathered.Select(s=>new{control=P(s,"Name"),component=P(P(s,"Id"),"FriendlyString"),size=P(s,"Size")?.ToString()}).ToArray(),deduplicated=true,originalBinding=true,workspaceUndoRedo=true});
            var fixture=Path.Combine(config.OutputDirectory,"Snapshot indicator fixture.qsys"); Require(!File.Exists(fixture),"Preserve prior fixture.");
            bool saved=await (Task<bool>)Call(T("Commands.Save"),"SaveFile",fixture)!; Require(saved,"Fixture save failed.");
            Check(new{inspectorFiltering=true,fixtureSaved=fixture});
            File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"),JsonSerializer.Serialize(new{status="complete",pid=Environment.ProcessId,checks},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex) {timer?.Stop(); Directory.CreateDirectory(config!.OutputDirectory); File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"),JsonSerializer.Serialize(new{status="error",error=ex.ToString(),checks},new JsonSerializerOptions{WriteIndented=true}));}
    }
}
