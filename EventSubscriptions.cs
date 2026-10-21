using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Designer.Modules.SelectionProbe;

/// <summary>Checks notification signatures and retains the exact delegates for removal.</summary>
internal sealed class EventSubscriptions : IDisposable
{
    private readonly List<Action> remove = new();
    private bool disposed;

    internal void Add(Type type, object? source, string name, Action callback, params Type[] parameters)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var flags = BindingFlags.Public | (source is null ? BindingFlags.Static : BindingFlags.Instance);
        var info = type.GetEvent(name, flags) ?? throw new MissingMemberException(type.FullName, name);
        var invoke = info.EventHandlerType?.GetMethod("Invoke");
        if (invoke is null || invoke.ReturnType != typeof(void) ||
            !invoke.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters) ||
            info.GetAddMethod() is null || info.GetRemoveMethod() is null)
            throw new InvalidOperationException($"Unsupported notification signature: {type.FullName}.{name}.");
        var args = parameters.Select(t => Expression.Parameter(t)).ToArray();
        var handler = Expression.Lambda(info.EventHandlerType!,
            Expression.Invoke(Expression.Constant(callback)), args).Compile();
        info.AddEventHandler(source, handler);
        remove.Add(() => info.RemoveEventHandler(source, handler));
    }

    internal void AddProperties(INotifyPropertyChanged source, Action callback, params string[] names)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var watched = new HashSet<string>(names, StringComparer.Ordinal);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName) || watched.Contains(args.PropertyName)) callback();
        };
        source.PropertyChanged += handler;
        remove.Add(() => source.PropertyChanged -= handler);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>();
        for (var i = remove.Count - 1; i >= 0; i--)
        {
            try { remove[i](); }
            catch (Exception ex) { failures.Add(ex); }
        }
        remove.Clear();
        if (failures.Count != 0) throw new AggregateException("Could not detach all notifications.", failures);
    }
}
