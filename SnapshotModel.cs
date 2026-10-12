using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;

namespace Designer.Modules.SelectionProbe;

internal static class SnapshotHost
{
    internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static Type Type(string name) => SelectionMover.HostType("QSC.QSys." + name);
    internal static object? Get(object? owner, string name)
    {
        if (owner is null) return null;
        Type type = owner as Type ?? owner.GetType();
        return type.GetProperty(name, Flags)?.GetValue(owner is Type ? null : owner)
            ?? type.GetField(name, Flags)?.GetValue(owner is Type ? null : owner);
    }
    internal static object? Call(object owner, string name, params object?[] args)
    {
        Type type = owner as Type ?? owner.GetType();
        var method = type.GetMethods(Flags).Single(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] is null || p.ParameterType.IsInstanceOfType(args[i])).All(b => b));
        return method.Invoke(owner is Type ? null : owner, args);
    }
    internal static object[] Items(object? items) => items is IEnumerable list ? list.Cast<object>().ToArray() : Array.Empty<object>();
    internal static string Text(object? owner, string name) => Get(owner, name)?.ToString() ?? "";
    internal static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name, Flags)!.SetValue(owner, value);
    internal static object? Manager => Get(Type("App"), "Manager");
    internal static object? Document => Get(Manager, "Document");
    internal static object[] Descriptors(object component)
    {
        var method = component.GetType().GetMethods(Flags).Single(m => m.Name == "GetControls" && m.GetParameters().Length == 1);
        var hint = Enum.Parse(method.GetParameters()[0].ParameterType, "SnapshotControls");
        return Items(method.Invoke(component, new object[] { hint }));
    }
    internal static string Path(object component) => Call(Type("Util.Find"), "PageNameComponentIsOn", component)?.ToString() ?? "";
    internal static object? KeyId(object? style) => Get(Get(style, "Value"), "Id");
}

internal sealed record SnapshotKey(object Id, string Control);
internal sealed record SnapshotRow(object BankObject, object ComponentObject, object? Descriptor, SnapshotKey Key,
    string Bank, string Component, string Path, string Control, string ControlId, string IncludedBy, string SnapshotId);

/// <summary>Read-only expansion of bank capture scope. This never reads or recalls saved preset values.</summary>
internal sealed class SnapshotModel : IDisposable
{
    private readonly Action changed;
    private readonly List<Action> unsubscribe = new();
    private readonly HashSet<object> watched = new(ReferenceEqualityComparer.Instance);
    internal SnapshotRow[] Rows { get; private set; } = Array.Empty<SnapshotRow>();
    internal string[] Warnings { get; private set; } = Array.Empty<string>();
    internal SnapshotModel(Action changed) => this.changed = changed;

    private void Watch(object? item)
    {
        if (item is null || !watched.Add(item)) return;
        if (item is INotifyCollectionChanged collection)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => changed();
            collection.CollectionChanged += handler; unsubscribe.Add(() => collection.CollectionChanged -= handler);
        }
        if (item is INotifyPropertyChanged properties)
        {
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName is not ("IsSelected" or "IsExpanded" or "Match" or "State" or "SnapState")) changed();
            };
            properties.PropertyChanged += handler; unsubscribe.Add(() => properties.PropertyChanged -= handler);
        }
    }

    internal void Refresh(object? document)
    {
        Dispose();
        var rows = new List<SnapshotRow>(); var warnings = new List<string>();
        var banks = SnapshotHost.Get(document, "Snapshots"); Watch(banks);
        var descriptors = new Dictionary<object, object[]>(ReferenceEqualityComparer.Instance);
        foreach (var bank in SnapshotHost.Items(banks))
        {
            if (SnapshotHost.Type("Design.Snapshot.GlobalBank").IsInstanceOfType(bank)) continue;
            Watch(bank); var members = SnapshotHost.Get(bank, "Components"); Watch(members);
            foreach (var member in SnapshotHost.Items(members))
            {
                Watch(member); var controls = SnapshotHost.Get(member, "Controls"); Watch(controls);
                var explicitControls = SnapshotHost.Items(controls); foreach (var control in explicitControls) Watch(control);
                var component = SnapshotHost.Get(member, "DesignComponent");
                if (component is null) { warnings.Add(SnapshotHost.Text(bank, "Name") + ": missing component"); continue; }
                Watch(component); var properties = SnapshotHost.Get(component, "Properties"); Watch(properties);
                foreach (var property in SnapshotHost.Items(properties)) Watch(property);
                try
                {
                    if (!descriptors.TryGetValue(component, out var list)) descriptors[component] = list = SnapshotHost.Descriptors(component);
                    var id = SnapshotHost.Get(component, "Id")!;
                    bool inclusive = SnapshotHost.Get(member, "IsInclusive") is true;
                    foreach (var descriptor in list)
                    {
                        string name = SnapshotHost.Text(descriptor, "ControlId"), proxy = SnapshotHost.Text(descriptor, "SnapshotProxy");
                        if (SnapshotHost.Get(descriptor, "IsPresetable") is not true && proxy.Length == 0) continue;
                        string snapshotId = proxy.Length == 0 ? name : proxy;
                        var exact = explicitControls.FirstOrDefault(c => SnapshotHost.Text(c, "CodeName") == name);
                        bool shared = explicitControls.Any(c => SnapshotHost.Text(c, "SnapshotId") == snapshotId);
                        if (!inclusive && exact is null && !shared) continue;
                        rows.Add(new(bank, component, descriptor, new(id, name), SnapshotHost.Text(bank, "Name"),
                            string.IsNullOrWhiteSpace(SnapshotHost.Text(component, "UserLabel")) ? SnapshotHost.Text(component, "Label") : SnapshotHost.Text(component, "UserLabel"), SnapshotHost.Path(component),
                            SnapshotHost.Text(descriptor, "PrettyName").Replace('~', ' '), name,
                            inclusive ? "Entire component" : exact is not null ? "Individual control" : "Shared snapshot control", snapshotId));
                    }
                    foreach (var control in explicitControls)
                        if (!list.Any(d => SnapshotHost.Text(d, "ControlId") == SnapshotHost.Text(control, "CodeName")))
                            warnings.Add($"{SnapshotHost.Text(bank, "Name")}: unavailable control {SnapshotHost.Text(control, "CodeName")}");
                }
                catch (Exception ex) { warnings.Add($"{SnapshotHost.Text(bank, "Name")}: {ex.GetBaseException().Message}"); }
            }
        }
        Rows = rows.DistinctBy(r => (r.BankObject, r.Key)).OrderBy(r => r.Bank).ThenBy(r => r.Path).ThenBy(r => r.Component).ThenBy(r => r.Control).ToArray();
        Warnings = warnings.Distinct().ToArray();
    }

    public void Dispose()
    {
        foreach (var action in unsubscribe) action(); unsubscribe.Clear(); watched.Clear();
        Rows = Array.Empty<SnapshotRow>(); Warnings = Array.Empty<string>();
    }
}
