using System.Collections;
using System.IO;

namespace Designer.Modules.SelectionProbe;

internal sealed record SnapshotReadContext(object Document, object? Connection, string CompileId, bool Connected, bool Emulator)
{
    internal static SnapshotReadContext Capture() => new(SnapshotHost.Document ?? throw new InvalidOperationException("Open a design first."),
        SnapshotHost.Get(SnapshotHost.Type("ControlConnection.Manager"), "CurrentConnection"), SnapshotHost.Text(SnapshotHost.Type("App"), "CompileID"),
        SnapshotHost.Get(SnapshotHost.Get(SnapshotHost.Type("ControlConnection.Manager"), "Status"), "IsConnected") is true,
        SnapshotHost.Get(SnapshotHost.Type("ControlConnection.Manager"), "IsEmulator") is true);
    internal bool IsCurrent()
    {
        var now = Capture();
        return ReferenceEquals(Document, now.Document) && ReferenceEquals(Connection, now.Connection) && CompileId == now.CompileId && Connected == now.Connected && Emulator == now.Emulator;
    }
    internal string CurrentLabel => Connected ? Emulator ? "Current · Emulate" : "Current · Core" : "Current · saved/cached";
    internal string SavedLabel => Connected ? Emulator ? "Emulate snapshot files" : "Connected Core snapshot files" : "Settings embedded in this design";
}

internal sealed record SnapshotFile(string Name, Dictionary<string, SnapshotDatum>? Values, string? Error);

