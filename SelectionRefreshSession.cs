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
            try
            {
                var acquired = subscribe(RequestRefresh);
                if (disposed) acquired.Dispose();
                else subscription = acquired;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    // Event arguments are deliberately not retained. The callback reads current state.
    internal void RequestRefresh()
    {
        lock (gate)
        {
            if (!started || disposed || pending) return;
            pending = true;
            var ticket = ++generation;
            try { post!(() => Drain(ticket)); }
            catch (Exception ex)
            {
                pending = false;
                Report(ex);
            }
        }
    }

    internal void RefreshNow()
    {
        lock (gate)
        {
            if (!started || disposed) return;
            pending = false;
            generation++; // An earlier queued callback must not repaint after manual Read.
            Refresh();
        }
    }

    private void Drain(long ticket)
    {
        lock (gate)
        {
            if (disposed || !pending || generation != ticket) return;
            pending = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        try { refresh!(); }
        catch (Exception ex) { Report(ex); }
    }

    private void Report(Exception error)
    {
        // A diagnostic sink must never throw back into a native notification.
        try { reportError?.Invoke(error); }
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
