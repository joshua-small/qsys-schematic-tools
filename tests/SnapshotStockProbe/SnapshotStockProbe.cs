using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.SnapshotStockProbe;

public sealed class StockProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory,string Input,string InputHash,string OutputDirectory);
    private Config? config; private DispatcherTimer? timer; private int attempts;
    private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public string Description=>"Read-only generated-page compatibility check";
    public string MenuTitle=>"Snapshot stock probe";
    public string Parent=>"mainMenu_Edit"; public string NestedMenu=>"";
    public Key ShortcutKey=>Key.None; public ModifierKeys ModifierKey=>ModifierKeys.None;
    public string ShortcutInputGestureText=>""; public Action Execute=>()=>{};
    private static Type T(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("QSC.QSys."+name)).First(t=>t is not null)!;
    private static object? P(object? o,string n){if(o is null)return null;var t=o as Type??o.GetType();return t.GetProperty(n,F)?.GetValue(o is Type?null:o)??t.GetField(n,F)?.GetValue(o is Type?null:o);}
    private static object? Call(object o,string n,params object?[] args){var t=o as Type??o.GetType();return t.GetMethods(F).Single(m=>m.Name==n&&!m.IsGenericMethod&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i] is null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b)).Invoke(o is Type?null:o,args);}
    private static object[] Items(object? x)=>x is IEnumerable seq?seq.Cast<object>().ToArray():Array.Empty<object>();
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool b,string message){if(!b)throw new Exception(message);}
    private static IEnumerable<DependencyObject> Tree(DependencyObject r){yield return r;for(int i=0;i<VisualTreeHelper.GetChildrenCount(r);i++)foreach(var c in Tree(VisualTreeHelper.GetChild(r,i)))yield return c;}
    public StockProbe()
    {
        string path=Path.Combine(AppContext.BaseDirectory,".snapshot-stock-probe.json");
        if(!File.Exists(path)||!File.Exists(Path.Combine(AppContext.BaseDirectory,".selection-probe-lab")))return;
        config=JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
        if(config is null||Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\')!=AppContext.BaseDirectory.TrimEnd('\\')||File.Exists(Path.Combine(config.OutputDirectory,"result.json")))return;
        if(!Environment.GetCommandLineArgs().Contains(config.Input)||Hash(config.Input)!=config.InputHash)return;
        timer=new DispatcherTimer(DispatcherPriority.ContextIdle){Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=async(_,_)=>await Run();timer.Start();
    }
    private async Task Run()
    {
        try
        {
            if(++attempts<15||Application.Current.MainWindow is not {IsLoaded:true,IsEnabled:true} main)return;
            if(!main.Title.StartsWith(Path.GetFileNameWithoutExtension(config!.Input),StringComparison.Ordinal)) {if(attempts>80)throw new Exception("Fixture did not open.");return;}
            timer!.Stop();Directory.CreateDirectory(config.OutputDirectory);
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "Designer.Modules.SelectionProbe" or "Designer.Modules.AutomaticGeometry"),"Feature mods must be absent.");
            var doc=P(P(T("App"),"Manager"),"Document")!;
            var page=Items(P(doc,"Pages")).Single(p=>(P(p,"Title") as string??"").StartsWith("Snapshot controls - Lab banks"));
            Require(P(page,"PageBackgroundColor") is uint color&&color==0xff282b2e,"Page background did not persist.");
            Call(P(T("App"),"Window")!,"OpenPage",page);await Task.Delay(500);
            var controls=Items(P(page,"AllChildren")).Where(T("Design.ControlStyle").IsInstanceOfType).ToArray();
            Require(controls.Length==4,"Generated control count changed.");
            var bound=controls.Select(c=>new{control=P(c,"Name"),component=Call(T("Design.ComponentCache"),"GetComponent",P(c,"Id")),orphaned=P(c,"IsOrphaned"),source=P(c,"IsInControlPanel"),size=P(c,"Size")?.ToString()}).ToArray();
            Require(bound.All(c=>c.component is not null&&P(c.component,"ClassName") as string=="gain"&&c.orphaned is false&&c.source is false),"Stock Designer did not retain native linked controls.");
            var canvas=Tree(main).OfType<FrameworkElement>().FirstOrDefault(v=>T("Graphic.GraphicCanvas").IsInstanceOfType(v)&&ReferenceEquals(P(v,"Root"),page));
            if(canvas is not null){canvas.UpdateLayout();var bitmap=new RenderTargetBitmap((int)canvas.ActualWidth,(int)canvas.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(config.OutputDirectory,"workspace-stock.png"));png.Save(file);}
            Require(Hash(config.Input)==config.InputHash,"Compatibility check changed the original fixture.");
            File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"),JsonSerializer.Serialize(new{status="complete",pid=Environment.ProcessId,featureModsAbsent=true,originalUnchanged=true,controls=bound.Select(c=>new{c.control,c.orphaned,c.source,c.size,componentClass=P(c.component,"ClassName")}),rendered=canvas is not null},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){timer?.Stop();Directory.CreateDirectory(config!.OutputDirectory);File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"),JsonSerializer.Serialize(new{status="error",error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));}
    }
}