/// <summary>Only existing files, cached state and native read subscriptions. No flush, save, load or control setters.</summary>
internal sealed class SnapshotComparisonSource : IDisposable
{
    internal SnapshotReadContext Context { get; }
    internal Dictionary<string, SnapshotFile> Files { get; } = new(StringComparer.Ordinal);
    private object? group;
    private readonly Dictionary<SnapshotKey, object> live = new();
    private readonly string prefix;
    private bool disposed;
    internal SnapshotComparisonSource(SnapshotReadContext context, IEnumerable<SnapshotRow> rows, string bankFileName)
    {
        Context = context; prefix = "controlset." + bankFileName + ".snapshot.";
        if (!context.Connected) return;
        if (context.Connection is null) throw new InvalidOperationException("QDS is still connecting. Refresh after the connection is ready.");
        group = Activator.CreateInstance(SnapshotHost.Type("ControlConnection.ChangeGroup"))!;
        try
        {
            foreach (var row in rows.DistinctBy(r => r.Key))
                live[row.Key] = SnapshotHost.Call(group, "AddControl", row.Key.Id, row.ControlId)!;
        }
        catch { Dispose(); throw; }
    }
    internal async Task ReadAsync()
    {
        object[] files;
        if (!Context.Connected) files = SnapshotHost.Items(SnapshotHost.Get(Context.Document, "CoreFiles"));
        else if (Context.Emulator)
        {
            // This lower-level native reader does not perform the flush in Async.Disconnect.GetSettings.
            var task = (Task)SnapshotHost.Call(SnapshotHost.Type("ControlConnection.Manager"), "GetSettings", null, SnapshotHost.Get(SnapshotHost.Type("App"), "TempPath"))!;
            await task; files = SnapshotHost.Items(SnapshotHost.Get(task, "Result"));
        }
        else
        {
            var manager = SnapshotHost.Type("ControlConnection.Manager");
            var providerType = SnapshotHost.Type("Deployment.RemoteSettingsProviderSystemController");
            object provider; object? communication = null;
            if (SnapshotHost.Get(manager, "IsReflect") is true)
                provider = SnapshotHost.Call(providerType, "CreateReflectSettingsProvider", Context.CompileId)!;
            else
            {
                communication = SnapshotHost.Get(SnapshotHost.Get(manager, "Status"), "CurrentCommMethod")
                    ?? throw new InvalidOperationException("The connected Core endpoint is unavailable.");
                var endpoint = SnapshotHost.Call(communication, "MakeUri", "")!;
                var session = SnapshotHost.Call(SnapshotHost.Type("Deployment.ApiSessionProvider"), "APISessionFromEndpoint", SnapshotHost.Text(endpoint, "AbsoluteUri"), false)!;
                var auth = (Task)SnapshotHost.Call(session, "IsAuthenticated")!; await auth;
                if (SnapshotHost.Get(auth, "Result") is not true) throw new InvalidOperationException("Sign in to the Core through QDS, then refresh this comparison.");
                var creation = (Task)SnapshotHost.Call(providerType, "Create", communication, Context.CompileId)!; await creation;
                provider = SnapshotHost.Get(creation, "Result")!;
            }
            var download = (Task)SnapshotHost.Call(provider, "GetRemoteSettings", communication, null, Context.CompileId)!;
            await download; files = SnapshotHost.Items(SnapshotHost.Get(download, "Result"));
        }
        if (disposed || !Context.IsCurrent()) throw new InvalidOperationException("The design or connection changed while values were being read. Refresh to try again.");
        long total = 0, characters = 0;
        foreach (var file in files)
        {
            string name = SnapshotHost.Text(file, "FileName");
            if (!name.StartsWith(prefix, StringComparison.Ordinal) && name != "controlset.global.snapshot.0") continue;
            if (SnapshotHost.Get(file, "Contents") is not byte[] bytes) throw new InvalidDataException("Snapshot contents are unavailable.");
            total += bytes.Length; if (total > 64 * 1024 * 1024) throw new InvalidDataException("Snapshot files exceed the comparison size limit.");
            SnapshotFile parsed;
            try { parsed = new(name, SnapshotPayload.Read(bytes), null); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
            { parsed = new(name, null, error.Message); }
            if (parsed.Values is not null) characters += parsed.Values.Sum(v => (long)v.Key.Length + (v.Value.Text?.Length ?? 0));
            if (characters > 32 * 1024 * 1024) throw new InvalidDataException("Decoded snapshot text exceeds the comparison size limit.");
            if (!Files.TryAdd(name, parsed)) throw new InvalidDataException("Duplicate snapshot filename in settings.");
        }
    }
    internal (SnapshotDatum? Value, string Detail) Current(SnapshotRow row)
    {
        if (Context.Connected)
        {
            if (!live.TryGetValue(row.Key, out var info)) return (null, "Not subscribed.");
            if (SnapshotHost.Get(info, "Values") is not null || SnapshotHost.Get(info, "Strings") is not null) return (null, "Vector feedback is not supported by this comparison.");
            string? text = SnapshotHost.Get(info, "String") as string;
            if (text is null) return (null, "Waiting for current feedback from QDS.");
            bool isText = SnapshotHost.Text(row.Descriptor, "ControlType") == "String";
            return (isText ? new(null, text, true) : new(Convert.ToSingle(SnapshotHost.Get(info, "Value")), null, false), "QDS readout: " + text);
        }
        // Cached component values are the last native feedback, when present. The
        // embedded Global zero file is the saved working-state fallback, never live.
        if (SnapshotHost.Get(row.ComponentObject, "CachedControlValues") is IDictionary cache && cache.Contains(row.ControlId))
        {
            var value = cache[row.ControlId]; bool text = SnapshotHost.Text(row.Descriptor, "ControlType") == "String";
            return (text ? new(null, SnapshotHost.Get(value, "String") as string, true) : new(Convert.ToSingle(SnapshotHost.Get(value, "Value")), null, false), "Last QDS cached value; not live. " + SnapshotHost.Text(value, "String"));
        }
        if (Files.TryGetValue("controlset.global.snapshot.0", out var working) && working.Values?.TryGetValue(Key(row, row.ControlId), out var saved) is true)
            return (saved, "Working state embedded in the design; not live.");
        return (null, "No saved or cached current value is available in Design mode.");
    }
    internal static string Key(SnapshotRow row, string control) => row.Key.Id + "_" + control;
    public void Dispose()
    {
        disposed = true;
        if (group is not null) { SnapshotHost.Call(group, "DestoryChangeGroup"); group = null; }
        live.Clear();
    }
}
