using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Designer.Modules.SelectionProbe;

/// <summary>Version-scoped host calls inspected in Designer 10.5; no file or Core deployment API.</summary>
internal static class SelectionMover
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
    private const int MaximumObjects = 256;
    private const double DeltaY = 20;

    private sealed record Target(object Model, PropertyInfo Position, Point Before, Point After);

    internal static Type HostType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(name, throwOnError: false)).FirstOrDefault(t => t is not null)
        ?? throw new MissingMemberException($"The loaded host type {name} was not found.");

    private static object Manager => HostType("QSC.QSys.App").GetField("Manager", Static)?.GetValue(null)
        ?? throw new MissingFieldException("QSC.QSys.App", "Manager");

    internal static object RequiredProperty(object instance, string name) =>
        instance.GetType().GetProperty(name, Instance)?.GetValue(instance)
        ?? throw new MissingMemberException(instance.GetType().FullName, name);

    private static MethodInfo Method(object instance, string name, params Type[] arguments) =>
        instance.GetType().GetMethod(name, Instance, binder: null, arguments, modifiers: null)
        ?? throw new MissingMethodException(instance.GetType().FullName, name);

    internal static object GlobalSelection => HostType("QSC.QSys.Util.Selection").GetField("Global", Static)?.GetValue(null)
        ?? throw new MissingFieldException("QSC.QSys.Util.Selection", "Global");

    internal static bool IsSchematicCanvas(object graphicCanvas)
    {
        var root = RequiredProperty(graphicCanvas, "Root");
        var document = RequiredProperty(Manager, "Document");
        var pending = new Stack<object>(((IEnumerable)RequiredProperty(document, "Pages")).Cast<object>());
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var containerType = HostType("QSC.QSys.Design.Container");
        var channelGroupType = HostType("QSC.QSys.Design.ChannelGroup");
        while (pending.TryPop(out var page))
        {
            if (!visited.Add(page)) continue;
            if (ReferenceEquals(page, root)) return true;
            foreach (var child in ((IEnumerable)RequiredProperty(page, "AllChildren")).Cast<object>())
            {
                if (!containerType.IsInstanceOfType(child) && !channelGroupType.IsInstanceOfType(child)) continue;
                // Only the first canvas carries the nested schematic. Further Container
                // canvases are user control panels and must retain native packing.
                var schematic = ((IEnumerable)RequiredProperty(child, "Canvases")).Cast<object>().FirstOrDefault();
                if (schematic is not null) pending.Push(schematic);
            }
        }
        return false;
    }

    internal static object[] SelectedModels()
    {
        // Matches the host alignment command: graphic objects -> models, excluding locked objects/pins.
        var selected = RequiredProperty(GlobalSelection, "UnlockedObjects") as IEnumerable
            ?? throw new InvalidOperationException("UnlockedObjects is not enumerable.");
        var objects = selected.Cast<object>().Distinct(ReferenceEqualityComparer.Instance)
            .Take(MaximumObjects + 1).ToArray();
        if (objects.Length > MaximumObjects)
            throw new InvalidOperationException($"Select at most {MaximumObjects} objects for this experiment. Nothing moved.");
        return objects;
    }

    private static object Describe(object model, Point position) => new
    {
        type = model.GetType().FullName,
        sessionIdentity = RuntimeHelpers.GetHashCode(model),
        position = new { x = position.X, y = position.Y }
    };

    internal static object[] ReadObjects() => SelectedModels().Select(model =>
        Describe(model, (Point)RequiredProperty(model, "Position"))).ToArray();

    internal static object ReadHostState()
    {
        var connection = HostType("QSC.QSys.ControlConnection.Manager");
        var status = connection.GetProperty("Status", Static)?.GetValue(null);
        bool? connected = status?.GetType().GetProperty("IsConnected", Instance)?.GetValue(status) as bool?;
        bool? emulator = connection.GetProperty("IsEmulator", Static)?.GetValue(null) as bool?;
        var manager = Manager;
        return new
        {
            mode = emulator == true ? "Emulate" : connected == true ? "Run" :
                emulator == false && connected == false ? "Design" : "Unknown",
            connected, emulator,
            dirty = RequiredProperty(manager, "Dirty"),
            undoable = RequiredProperty(manager, "Undoable"),
            redoable = RequiredProperty(manager, "Redoable")
        };
    }

    private static bool SetterPreserves(double value) => double.IsFinite(value)
        && value >= 0 && value < int.MaxValue && Math.Truncate(value + 0.5) == value;

    internal static string MoveDown20(Func<string, object, string> receipt)
    {
        Application.Current.Dispatcher.VerifyAccess();
        var models = SelectedModels();
        if (models.Length == 0)
            return "No unlocked objects selected. Select schematic objects, then try again.\n\n"
                + receipt("move-noop", new { requestedDeltaY = DeltaY, hostState = ReadHostState() });

        return ApplyPositions(models.Select(model =>
        {
            var before = (Point)RequiredProperty(model, "Position");
            return (model, new Point(before.X, before.Y + DeltaY));
        }).ToArray(), "move", new { requestedDeltaY = DeltaY },
            $"Moved {models.Length} object(s) down 20 schematic units.", receipt);
    }

    internal static string ApplyPositions(IReadOnlyList<(object Model, Point After)> requested,
        string kind, object parameters, string successMessage, Func<string, object, string> receipt)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if (requested.Count == 0 || requested.Count > MaximumObjects
            || requested.Select(t => t.Model).Distinct(ReferenceEqualityComparer.Instance).Count() != requested.Count)
            throw new InvalidOperationException("Invalid movement selection. Nothing moved.");
        var modelType = HostType("QSC.QSys.Design.Object");
        // Validate every target before the first setter; never partially truncate the selection.
        var targets = requested.Select(request =>
        {
            var model = request.Model;
            if (!modelType.IsInstanceOfType(model) || RequiredProperty(model, "IsAttached") is not true
                || RequiredProperty(model, "IsLocked") is not false)
                throw new InvalidOperationException("Selection contains an unsupported, detached, or locked object. Nothing moved.");
            var position = modelType.GetProperty("Position", Instance);
            if (position?.PropertyType != typeof(Point) || position.SetMethod?.IsPublic != true)
                throw new MissingMemberException("The reviewed writable Point Position property is unavailable.");
            var before = (Point)position.GetValue(model)!;
            var after = request.After;
            // The vendor setter rounds coordinates. Reject cases where it would alter another coordinate.
            if (!SetterPreserves(before.X) || !SetterPreserves(before.Y)
                || !SetterPreserves(after.X) || !SetterPreserves(after.Y))
                throw new InvalidOperationException("This experiment requires nonnegative whole-unit positions in range. Nothing moved.");
            return new Target(model, position, before, after);
        }).ToArray();

        if (targets.All(t => t.Before == t.After))
            return "The selection is already at the requested positions. Nothing moved.\n\n"
                + receipt(kind + "-noop", new { parameters, hostState = ReadHostState() });

        var manager = Manager;
        foreach (var state in new[] { "IsLoading", "IsInUndo", "IsInEndOperation" })
            if (RequiredProperty(manager, state) is true)
                throw new InvalidOperationException($"Designer is busy ({state}). Nothing moved.");
        var start = Method(manager, "StartOperation");
        var finish = Method(manager, "EndOperation", typeof(Guid));
        var cancel = Method(manager, "CancelOperation");
        var beforeState = ReadHostState();
        var beforeObjects = targets.Select(t => Describe(t.Model, t.Before)).ToArray();
        // If evidence cannot be written, fail before changing the editor model.
        receipt(kind + "-start", new { parameters, hostState = beforeState, objects = beforeObjects });
        var operation = (Guid)start.Invoke(manager, null)!;
        if (operation == Guid.Empty)
            throw new InvalidOperationException("Another Designer operation is active. Nothing moved.");

        var finishing = false;
        try
        {
            foreach (var target in targets) target.Position.SetValue(target.Model, target.After);
            foreach (var target in targets)
                if ((Point)target.Position.GetValue(target.Model)! != target.After)
                    throw new InvalidOperationException("A host position did not match the requested move.");
            finishing = true;
            finish.Invoke(manager, new object[] { operation });
        }
        catch (Exception error)
        {
            string rollback;
            if (finishing)
                rollback = "EndOperation failed; commit state is uncertain. No further mutation attempted.";
            else
            {
                try
                {
                    cancel.Invoke(manager, null);
                    finish.Invoke(manager, new object[] { operation });
                    rollback = targets.All(t => (Point)t.Position.GetValue(t.Model)! == t.Before)
                        ? "Original positions restored through host CancelOperation."
                        : "Host cancellation did not restore every original position.";
                }
                catch (Exception cancelError) { rollback = "Cancellation failed: " + cancelError; }
            }
            receipt(kind + "-failed", new { error = error.ToString(), rollback, beforeObjects, operation });
            throw new InvalidOperationException(rollback, error);
        }

        // Record final positions after EndOperation, so its notifications are included in the observation.
        var afterObjects = targets.Select(t => Describe(t.Model, (Point)t.Position.GetValue(t.Model)!)).ToArray();
        var exact = targets.All(t => (Point)t.Position.GetValue(t.Model)! == t.After);
        var data = new
        {
            parameters, movedCount = targets.Count(t => t.Before != t.After), selectedCount = targets.Length,
            exactPositionsAfterEndOperation = exact,
            beforeState, afterState = ReadHostState(), beforeObjects, afterObjects, operation
        };
        try
        {
            var result = receipt(kind + "-completed", data);
            return (exact ? successMessage
                : "The host changed a position during completion; inspect the details below.") + "\n\n" + result;
        }
        catch (Exception evidenceError)
        {
            // Never report a completed edit as though it never happened just because log I/O failed.
            return $"The move completed for {targets.Length} object(s), but its result log could not be saved.\n"
                + evidenceError;
        }
    }
}
