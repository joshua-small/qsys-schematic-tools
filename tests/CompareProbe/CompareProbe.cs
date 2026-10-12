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
using HarmonyLib;
namespace Designer.Modules.SnapshotCompareProbe;
public sealed class CompareProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory,string OutputDirectory,string? Input=null,string? InputHash=null);
    private Config? config;private DispatcherTimer? timer;private int attempts;private bool locked;private string phase="starting";
    private static bool comparing;private static int attemptedWrites;
    private static void WriteGuard(){if(comparing){attemptedWrites++;throw new Exception("Comparison attempted a control write or flush.");}}
    private static IEnumerable<DependencyObject> Tree(DependencyObject r){yield return r;for(int i=0;i<VisualTreeHelper.GetChildrenCount(r);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(r,i)))yield return child;}
    private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public string Description=>"Guarded disposable snapshot value research";public string MenuTitle=>"Compare probe";public string Parent=>"mainMenu_Edit";public string NestedMenu=>"";public Key ShortcutKey=>Key.None;public ModifierKeys ModifierKey=>ModifierKeys.None;public string ShortcutInputGestureText=>"";public Action Execute=>()=>{};
    private static Type T(string n)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(n.StartsWith("QSC.")?n:"QSC.QSys."+n)).First(t=>t is not null)!;
    private static object? P(object? o,string n){if(o is null)return null;var t=o as Type??o.GetType();return t.GetProperty(n,F)?.GetValue(o is Type?null:o)??t.GetField(n,F)?.GetValue(o is Type?null:o);}
    private static void Set(object o,string n,object v){var t=o as Type??o.GetType();if(t.GetProperty(n,F) is {} p)p.SetValue(o is Type?null:o,v);else t.GetField(n,F)!.SetValue(o is Type?null:o,v);}
    private static object? Call(object o,string n,params object?[] args){var t=o as Type??o.GetType();return t.GetMethods(F).Single(m=>m.Name==n&&!m.IsGenericMethod&&m.GetParameters().Length==args.Length&&m.GetParameters().Select((p,i)=>args[i] is null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b)).Invoke(o is Type?null:o,args);}
    private static object New(string n)=>Activator.CreateInstance(T(n))!;
    private static object[] Items(object? o)=>o is IEnumerable e?e.Cast<object>().ToArray():Array.Empty<object>();
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    private static string Hash(byte[] bytes)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    private static object FindRow(Window window,object component,string id)=>Items(P(window,"data")).Single(r=>ReferenceEquals(P(P(r,"Source"),"ComponentObject"),component)&&P(P(r,"Source"),"ControlId") as string==id);
    private static async Task Ready(Window window)
    {
        for(int i=0;i<80&&P(window,"savedRead") is not true;i++)await Task.Delay(100);
        Require(P(window,"savedRead") is true,"Comparison did not finish: "+P(P(window,"status"),"Text"));await Task.Delay(700);
    }
    private static Window OpenComparison(object bank)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Designer.Modules.SelectionProbe").GetType("Designer.Modules.SelectionProbe.SnapshotComparison")!;
        var window=(Window)Activator.CreateInstance(type,F,null,new[]{bank},null)!;window.Left=-32000;window.Top=-32000;window.Show();return window;
    }
    private async Task Reopened()
    {
        Require(Environment.GetCommandLineArgs().Contains(config!.Input)&&Hash(File.ReadAllBytes(config.Input!))==config.InputHash,"Reopen fixture/hash mismatch.");
        var connection=T("ControlConnection.Manager");Require(P(P(connection,"Status"),"IsConnected") is not true,"Offline reopen required.");
        var doc=P(P(T("App"),"Manager"),"Document")!;var bank=Items(P(doc,"Snapshots")).Single(b=>P(b,"Name") as string=="Comparison fixture");
        var gain=Items(P(T("Design.ComponentCache"),"Components")).Single(c=>P(c,"UserLabel") as string=="Snapshot value fixture");
        var window=OpenComparison(bank);await Ready(window);var row=FindRow(window,gain,"gain");
        var cells=Items(P(row,"Saved"));Require(Convert.ToSingle(P(P(cells[0],"Value"),"Number"))==-12&&Convert.ToSingle(P(P(cells[1],"Value"),"Number"))==-6,"Reopened saved values changed.");
        Require((P(P(row,"Current"),"Detail") as string??"").Contains("not live"),"Offline Current was not labeled as saved/cached.");
        var current=P(P(P(row,"Current"),"Value"),"Number");window.Close();
        Require(Hash(File.ReadAllBytes(config.Input!))==config.InputHash,"Read-only reopen changed fixture.");
        Write("result.json",new{status="complete",pid=Environment.ProcessId,reopened=true,savedMinus12And6=true,current,offlineSourceLabeled=true,fileUnchanged=true,recalls=0});
    }
    private void Write(string file,object data)=>File.WriteAllText(Path.Combine(config!.OutputDirectory,file),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
    private object[] Controls(object component){var m=component.GetType().GetMethod("GetControls")!;return Items(m.Invoke(component,new[]{Enum.Parse(m.GetParameters()[0].ParameterType,"AllControls")}));}
    public CompareProbe(){var path=Path.Combine(AppContext.BaseDirectory,".snapshot-compare-probe.json");if(!File.Exists(path)||!File.Exists(Path.Combine(AppContext.BaseDirectory,".selection-probe-lab")))return;config=JsonSerializer.Deserialize<Config>(File.ReadAllText(path));if(config is null||Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\')!=AppContext.BaseDirectory.TrimEnd('\\')||File.Exists(Path.Combine(config.OutputDirectory,"result.json")))return;timer=new DispatcherTimer(DispatcherPriority.ContextIdle){Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=async(_,_)=>await Run();timer.Start();}
    private async Task Run()
    {
        try
        {
            if(++attempts<12||Application.Current.MainWindow is not {IsLoaded:true,IsEnabled:true} main)return;timer!.Stop();Directory.CreateDirectory(config!.OutputDirectory);
            if(config.Input is not null){await Reopened();return;}
            Require(main.Title.StartsWith("untitled",StringComparison.OrdinalIgnoreCase),"Blank lab required");var connection=T("ControlConnection.Manager");Require(P(P(connection,"Status"),"IsConnected") is not true,"Disconnected lab required");
            var doc=P(P(T("App"),"Manager"),"Document")!;var layer=P(Items(P(doc,"Pages")).First(),"CurrentLayer")!;
            var creator=Activator.CreateInstance(T("Drag.ComponentCreator`1").MakeGenericType(T("Design.Component")))!;Set(creator,"ClassName","gain");var gain=Call(creator,"Create",new Point(100,100))!;Set(gain,"UserLabel","Snapshot value fixture");Call(layer,"AddChild",gain);Call(gain,"Attach");
            var bank=New("Design.Snapshot.Bank");Set(bank,"Name","Comparison fixture");Set(bank,"Count",3);var member=New("Design.Snapshot.Component");Set(member,"DesignComponent",gain);Set(member,"IsInclusive",true);Call(P(bank,"Components")!,"Add",member);Call(P(doc,"Snapshots")!,"Add",bank);
            var bankCreator=P(bank,"Creator")!;var controller=Call(bankCreator,"Create",new Point(300,100))!;((Delegate)P(bankCreator,"OnInit")!).DynamicInvoke(controller);Call(layer,"AddChild",controller);Call(controller,"Attach");Call(P(T("App"),"Manager")!,"EndOperation");
            Set(creator,"ClassName",P(T("QSC.QSysComponents.ControlScript"),"ClassName2")!);var script=Call(creator,"Create",new Point(100,250))!;Call(layer,"AddChild",script);Call(script,"Attach");
            var scriptMember=New("Design.Snapshot.Component");Set(scriptMember,"DesignComponent",script);Set(scriptMember,"IsInclusive",true);Call(P(bank,"Components")!,"Add",scriptMember);
            var jsonBank=New("Design.Snapshot.Bank");Set(jsonBank,"Name","JSON comparison fixture");Set(jsonBank,"Count",3);Set(jsonBank,"IsJson",true);var jsonMember=New("Design.Snapshot.Component");Set(jsonMember,"DesignComponent",gain);Set(jsonMember,"IsInclusive",true);Call(P(jsonBank,"Components")!,"Add",jsonMember);Call(P(doc,"Snapshots")!,"Add",jsonBank);
            var jsonCreator=P(jsonBank,"Creator")!;var jsonController=Call(jsonCreator,"Create",new Point(500,100))!;((Delegate)P(jsonCreator,"OnInit")!).DynamicInvoke(jsonController);Call(layer,"AddChild",jsonController);Call(jsonController,"Attach");Call(P(T("App"),"Manager")!,"EndOperation");
            Write("fixture.json",new{gainId=P(P(gain,"Id"),"FriendlyString"),encodedGain=P(gain,"Id")!.ToString(),scriptId=P(P(script,"Id"),"FriendlyString"),encodedScript=P(script,"Id")!.ToString(),bankId=P(bank,"FileName"),jsonBankId=P(jsonBank,"FileName"),controllerId=P(P(controller,"Id"),"FriendlyString"),controls=Controls(controller).Select(d=>new{id=P(d,"ControlId"),name=P(d,"PrettyName"),type=P(d,"ControlType")?.ToString()}),gainControls=Controls(gain).Select(d=>new{id=P(d,"ControlId"),type=P(d,"ControlType")?.ToString()}),scriptControls=Controls(script).Select(d=>new{id=P(d,"ControlId"),type=P(d,"ControlType")?.ToString(),proxy=P(d,"SnapshotProxy")})});
            phase="launch emulator";locked=Call(T("Commands.Emulate"),"LockEmulator") is true;Require(locked,"Another emulator is active; no interruption allowed");Require(Call(T("Async.LaunchEmulator"),"RunHeadless") is true,"Lab compile/emulation failed");Call(connection,"InitConnection");
            var group=New("ControlConnection.ChangeGroup");var gainValue=Call(group,"AddControl",P(gain,"Id"),"gain")!;var muteValue=Call(group,"AddControl",P(gain,"Id"),"mute")!;
            await Task.Delay(1500);
            foreach(var slot in new[]{1,2})
            {
                phase="save fixture slot "+slot;
                Call(connection,"SetControlValue",Call(connection,"GetControlID",P(gain,"Id"),"gain"),slot==1?-12f:-6f);
                Call(connection,"SetControlValue",Call(connection,"GetControlID",P(gain,"Id"),"mute"),slot==1?1f:0f);
                Call(connection,"SetControlString",Call(connection,"GetControlID",P(script,"Id"),"code"),"-- Snapshot "+slot+" café λ\n-- "+new string(Enumerable.Range(0,1600).Select(i=>(char)('A'+(i*i+17*i)%26)).ToArray()));
                await Task.Delay(400);
                var save=Controls(controller).First(d=>P(d,"ControlId") as string=="save_"+slot);
                Call(connection,"SetControlValue",Call(connection,"GetControlID",P(controller,"Id"),P(save,"ControlId")),1f);await Task.Delay(600);
                Call(connection,"SetControlValue",Call(connection,"GetControlID",P(jsonController,"Id"),P(save,"ControlId")),1f);await Task.Delay(400);
            }
            Call(connection,"SetControlValue",Call(connection,"GetControlID",P(gain,"Id"),"gain"),-3f);Call(connection,"SetControlValue",Call(connection,"GetControlID",P(gain,"Id"),"mute"),1f);await Task.Delay(700);
            phase="read files without flush";var task=(Task)Call(connection,"GetSettings",null,P(T("App"),"TempPath"))!;await task;var files=Items(P(task,"Result"));
            foreach(var file in files){var name=(string)P(file,"FileName")!;if(name.StartsWith("controlset."))File.WriteAllBytes(Path.Combine(config.OutputDirectory,name),(byte[])P(file,"Contents")!);}
            phase="comparison UI";
            var extension=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Designer.Modules.SelectionProbe");
            var snapshotTools=extension.GetType("Designer.Modules.SelectionProbe.SnapshotTools")!;Call(snapshotTools,"Refresh");
            var hooks=new Harmony("qsys.snapshot-compare-lab-guard");
            foreach(var method in connection.GetMethods(F).Where(m=>m.Name.StartsWith("SetControl",StringComparison.Ordinal)||m.Name=="SnapshotFlush"))hooks.Patch(method,prefix:new HarmonyMethod(typeof(CompareProbe),nameof(WriteGuard)));
            var beforeFiles=files.ToDictionary(f=>(string)P(f,"FileName")!,f=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData((byte[])P(f,"Contents")!)));
            int beforeGroups=Items(P(connection,"_ChangeGroups")).Length;comparing=true;
            var comparisonType=extension.GetType("Designer.Modules.SelectionProbe.SnapshotComparison")!;
            var comparison=(Window)Activator.CreateInstance(comparisonType,F,null,new[]{bank},null)!;comparison.Left=-32000;comparison.Top=-32000;comparison.Show();
            for(int i=0;i<80&&(P(comparison,"savedRead") is not true);i++)await Task.Delay(100);
            Require(P(comparison,"savedRead") is true,"Comparison did not finish: "+P(P(comparison,"status"),"Text"));
            await Task.Delay(800);
            var compareRows=Items(P(comparison,"data"));var gainRow=compareRows.Single(r=>P(P(r,"Source"),"ControlId") as string=="gain");var muteRow=compareRows.Single(r=>P(P(r,"Source"),"ControlId") as string=="mute");
            Require(Convert.ToSingle(P(P(P(gainRow,"Current"),"Value"),"Number"))==-3,"Current Gain is stale or changed by comparison.");
            var savedGain=Items(P(gainRow,"Saved"));Require(Convert.ToSingle(P(P(savedGain[0],"Value"),"Number"))==-12&&Convert.ToSingle(P(P(savedGain[1],"Value"),"Number"))==-6,"UI saved Gain values differ from fixture.");
            Require(P(savedGain[2],"Text") as string=="Not saved","Empty slot not identified.");Require(P(savedGain[0],"Different") is true&&P(savedGain[1],"Different") is true,"Differences not highlighted.");
            Require(P(P(muteRow,"Current"),"Text") as string=="On","Boolean current formatting failed.");
            var emptyRow=FindRow(comparison,script,"magic_number");Require(Items(P(emptyRow,"Saved")).Take(2).All(c=>P(c,"Different") is false),"Empty saved text falsely differs from live empty text.");
            var field=Tree(comparison).OfType<TextBox>().First();field.Text="snapshot gain";var grid=Tree(comparison).OfType<DataGrid>().Single();Require(grid.Items.Count==1,"Comparison search failed.");field.Clear();
            await (Task)Call(comparison,"Reload")!;await Task.Delay(700);Require(attemptedWrites==0,"Read-only guard fired.");
            comparison.UpdateLayout();var bitmap=new RenderTargetBitmap((int)comparison.ActualWidth,(int)comparison.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(comparison);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var image=File.Create(Path.Combine(config.OutputDirectory,"comparison.png")))png.Save(image);
            comparing=false;Call(connection,"SetControlValue",Call(connection,"GetControlID",P(gain,"Id"),"gain"),-2f);comparing=true;await Task.Delay(900);
            gainRow=Items(P(comparison,"data")).Single(r=>P(P(r,"Source"),"ControlId") as string=="gain");Require(Convert.ToSingle(P(P(P(gainRow,"Current"),"Value"),"Number"))==-2,"Current feedback did not update.");
            var afterTask=(Task)Call(connection,"GetSettings",null,P(T("App"),"TempPath"))!;await afterTask;var afterFiles=Items(P(afterTask,"Result"));
            Require(afterFiles.All(f=>!((string)P(f,"FileName")!).Contains(".snapshot.1")&&!((string)P(f,"FileName")!).Contains(".snapshot.2")||beforeFiles[(string)P(f,"FileName")!]==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData((byte[])P(f,"Contents")!))),"Comparison changed stored snapshot files.");
            comparison.Close();Require(Items(P(connection,"_ChangeGroups")).Length==beforeGroups,"Comparison leaked native subscriptions.");
            phase="Global and unsupported format";var global=Items(P(doc,"Snapshots")).Single(b=>T("Design.Snapshot.GlobalBank").IsInstanceOfType(b));var globalWindow=OpenComparison(global);await Ready(globalWindow);Require(Convert.ToSingle(P(P(P(FindRow(globalWindow,gain,"gain"),"Current"),"Value"),"Number"))==-2,"Global live Current is wrong.");globalWindow.Close();
            var unsupported=OpenComparison(jsonBank);await Task.Delay(900);Require((P(P(unsupported,"status"),"Text") as string??"").Contains("JSON snapshot variant"),"Unsupported bank not explained.");Require(Items(P(FindRow(unsupported,gain,"gain"),"Saved")).All(c=>P(c,"Text") as string=="Unavailable"),"Unsupported bank falsely marked Not saved.");unsupported.Close();
            phase="context change and offline embedded settings";comparison=OpenComparison(bank);await Ready(comparison);
            comparing=false;Call(group,"DestoryChangeGroup");Call(connection,"Disconnect");Call(T("Commands.Emulate"),"ReleaseEmulator");locked=false;comparing=true;await Task.Delay(900);
            Require(Items(P(comparison,"data")).Length==0&&(P(P(comparison,"status"),"Text") as string??"").Contains("connection changed"),"Connection change did not invalidate comparison.");comparison.Close();
            // Fixture preparation only: preserve the existing no-flush settings read,
            // then add a new bank member whose controls are absent from old slots.
            comparing=false;Set(doc,"CoreFiles",P(task,"Result")!);Call(P(gain,"CachedControlValues")!,"Clear");
            Set(creator,"ClassName","gain");var lateGain=Call(creator,"Create",new Point(100,400))!;Set(lateGain,"UserLabel","Added after capture");Call(layer,"AddChild",lateGain);Call(lateGain,"Attach");var lateMember=New("Design.Snapshot.Component");Set(lateMember,"DesignComponent",lateGain);Set(lateMember,"IsInclusive",true);Call(P(bank,"Components")!,"Add",lateMember);Call(P(T("App"),"Manager")!,"EndOperation");
            comparing=true;comparison=OpenComparison(bank);await Ready(comparison);gainRow=FindRow(comparison,gain,"gain");var lateRow=FindRow(comparison,lateGain,"gain");
            Require((P(P(gainRow,"Current"),"Detail") as string??"").Contains("Working state embedded"),"Offline Current did not use saved working state.");
            Require(Items(P(lateRow,"Saved")).Take(2).All(c=>P(c,"Text") as string=="Not stored"),"New member was not marked Not stored.");Require(P(P(lateRow,"Current"),"Text") as string=="Unavailable","Missing current value must not become zero.");
            comparison.Close();Require(attemptedWrites==0,"Read-only guard fired.");comparing=false;hooks.UnpatchAll(hooks.Id);
            phase="save separate offline fixture";string savedPath=Path.Combine(config.OutputDirectory,"snapshot-comparison.qsys");Require(await (Task<bool>)Call(T("Commands.Save"),"SaveFile",savedPath)!,"Offline fixture save failed.");
            Write("comparison-checks.json",new{currentMinus3=true,savedMinus12And6=true,emptySlot=true,emptyTextMatches=true,highlighted=true,filter=true,currentFeedbackMinus2=true,savedFilesUnchanged=true,subscriptionsReleased=true,globalLive=true,unsupportedJson=true,contextChangeCleared=true,offlineWorkingState=true,notStored=true,missingCurrentUnavailable=true,attemptedWrites,fixtureHash=Hash(File.ReadAllBytes(savedPath))});
            Write("result.json",new{status="complete",pid=Environment.ProcessId,tempPath=P(T("App"),"TempPath"),files=files.Select(f=>new{name=P(f,"FileName"),length=((byte[])P(f,"Contents")!).Length}),currentGain=new{value=P(gainValue,"Value"),text=P(gainValue,"String")},currentMute=new{value=P(muteValue,"Value"),text=P(muteValue,"String")},recalls=0});
        }
        catch(Exception ex){Directory.CreateDirectory(config!.OutputDirectory);Write("result.json",new{status="error",phase,error=ex.ToString()});}
        finally{comparing=false;if(locked){Call(T("ControlConnection.Manager"),"Disconnect");Call(T("Commands.Emulate"),"ReleaseEmulator");locked=false;}}
    }
}
