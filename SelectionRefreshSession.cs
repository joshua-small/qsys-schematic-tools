namespace Designer.Modules.SelectionProbe;

/// <summary>Owns one report window's notifications and deferred refresh work.</summary>
internal sealed class SelectionRefreshSession : IDisposable
{
    private readonly object gate = new();
    private Action<Action>? post;
    private Action? refresh;
    private Action<Exception>? reportError;
    private IDisposable? subscription;
    private bool started;
    private bool disposed;
    private bool pending;
    private bool queued;
    private bool reading;
    private long generation;

    internal SelectionRefreshSession(Action<Action> post, Action refresh, Action<Exception> reportError)
    {
        this.post = post;
        this.refresh = refresh;
        this.reportError = reportError;
    }

    internal void Start(Func<Action, IDisposable> subscribe)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) return;
            started = true;
        }
        IDisposable acquired;
        try { acquired = subscribe(RequestRefresh); }
        catch { Dispose(); throw; }
        lock (gate)
        {
            if (!disposed) { subscription = acquired; return; }
        }
        acquired.Dispose();
    }

    // Event arguments are deliberately not retained. The callback reads current state.
    internal void RequestRefresh()
    {
        lock (gate)
        {
            if (!started || disposed) return;
            pending = true;
        }
        QueuePending();
    }

    private void QueuePending()
    {
        Action<Action> schedule;
        long ticket;
        lock (gate)
        {
            if (disposed || !pending || reading || queued) return;
            queued = true;
            ticket = ++generation;
            schedule = post!;
        }
        // Host callbacks, dispatcher posting and capture must never hold the gate.
        try { schedule(() => Drain(ticket)); }
        catch (Exception ex)
        {
            lock (gate)
            {
                if (disposed || !queued || generation != ticket) return;
                queued = false;
                generation++; // Ignore a callback if the failed poster queued it first.
            }
            Report(ex);
        }
    }

    internal void RefreshNow()
    {
        Action read;
        lock (gate)
        {
            if (!started || disposed) return;
            queued = false;
            generation++; // An earlier queued callback must not repaint after manual Read.
            pending = true;
            if (reading) return;
            pending = false;
            reading = true;
            read = refresh!;
        }
        Refresh(read);
    }

    private void Drain(long ticket)
    {
        Action read;
        lock (gate)
        {
            if (disposed || !queued || generation != ticket) return;
            queued = false;
            if (!pending || reading) return;
            pending = false;
            reading = true;
            read = refresh!;
        }
        Refresh(read);
    }

    private void Refresh(Action read)
    {
        try { read(); }
        catch (Exception ex) { Report(ex); }
        finally
        {
            lock (gate) { reading = false; }
            QueuePending();
        }
    }

    private void Report(Exception error)
    {
        // A diagnostic sink must never throw back into a native notification.
        Action<Exception>? report;
        lock (gate) { report = reportError; }
        try { report?.Invoke(error); }
        catch { }
    }

    public void Dispose()
    {
        IDisposable? old;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            pending = false;
            queued = false;
            generation++;
            old = subscription;
            subscription = null;
            refresh = null;
            post = null;
            reportError = null;
        }
        old?.Dispose();
    }
}
