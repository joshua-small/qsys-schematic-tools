using System.Collections.Concurrent;
using System.ComponentModel;
using Designer.Modules.SelectionProbe;

internal static class RefreshTests
{
    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("notifications coalesce into one deferred read", () =>
        {
            var q = new Queue();
            var source = new Source();
            var reads = 0;
            using var session = new SelectionRefreshSession(q.Post, () => reads++, _ => { });
            session.Start(source.Subscribe);
            for (var i = 0; i < 100; i++) source.Raise();
            Equal(0, reads); Equal(1, q.Count);
            q.Drain(); Equal(1, reads);
        });
        yield return ("deferred refresh reads the latest document and session", () =>
        {
            var q = new Queue(); var source = new Source();
            var state = "document A / Design"; string? observed = null;
            using var session = new SelectionRefreshSession(q.Post, () => observed = state, _ => { });
            session.Start(source.Subscribe);
            source.Raise(); state = "loading"; source.Raise();
            state = "document B / Emulate"; source.Raise(); q.Drain();
            Equal("document B / Emulate", observed);
        });
        yield return ("worker notifications queue without reading on the worker", () =>
        {
            var q = new Queue(); var source = new Source(); var readThread = 0;
            var owner = Environment.CurrentManagedThreadId;
            using var session = new SelectionRefreshSession(q.Post, () => readThread = Environment.CurrentManagedThreadId, _ => { });
            session.Start(source.Subscribe);
            Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(source.Raise))).GetAwaiter().GetResult();
            Equal(0, readThread); Equal(1, q.Count); q.Drain(); Equal(owner, readThread);
        });
        yield return ("manual Read cancels an older queued repaint", () =>
        {
            var q = new Queue(); var source = new Source(); var reads = 0;
            using var session = new SelectionRefreshSession(q.Post, () => reads++, _ => { });
            session.Start(source.Subscribe); source.Raise(); session.RefreshNow();
            Equal(1, reads); q.Drain(); Equal(1, reads);
        });
        yield return ("old queued work cannot consume a newer request after manual Read", () =>
        {
            var q = new Queue(); var source = new Source(); var reads = 0;
            using var session = new SelectionRefreshSession(q.Post, () => reads++, _ => { });
            session.Start(source.Subscribe); source.Raise(); session.RefreshNow(); source.Raise();
            q.Next(); Equal(1, reads); q.Next(); Equal(2, reads);
        });
        yield return ("Start is idempotent and does not duplicate subscriptions", () =>
        {
            var q = new Queue(); var source = new Source();
            using var session = new SelectionRefreshSession(q.Post, () => { }, _ => { });
            session.Start(source.Subscribe); session.Start(source.Subscribe);
            Equal(1, source.Count);
        });
        yield return ("requests before Start do not read or enqueue", () =>
        {
            var q = new Queue();
            using var session = new SelectionRefreshSession(q.Post, () => throw new Exception("unexpected read"), _ => { });
            session.RequestRefresh(); session.RefreshNow(); Equal(0, q.Count);
        });
        yield return ("close unsubscribes and suppresses queued and future callbacks", () =>
        {
            var q = new Queue(); var source = new Source(); var reads = 0;
            var session = new SelectionRefreshSession(q.Post, () => reads++, _ => { });
            session.Start(source.Subscribe); source.Raise(); session.Dispose(); session.Dispose();
            Equal(0, source.Count);
            source.Raise(); session.RefreshNow(); session.RequestRefresh(); q.Drain();
            Equal(0, reads);
        });
        yield return ("reopen has one subscription and ignores the closed window queue", () =>
        {
            var q = new Queue(); var source = new Source(); var oldReads = 0; var newReads = 0;
            var old = new SelectionRefreshSession(q.Post, () => oldReads++, _ => { });
            old.Start(source.Subscribe); source.Raise(); old.Dispose();
            using var current = new SelectionRefreshSession(q.Post, () => newReads++, _ => { });
            current.Start(source.Subscribe); source.Raise(); q.Drain();
            Equal(0, oldReads); Equal(1, newReads); Equal(1, source.Count);
        });
        yield return ("failed subscription invalidates callbacks already queued", () =>
        {
            var q = new Queue(); var reads = 0;
            using var session = new SelectionRefreshSession(q.Post, () => reads++, _ => { });
            Throws<InvalidOperationException>(() => session.Start(changed =>
            {
                changed(); throw new InvalidOperationException("subscription failed");
            }));
            q.Drain(); Equal(0, reads);
        });
        yield return ("disposing during subscription releases the acquired handle", () =>
        {
            var q = new Queue(); var removed = 0;
            using var session = new SelectionRefreshSession(q.Post, () => { }, _ => { });
            session.Start(_ => { session.Dispose(); return new Cleanup(() => removed++); });
            Equal(1, removed);
        });
        yield return ("a disposed session cannot be restarted", () =>
        {
            var source = new Source();
            var session = new SelectionRefreshSession(_ => { }, () => { }, _ => { });
            session.Dispose();
            Throws<ObjectDisposedException>(() => session.Start(source.Subscribe)); Equal(0, source.Count);
        });
        yield return ("refresh failure is reported and the next request can recover", () =>
        {
            var q = new Queue(); var source = new Source(); var attempts = 0; var errors = 0;
            using var session = new SelectionRefreshSession(q.Post,
                () => { if (++attempts == 1) throw new InvalidOperationException(); }, _ => errors++);
            session.Start(source.Subscribe); source.Raise(); q.Drain(); source.Raise(); q.Drain();
            Equal(2, attempts); Equal(1, errors);
        });
        yield return ("failed dispatcher posting does not leave refresh stuck pending", () =>
        {
            var q = new Queue(); var source = new Source(); var attempts = 0; var errors = 0; var reads = 0;
            using var session = new SelectionRefreshSession(action =>
            {
                if (++attempts == 1) throw new InvalidOperationException("dispatcher unavailable");
                q.Post(action);
            }, () => reads++, _ => errors++);
            session.Start(source.Subscribe); source.Raise(); source.Raise(); q.Drain();
            Equal(1, errors); Equal(1, reads);
        });
        yield return ("an error sink cannot throw into notification dispatch", () =>
        {
            var q = new Queue(); var source = new Source();
            using var session = new SelectionRefreshSession(q.Post, () => throw new InvalidOperationException(),
                _ => throw new IOException());
            session.Start(source.Subscribe); source.Raise(); q.Drain();
        });
        yield return ("a change during refresh gets one following read", () =>
        {
            var q = new Queue(); var source = new Source(); var reads = 0;
            using var session = new SelectionRefreshSession(q.Post, () =>
            {
                if (++reads == 1) { source.Raise(); source.Raise(); }
            }, _ => { });
            session.Start(source.Subscribe); source.Raise(); q.Drain();
            Equal(2, reads);
        });
        yield return ("closing from a refresh leaves no future reader", () =>
        {
            var q = new Queue(); var source = new Source(); var reads = 0;
            SelectionRefreshSession? session = null;
            session = new SelectionRefreshSession(q.Post, () => { reads++; session!.Dispose(); }, _ => { });
            session.Start(source.Subscribe); source.Raise(); q.Drain(); source.Raise(); q.Drain();
            Equal(1, reads); Equal(0, source.Count);
        });
        yield return ("exact one-argument reflected event attaches and detaches", () =>
        {
            var source = new NativeSource(); var calls = 0;
            var subscriptions = new EventSubscriptions();
            subscriptions.Add(typeof(NativeSource), source, nameof(NativeSource.SelectionChanged),
                () => calls++, typeof(NativeSource));
            source.Select(); Equal(1, calls); subscriptions.Dispose(); subscriptions.Dispose();
            source.Select(); Equal(1, calls); Equal(0, source.SelectionHandlers);
        });
        yield return ("exact two-argument reflected event attaches and detaches", () =>
        {
            var source = new NativeSource(); var calls = 0;
            var subscriptions = new EventSubscriptions();
            subscriptions.Add(typeof(NativeSource), source, nameof(NativeSource.Operation),
                () => calls++, typeof(object), typeof(EventArgs));
            source.End(); subscriptions.Dispose(); source.End(); Equal(1, calls);
        });
        yield return ("static session event detaches the same delegate", () =>
        {
            var calls = 0; var subscriptions = new EventSubscriptions();
            subscriptions.Add(typeof(NativeSource), null, nameof(NativeSource.Connection),
                () => calls++, typeof(EventArgs));
            NativeSource.Connect(); subscriptions.Dispose(); NativeSource.Connect(); Equal(1, calls);
        });
        yield return ("an unexpected event signature is rejected before attaching", () =>
        {
            var source = new NativeSource(); using var subscriptions = new EventSubscriptions();
            Throws<InvalidOperationException>(() => subscriptions.Add(typeof(NativeSource), source,
                nameof(NativeSource.SelectionChanged), () => { }, typeof(object)));
            Equal(0, source.SelectionHandlers);
        });
        yield return ("a missing native event is rejected", () =>
        {
            using var subscriptions = new EventSubscriptions();
            Throws<MissingMemberException>(() => subscriptions.Add(typeof(NativeSource), new NativeSource(),
                "Missing", () => { }));
        });
        yield return ("document properties include null and empty all-property notifications", () =>
        {
            var source = new NativeSource(); var calls = 0;
            using var subscriptions = new EventSubscriptions();
            subscriptions.AddProperties(source, () => calls++, "Document", "IsLoading");
            source.Property("Unrelated"); Equal(0, calls);
            source.Property("Document"); source.Property("IsLoading"); source.Property(null); source.Property("");
            Equal(4, calls); subscriptions.Dispose(); source.Property("Document"); Equal(4, calls);
        });
        yield return ("partial binding failure can roll back all prior handlers", () =>
        {
            var source = new NativeSource();
            Throws<MissingMemberException>(() =>
            {
                using var subscriptions = new EventSubscriptions();
                subscriptions.Add(typeof(NativeSource), source, nameof(NativeSource.SelectionChanged), () => { }, typeof(NativeSource));
                subscriptions.Add(typeof(NativeSource), source, "Missing", () => { });
            });
            Equal(0, source.SelectionHandlers);
        });
        yield return ("one removal failure does not skip remaining detach operations", () =>
        {
            var source = new NativeSource(); var subscriptions = new EventSubscriptions();
            subscriptions.Add(typeof(NativeSource), source, nameof(NativeSource.SelectionChanged), () => { }, typeof(NativeSource));
            subscriptions.Add(typeof(NativeSource), source, nameof(NativeSource.FailingRemoval), () => { });
            Throws<AggregateException>(subscriptions.Dispose); Equal(0, source.SelectionHandlers);
            subscriptions.Dispose();
        });
        yield return ("disposed subscription sets refuse new handlers", () =>
        {
            var source = new NativeSource(); var subscriptions = new EventSubscriptions(); subscriptions.Dispose();
            Throws<ObjectDisposedException>(() => subscriptions.AddProperties(source, () => { }, "Document"));
            Throws<ObjectDisposedException>(() => subscriptions.Add(typeof(NativeSource), source,
                nameof(NativeSource.SelectionChanged), () => { }, typeof(NativeSource)));
            Equal(0, source.SelectionHandlers);
        });
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private sealed class Queue
    {
        private readonly ConcurrentQueue<Action> work = new();
        internal int Count => work.Count;
        internal void Post(Action action) => work.Enqueue(action);
        internal void Next() { if (work.TryDequeue(out var action)) action(); }
        internal void Drain() { while (work.TryDequeue(out var action)) action(); }
    }
    private sealed class Source
    {
        private event Action? Changed;
        internal int Count => Changed?.GetInvocationList().Length ?? 0;
        internal IDisposable Subscribe(Action callback)
        {
            Changed += callback;
            return new Cleanup(() => Changed -= callback);
        }
        internal void Raise() => Changed?.Invoke();
    }
    private sealed class Cleanup(Action action) : IDisposable
    {
        private Action? dispose = action;
        public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
    }
    public sealed class NativeSource : INotifyPropertyChanged
    {
        public delegate void SelectionHandler(NativeSource source);
        public delegate void OperationHandler(object sender, EventArgs args);
        public delegate void ConnectionHandler(EventArgs args);
        public event SelectionHandler? SelectionChanged;
        public event OperationHandler? Operation;
        public static event ConnectionHandler? Connection;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action FailingRemoval { add { } remove { throw new InvalidOperationException("detach failed"); } }
        public int SelectionHandlers => SelectionChanged?.GetInvocationList().Length ?? 0;
        public void Select() => SelectionChanged?.Invoke(this);
        public void End() => Operation?.Invoke(this, EventArgs.Empty);
        public static void Connect() => Connection?.Invoke(EventArgs.Empty);
        public void Property(string? name) => PropertyChanged?.Invoke(this, new(name));
    }
}
