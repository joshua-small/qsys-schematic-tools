using System.ComponentModel;
using QsysMods.Core;

namespace Designer.Modules.SelectionProbe;

/// <summary>Read-only notification adapter for the host build allowed by Mods Core.</summary>
internal sealed class SelectionNotifications : IDisposable
{
    private readonly Action changed;
    private EventSubscriptions? subscriptions;
    private object? selection;
    private object? manager;
    private Type? connection;
    private bool disposed;

    internal SelectionNotifications(Action changed)
    {
        this.changed = changed;
        Rebind();
    }

    private static object CurrentManager =>
        HostSelection.HostType("QSC.QSys.App").GetField("Manager", Host.Static)?.GetValue(null)
        ?? throw new MissingFieldException("QSC.QSys.App", "Manager");

    internal static bool IsLoading =>
        HostSelection.RequiredProperty(CurrentManager, "IsLoading") is bool loading ? loading
        : throw new InvalidOperationException("The design loading state is unavailable.");

    internal void Rebind()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var nextSelection = HostSelection.GlobalSelection;
        var nextManager = CurrentManager;
        var nextConnection = HostSelection.HostType("QSC.QSys.ControlConnection.Manager");
        if (ReferenceEquals(selection, nextSelection) && ReferenceEquals(manager, nextManager) &&
            connection == nextConnection) return;

        var next = new EventSubscriptions();
        try
        {
            next.Add(nextSelection.GetType(), nextSelection, "SelectionChanged", changed, nextSelection.GetType());
            next.AddProperties(nextManager as INotifyPropertyChanged
                ?? throw new InvalidOperationException("The design manager has no property notifications."),
                changed, "Document", "IsLoading");
            next.Add(nextManager.GetType(), nextManager, "OnEndOperation", changed, typeof(object),
                HostSelection.HostType("QSC.QSys.Design.Manager+EndOperationArgs"));
            next.Add(nextConnection, null, "ConnectionChanged", changed,
                HostSelection.HostType("QSC.QSys.ControlConnection.Manager+ConnectionChangedEventArgs"));
        }
        catch
        {
            next.Dispose();
            throw;
        }
        var previous = subscriptions;
        subscriptions = next;
        selection = nextSelection;
        manager = nextManager;
        connection = nextConnection;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var previous = subscriptions;
        subscriptions = null;
        selection = null;
        manager = null;
        connection = null;
        previous?.Dispose();
    }
}
