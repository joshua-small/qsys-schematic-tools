using System.Windows;
using System.Windows.Input;

namespace Designer.Modules.SelectionProbe;

internal enum PackingFocus { Blocked, Schematic, OtherCanvas }
internal sealed record PackingCommandContext(PackingFocus Focus, object? Canvas = null);

/// <summary>Intercept only the reviewed commands before their native Executed handlers.</summary>
internal sealed class PackingCommandRouter : IDisposable
{
    private readonly UIElement root;
    private readonly IReadOnlyDictionary<RoutedCommand, PackAlignment> commands;
    private readonly Func<PackingCommandContext> context;
    private readonly Func<object, bool> canPack;
    private readonly Action<PackAlignment, object, string> pack;
    private readonly Action<Exception> error;

    internal PackingCommandRouter(UIElement root, IReadOnlyDictionary<RoutedCommand, PackAlignment> commands,
        Func<PackingCommandContext> context, Func<object, bool> canPack,
        Action<PackAlignment, object, string> pack, Action<Exception> error)
    {
        this.root = root;
        this.commands = commands;
        this.context = context;
        this.canPack = canPack;
        this.pack = pack;
        this.error = error;
        CommandManager.AddPreviewCanExecuteHandler(root, CanExecute);
        CommandManager.AddPreviewExecutedHandler(root, Execute);
    }

    private void CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command is not RoutedCommand command || !commands.ContainsKey(command)) return;
        try
        {
            var current = context();
            if (current.Focus == PackingFocus.OtherCanvas) return; // Preserve native UCI/control-panel packing.
            e.Handled = true;
            e.CanExecute = current.Focus == PackingFocus.Schematic && current.Canvas is not null && canPack(current.Canvas);
            e.ContinueRouting = !e.CanExecute; // Do not consume editor input when packing is unavailable.
        }
        catch (Exception)
        {
            // CanExecute is queried frequently; never fall through to native packing on a read failure.
            e.Handled = true;
            e.CanExecute = false;
            e.ContinueRouting = true;
        }
    }

    private void Execute(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command is not RoutedCommand command || !commands.TryGetValue(command, out var alignment)) return;
        try
        {
            var current = context();
            if (current.Focus == PackingFocus.OtherCanvas) return;
            // Mark handled before validation or mutation: rejection must never run the native action.
            e.Handled = true;
            if (current.Focus == PackingFocus.Schematic && current.Canvas is not null && canPack(current.Canvas))
                pack(alignment, current.Canvas, command.Name);
        }
        catch (Exception exception)
        {
            e.Handled = true;
            error(exception);
        }
    }

    public void Dispose()
    {
        CommandManager.RemovePreviewCanExecuteHandler(root, CanExecute);
        CommandManager.RemovePreviewExecutedHandler(root, Execute);
    }
}
