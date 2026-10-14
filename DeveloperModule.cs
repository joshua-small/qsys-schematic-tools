using System.Collections;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using QsysMods.Core;

namespace Designer.Modules.SelectionProbe;

/// <summary>Opt-in development diagnostics; ordinary tools live in their own mods.</summary>
public sealed class SelectionProbeModule : ModModule
{
    private Window? window;
    private TextBox? output;
    public SelectionProbeModule() => Mods.Register("schematic-tools", _ =>
    {
        if (!Host.DeveloperMode) return;
        Commands.Register(new("schematic-tools/selection", "Schematic Tools — Selection Diagnostics…", "Schematic Tools", Show, () => Host.DeveloperMode, InMenu: true));
    });
    private void Show()
    {
        if (window is not null) { window.Activate(); return; }
        window = new Window { Title = "Schematic Tools — Selection Diagnostics", Owner = Application.Current.MainWindow, Width = 920, Height = 560 };
        Host.Theme(window); var body = new DockPanel { Margin = new Thickness(12) };
        var read = new Button { Content = "Read current selection", Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(read,Dock.Top); body.Children.Add(read);
        var move = new Button { Content = "Debug: Move selection down 20", Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(move,Dock.Top); body.Children.Add(move);
        output = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(output); window.Content = body; read.Click += (_, _) => Read();
        move.Click += (_, _) =>
        {
            try { output.Text = HostSelection.MoveDown20((kind, detail) => Host.RequiredReceipt("schematic-tools", kind, detail)); }
            catch (Exception ex) { output.Text = ex.GetBaseException().Message; Host.Error("schematic-tools",ex); }
        };
        window.Closed += (_, _) => { window = null; output = null; }; window.Show(); Read();
    }
    private void Read()
    {
        try
        {
            var selection = ((IEnumerable)HostSelection.GlobalSelection).Cast<object>().Take(256).Select(item => new
            {
                type = item.GetType().FullName,
                label = item.GetType().GetProperty("Label")?.GetValue(item) as string,
                position = item.GetType().GetProperty("Position")?.GetValue(item)?.ToString()
            }).ToArray();
            var snapshot = new { selection, hostState = HostSelection.ReadHostState(), unlockedObjects = HostSelection.ReadObjects() };
            output!.Text = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            Host.Receipt("schematic-tools", "selection", snapshot);
        }
        catch(Exception ex) { output!.Text = ex.GetBaseException().Message; Host.Error("schematic-tools",ex); }
    }
}
