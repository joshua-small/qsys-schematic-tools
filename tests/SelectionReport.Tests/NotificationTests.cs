using System.ComponentModel;
using System.Reflection;
using Designer.Modules.SelectionProbe;

internal static class NotificationTests
{
    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("capture context rejects a replaced document on the same manager", () =>
        {
            Reset(); var before = SelectionNotifications.ReadContext();
            Equal(true, before.Matches(SelectionNotifications.ReadContext()));
            Manager.Document = new object();
            Equal(false, before.Matches(SelectionNotifications.ReadContext()));
        });
        yield return ("capture context rejects changed publishers and loading transitions", () =>
        {
            Reset(); var before = SelectionNotifications.ReadContext();
            Manager.IsLoading = true;
            Equal(false, before.Matches(SelectionNotifications.ReadContext()));
            Manager.IsLoading = false;
            QsysMods.Core.HostSelection.GlobalSelection = new SyntheticSelection();
            Equal(false, before.Matches(SelectionNotifications.ReadContext()));
            before = SelectionNotifications.ReadContext();
            SyntheticApp.Manager = new SyntheticManager();
            Equal(false, before.Matches(SelectionNotifications.ReadContext()));
        });
        yield return ("loading context tolerates a temporarily absent document but never publishes it", () =>
        {
            Reset(); Manager.Document = null!; Manager.IsLoading = true;
            var loading = SelectionNotifications.ReadContext();
            Equal(true, loading.IsLoading);
            Equal(false, loading.Matches(loading));
            Manager.IsLoading = false;
            Throws<MissingMemberException>(() => _ = SelectionNotifications.ReadContext());
        });
        yield return ("native adapter routes selection document operation and connection events", () =>
        {
            Reset(); var calls = 0;
            using var notifications = new SelectionNotifications(() => calls++);
            Selection.Select(); Manager.Property("Document"); Manager.Property("IsLoading");
            Manager.End(); SyntheticConnection.Connect();
            Equal(5, calls);
            Manager.Property("Unrelated"); Equal(5, calls);
            Manager.IsLoading = true; Equal(true, SelectionNotifications.IsLoading);
            Manager.IsLoading = false; Equal(false, SelectionNotifications.IsLoading);
        });
        yield return ("rebinding unchanged native publishers retains exactly one handler per event", () =>
        {
            Reset(); var calls = 0;
            using var notifications = new SelectionNotifications(() => calls++);
            notifications.Rebind(); notifications.Rebind();
            Counts(Selection, Manager, 1); Equal(1, SyntheticConnection.Count);
            Selection.Select(); Manager.End(); SyntheticConnection.Connect(); Equal(3, calls);
        });
        yield return ("new native publishers detach old sources and use the current loading state", () =>
        {
            Reset(); var calls = 0;
            using var notifications = new SelectionNotifications(() => calls++);
            var oldSelection = Selection; var oldManager = Manager;
            SyntheticApp.Manager = new SyntheticManager { IsLoading = true };
            QsysMods.Core.HostSelection.GlobalSelection = new SyntheticSelection();
            notifications.Rebind();
            Counts(oldSelection, oldManager, 0); Counts(Selection, Manager, 1);
            Equal(1, SyntheticConnection.Count); Equal(true, SelectionNotifications.IsLoading);
            oldSelection.Select(); oldManager.Property("Document"); oldManager.End(); Equal(0, calls);
            Selection.Select(); Manager.Property("Document"); Equal(2, calls);
            Manager.IsLoading = false; Manager.Property("IsLoading"); Equal(3, calls);
            Equal(false, SelectionNotifications.IsLoading);
        });
        yield return ("failed native rebind rolls back new handlers and preserves detachable old ones", () =>
        {
            Reset(); var calls = 0;
            var notifications = new SelectionNotifications(() => calls++);
            var oldSelection = Selection; var oldManager = Manager;
            SyntheticApp.Manager = new SyntheticManager();
            QsysMods.Core.HostSelection.GlobalSelection = new SyntheticSelection();
            QsysMods.Core.HostSelection.ConnectionType = typeof(object);
            Throws<MissingMemberException>(notifications.Rebind);
            Counts(Selection, Manager, 0); Counts(oldSelection, oldManager, 1);
            Equal(1, SyntheticConnection.Count);
            notifications.Dispose();
            Counts(oldSelection, oldManager, 0); Equal(0, SyntheticConnection.Count);
            oldSelection.Select(); Selection.Select(); SyntheticConnection.Connect(); Equal(0, calls);
        });
        yield return ("native constructor failure leaves no partial subscriptions", () =>
        {
            Reset(); QsysMods.Core.HostSelection.ConnectionType = typeof(object);
            Throws<MissingMemberException>(() => new SelectionNotifications(() => { }));
            Counts(Selection, Manager, 0); Equal(0, SyntheticConnection.Count);
        });
        yield return ("native close and reopen do not deliver callbacks to the old window", () =>
        {
            Reset(); var oldCalls = 0; var newCalls = 0;
            var previous = new SelectionNotifications(() => oldCalls++);
            previous.Dispose(); previous.Dispose();
            Throws<ObjectDisposedException>(previous.Rebind);
            Counts(Selection, Manager, 0); Equal(0, SyntheticConnection.Count);
            using var current = new SelectionNotifications(() => newCalls++);
            Selection.Select(); Manager.Property("Document"); Manager.End(); SyntheticConnection.Connect();
            Equal(0, oldCalls); Equal(4, newCalls);
            Counts(Selection, Manager, 1); Equal(1, SyntheticConnection.Count);
        });
        yield return ("unknown native loading state is rejected rather than inferred", () =>
        {
            Reset();
            SyntheticApp.Manager = new object();
            Throws<MissingMemberException>(() => _ = SelectionNotifications.IsLoading);
            SyntheticApp.Manager = new UnknownLoading();
            Throws<InvalidOperationException>(() => _ = SelectionNotifications.IsLoading);
        });
        yield return ("native events and production session together read only current state", () =>
        {
            Reset(); var queued = new Queue<Action>(); var reads = 0; object? observed = null;
            using var session = new SelectionRefreshSession(queued.Enqueue,
                () => { reads++; observed = SyntheticApp.Manager; }, error => throw error);
            session.Start(changed => new SelectionNotifications(changed));
            Selection.Select(); Manager.IsLoading = true; Manager.Property("IsLoading");
            Manager.Property("Document"); Manager.IsLoading = false; Manager.Property("IsLoading");
            Manager.End(); SyntheticConnection.Connect();
            Equal(0, reads); Equal(1, queued.Count);
            queued.Dequeue()(); Equal(1, reads); Equal(SyntheticApp.Manager, observed);
            Selection.Select(); session.Dispose(); queued.Dequeue()(); Equal(1, reads);
            Counts(Selection, Manager, 0); Equal(0, SyntheticConnection.Count);
        });
    }

    private static SyntheticSelection Selection => (SyntheticSelection)QsysMods.Core.HostSelection.GlobalSelection;
    private static SyntheticManager Manager => (SyntheticManager)SyntheticApp.Manager;
    private static void Reset()
    {
        // Do not clear static event handlers: a leak from the preceding test must fail.
        Equal(0, SyntheticConnection.Count);
        SyntheticApp.Manager = new SyntheticManager();
        QsysMods.Core.HostSelection.GlobalSelection = new SyntheticSelection();
        QsysMods.Core.HostSelection.ConnectionType = typeof(SyntheticConnection);
    }
    private static void Counts(SyntheticSelection selection, SyntheticManager manager, int expected)
    {
        Equal(expected, selection.Count);
        Equal(expected, manager.PropertyCount);
        Equal(expected, manager.OperationCount);
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    internal static class SyntheticApp
    {
        public static object Manager = new SyntheticManager();
    }
    internal sealed class SyntheticSelection
    {
        public delegate void Handler(SyntheticSelection selection);
        public event Handler? SelectionChanged;
        public int Count => SelectionChanged?.GetInvocationList().Length ?? 0;
        public void Select() => SelectionChanged?.Invoke(this);
    }
    internal sealed class SyntheticManager : INotifyPropertyChanged
    {
        public sealed class EndOperationArgs : EventArgs;
        public delegate void EndOperationHandler(object sender, EndOperationArgs args);
        public event EndOperationHandler? OnEndOperation;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsLoading { get; set; }
        public object Document { get; set; } = new();
        public int PropertyCount => PropertyChanged?.GetInvocationList().Length ?? 0;
        public int OperationCount => OnEndOperation?.GetInvocationList().Length ?? 0;
        public void Property(string name) => PropertyChanged?.Invoke(this, new(name));
        public void End() => OnEndOperation?.Invoke(this, new());
    }
    internal static class SyntheticConnection
    {
        public sealed class ConnectionChangedEventArgs : EventArgs;
        public delegate void ConnectionChangedHandler(ConnectionChangedEventArgs args);
        public static event ConnectionChangedHandler? ConnectionChanged;
        public static int Count => ConnectionChanged?.GetInvocationList().Length ?? 0;
        public static void Connect() => ConnectionChanged?.Invoke(new());
    }
    private sealed class UnknownLoading
    {
        public string IsLoading => "Unknown";
    }
}

// Test-only resolution boundary. The production notification adapter is compiled
// unchanged; these synthetic publishers do not emulate Designer or load Core.
namespace QsysMods.Core
{
    internal static class Host
    {
        internal const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
    }
    internal static class HostSelection
    {
        internal static object GlobalSelection = new NotificationTests.SyntheticSelection();
        internal static Type ConnectionType = typeof(NotificationTests.SyntheticConnection);
        internal static Type HostType(string name) => name switch
        {
            "QSC.QSys.App" => typeof(NotificationTests.SyntheticApp),
            "QSC.QSys.ControlConnection.Manager" => ConnectionType,
            "QSC.QSys.Design.Manager+EndOperationArgs" => typeof(NotificationTests.SyntheticManager.EndOperationArgs),
            "QSC.QSys.ControlConnection.Manager+ConnectionChangedEventArgs" => typeof(NotificationTests.SyntheticConnection.ConnectionChangedEventArgs),
            _ => throw new MissingMemberException(name)
        };
        internal static object RequiredProperty(object instance, string name) =>
            instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, name);
    }
}
