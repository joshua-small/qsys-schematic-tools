using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QsysMods.Core;

namespace Designer.Modules.SelectionProbe;

/// <summary>Opt-in development diagnostics; ordinary tools live in their own mods.</summary>
public sealed class SelectionProbeModule : ModModule
{
    private Window? window;
    private TextBox? output;
    private TextBlock? refreshStatus;
    private SelectionRefreshSession? refreshSession;
    private SelectionNotifications? notifications;
    public SelectionProbeModule() => Mods.Register("schematic-tools", _ =>
    {
        if (!Host.DeveloperMode) return;
        Commands.Register(new("schematic-tools/selection", "Schematic Tools — Selection Diagnostics…", "Schematic Tools", Show, () => Host.DeveloperMode, InMenu: true));
    });
    private void Show()
    {
        if (!Host.Enabled || !Host.DeveloperMode) return;
        if (window is not null) { window.Activate(); return; }
        window = new Window { Title = "Schematic Tools — Selection Diagnostics", Owner = Application.Current.MainWindow, Width = 920, Height = 560 };
        var view = window;
        Host.Theme(window); var body = new DockPanel { Margin = new Thickness(12) };
        refreshStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };
        DockPanel.SetDock(refreshStatus, Dock.Top); body.Children.Add(refreshStatus);
        var read = new Button { Content = "Read current selection", Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(read,Dock.Top); body.Children.Add(read);
        var move = new Button { Content = "Debug: Move selection down 20", Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,0,10), HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(move,Dock.Top); body.Children.Add(move);
        output = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(output); window.Content = body; read.Click += (_, _) => ReadCurrent();
        move.Click += (_, _) =>
        {
            try { output.Text = HostSelection.MoveDown20((kind, detail) => Host.RequiredReceipt("schematic-tools", kind, detail)); }
            catch (Exception ex) { output.Text = ex.GetBaseException().Message; Host.Error("schematic-tools",ex); }
        };
        window.Activated += (_, _) => refreshSession?.RequestRefresh();
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(window, view)) return;
            StopRefresh();
            window = null; output = null; refreshStatus = null;
        };
        window.Show();
        try
        {
            refreshSession = new SelectionRefreshSession(
                action =>
                {
                    if (view.Dispatcher.HasShutdownStarted || view.Dispatcher.HasShutdownFinished)
                        throw new InvalidOperationException("The report dispatcher is shutting down.");
                    view.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, action);
                },
                Read, ex => Host.Error("schematic-tools", ex));
            refreshSession.Start(changed => notifications = new SelectionNotifications(changed));
            refreshStatus.Text = "Automatic refresh is active. Read current selection also refreshes immediately.";
            Host.Receipt("schematic-tools", "selection-refresh-started", new { notificationSources = 4 });
        }
        catch (Exception ex)
        {
            StopRefresh();
            refreshStatus.Text = "Automatic refresh unavailable. Use Read current selection.";
            Host.Error("schematic-tools", ex);
        }
        ReadCurrent();
    }
    private void StopRefresh()
    {
        try { refreshSession?.Dispose(); }
        catch (Exception ex) { Host.Error("schematic-tools", ex); }
        finally { refreshSession = null; notifications = null; }
    }
    private void ReadCurrent()
    {
        if (refreshSession is not null) refreshSession.RefreshNow();
        else Read();
    }
    private void Read()
    {
        if (window is null || output is null) return;
        window.Dispatcher.VerifyAccess();
        if (!Host.Enabled || !Host.DeveloperMode) { window.Close(); return; }
        try
        {
            try { notifications?.Rebind(); }
            catch (Exception ex)
            {
                StopRefresh();
                refreshStatus!.Text = "Automatic refresh unavailable. Use Read current selection.";
                Host.Error("schematic-tools", ex);
            }
            // Never show the previous document's report during a nested loading dispatch.
            if (SelectionNotifications.IsLoading)
            {
                output.Text = "Selection temporarily unavailable while the design is loading.";
                return;
            }
            var snapshot = SelectionReport.Capture(
                (IEnumerable)HostSelection.GlobalSelection,
                () => HostSelection.ReadHostState(),
                () => HostSelection.ReadObjects());
            output!.Text = SelectionReport.Serialize(snapshot);
            Host.Receipt("schematic-tools", "selection", snapshot);
        }
        catch(Exception ex) { output!.Text = ex.GetBaseException().Message; Host.Error("schematic-tools",ex); }
    }
}
