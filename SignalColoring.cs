using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Designer.Modules.SelectionProbe;

internal enum SignalColorScope { Selection, EntireDesign }

internal static class SignalColoring
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const int Limit = 4096;
    internal sealed record PinState(object Pin, object Provider, Color Before, SignalPinEvidence Evidence,
        bool Attached, bool Locked);
    internal sealed record Group(string Name, PinState[] Pins, SignalColorChoice Choice, int OutsideSelection)
    {
        public string Role => Choice.Role;
        public string Current => string.Join(", ", Pins.Select(p => Hex(p.Before)).Distinct());
        public string Proposed => Choice.Color ?? "Unchanged";
        public int AffectedPins => Pins.Length;
        public string Reason => Choice.Reason;
        internal bool Changes => Choice.Color is { } c && Pins.Any(p => Hex(p.Before) != c);
    }
    internal sealed record Preview(object Manager, SignalColorScope Scope, object[] Selection, Group[] Groups,
        Dictionary<SignalRole, string> Palette, SignalRole? ForcedRole, bool DefaultColorsOnly);

    private static object Required(object o, string name) => SelectionMover.RequiredProperty(o, name);
    private static object? Property(object o, string name) => o.GetType().GetProperty(name, PublicInstance)?.GetValue(o);
    private static object Field(object o, string name) => o.GetType().GetField(name, PublicInstance)?.GetValue(o)
        ?? throw new MissingFieldException(o.GetType().FullName, name);
    private static string Label(object pin)
    {
        var property = pin.GetType().GetProperty("Label", PublicInstance)
            ?? throw new MissingMemberException(pin.GetType().FullName, "Label");
        return property.GetValue(pin) as string ?? "";
    }
    private static object Manager => SelectionMover.HostType("QSC.QSys.App").GetField("Manager", PublicStatic)?.GetValue(null)
        ?? throw new MissingFieldException("QSC.QSys.App", "Manager");
    internal static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static Color Parse(string c) => (Color)ColorConverter.ConvertFromString(SignalColorRules.Normalize(c));
    private static object[] Bounded(IEnumerable values) => CheckBound(values.Cast<object>().Take(Limit + 1).ToArray());
    private static object[] CheckBound(object[] values) => values.Length > Limit
        ? throw new InvalidOperationException($"This preview exceeds {Limit} pin/graphic records. Nothing changed; use a smaller selection.") : values;
    private static bool SameSet(IEnumerable<object> a, IEnumerable<object> b) =>
        new HashSet<object>(a, ReferenceEqualityComparer.Instance).SetEquals(b);

    private static HashSet<object> AssociatedPins(object info)
    {
        var associated = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var field in new[] { "Tx", "Rx" })
        {
            var member = info.GetType().GetField(field, PublicInstance)
                ?? throw new MissingFieldException(info.GetType().FullName, field);
            if (member.GetValue(info) is IEnumerable pins)
                foreach (var p in Bounded(pins)) associated.Add(p);
        }
        CheckBound(associated.ToArray());
        return associated;
    }

    private static HashSet<object> EntireDesignPins()
    {
        // The inspected getter merges the host's output-name and input-name maps,
        // including receiver-only names, independently of the current canvas.
        var all = SelectionMover.HostType("QSC.QSys.Design.SignalNameManager")
            .GetProperty("AllInfos", PublicStatic)?.GetValue(null) as IDictionary
            ?? throw new MissingMemberException("SignalNameManager.AllInfos");
        if (all.Count > 256)
            throw new InvalidOperationException("This design exceeds the 256-group preview limit. Nothing changed; use selected groups instead.");
        var pins = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (DictionaryEntry entry in all)
        {
            if (entry.Key is not string name || string.IsNullOrWhiteSpace(name) || entry.Value is null)
                throw new InvalidOperationException("The design's Named Signal collection contains an invalid group. Nothing changed.");
            var associated = AssociatedPins(entry.Value);
            if (associated.Count == 0 || associated.Any(p => !string.Equals(Label(p), name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The design's Named Signal collection contains inconsistent pins. Nothing changed.");
            pins.UnionWith(associated);
            CheckBound(pins.ToArray());
        }
        return pins;
    }

    internal static Preview Read(IReadOnlyDictionary<SignalRole, string> palette, SignalRole? forced,
        SignalColorScope scope = SignalColorScope.Selection, bool defaultColorsOnly = false)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if (!HostInstallation.Enabled) throw new InvalidOperationException("Schematic Tools is not enabled for this host.");
        var manager = Manager;
        var raw = scope == SignalColorScope.Selection ? Bounded((IEnumerable)SelectionMover.GlobalSelection) : Array.Empty<object>();
        var graphicPin = SelectionMover.HostType("QSC.QSys.Graphic.Pin");
        var pinLabel = SelectionMover.HostType("QSC.QSys.Graphic.Pins.PinLabel");
        var graphicComponent = SelectionMover.HostType("QSC.QSys.Graphic.Component");
        var selected = scope == SignalColorScope.EntireDesign ? EntireDesignPins() : new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var g in raw)
        {
            if (graphicPin.IsInstanceOfType(g)) selected.Add(Required(g, "InnerPin"));
            else if (pinLabel.IsInstanceOfType(g)) selected.Add(Required(Required(g, "Pin"), "InnerPin"));
            else if (graphicComponent.IsInstanceOfType(g))
                foreach (var p in Bounded((IEnumerable)Required(Required(g, "obj"), "Pins"))) selected.Add(p);
            else throw new InvalidOperationException("Select Named Signal tags, pins, or schematic components. Deselect other objects before previewing colors.");
        }
        CheckBound(selected.ToArray());
        selected.RemoveWhere(p => string.IsNullOrWhiteSpace(Label(p)));
        var getInfo = SelectionMover.HostType("QSC.QSys.Design.SignalNameManager")
            .GetMethod("GetInfo", PublicStatic, null, new[] { typeof(string) }, null)
            ?? throw new MissingMethodException("SignalNameManager.GetInfo(string)");
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var groups = new List<Group>();
        foreach (var seed in selected)
        {
            if (visited.Contains(seed)) continue;
            var name = Label(seed);
            var info = getInfo.Invoke(null, new object[] { name }) ?? throw new InvalidOperationException("Named Signal lookup failed.");
            var associated = AssociatedPins(info);
            if (!associated.Contains(seed)) throw new InvalidOperationException("A selected pin was absent from the native associated-name group. Nothing changed.");
            CheckBound(associated.ToArray());
            if (associated.Count > 256)
                throw new InvalidOperationException("A signal group exceeds 256 associated pins. This first color experiment does not support that group.");
            if (associated.Any(visited.Contains)) throw new InvalidOperationException("Named Signal groups overlap unexpectedly. Nothing changed.");
            visited.UnionWith(associated);
            if (visited.Count > Limit || groups.Count == 256)
                throw new InvalidOperationException("This preview exceeds 256 groups or 4096 associated pins. Select fewer names.");
            var states = associated.Select(ReadPin).ToArray();
            if (states.Any(p => !string.Equals(p.Evidence.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The native group contains unexpected names. Nothing changed.");
            var choice = states.Any(p => !p.Attached || p.Locked)
                ? new SignalColorChoice("Unchanged", null, "An associated pin/provider is detached or locked; the entire group is skipped.")
                : SignalColorRules.Choose(states.Select(p => p.Evidence).ToArray(), palette, forced, defaultColorsOnly);
            groups.Add(new Group(name, states, choice, states.Count(p => !selected.Contains(p.Pin))));
        }
        return new Preview(manager, scope, raw, groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            palette.ToDictionary(p => p.Key, p => SignalColorRules.Normalize(p.Value)), forced, defaultColorsOnly);
    }

    private static PinState ReadPin(object pin)
    {
        var provider = Field(pin, "Provider");
        var color = (Color)Required(pin, "Color");
        if (color.A != 255) throw new InvalidOperationException("This experiment expects opaque Named Signal colors.");
        var evidence = new SignalPinEvidence(Label(pin), Convert.ToInt32(Required(pin, "WiringDomain")),
            Convert.ToInt32(Field(pin, "Direction")), Property(provider, "ClassName") as string ?? provider.GetType().FullName ?? "",
            string.Join(" ", new[] { Property(provider, "Label") as string, Property(provider, "CodeName") as string }.Where(s => !string.IsNullOrWhiteSpace(s))),
            HostFields.OptionalText(pin, "CodeName"), Hex((Color)Required(provider, "Color")), Property(provider, "CustomColorApplied") as bool?, Hex(color));
        return new PinState(pin, provider, color, evidence,
            Required(pin, "IsAttached") is true && Required(provider, "IsAttached") is true,
            Required(provider, "IsLocked") is not false);
    }

    private static bool StillMatches(Preview a, Preview b)
    {
        if (!ReferenceEquals(a.Manager, b.Manager) || a.Scope != b.Scope || a.DefaultColorsOnly != b.DefaultColorsOnly
            || !SameSet(a.Selection, b.Selection) || a.Groups.Length != b.Groups.Length) return false;
        foreach (var old in a.Groups)
        {
            var fresh = b.Groups.FirstOrDefault(g => g.Name == old.Name);
            if (fresh is null || fresh.Choice != old.Choice || !SameSet(old.Pins.Select(p => p.Pin), fresh.Pins.Select(p => p.Pin))) return false;
            foreach (var p in old.Pins)
                if (fresh.Pins.Single(n => ReferenceEquals(n.Pin, p.Pin)) != p) return false;
        }
        return true;
    }

    internal static object Describe(Preview plan) => new
    {
        scope = plan.Scope.ToString(), defaultColorsOnly = plan.DefaultColorsOnly,
        forcedRole = plan.ForcedRole?.ToString(), palette = plan.Palette.ToDictionary(x => x.Key.ToString(), x => x.Value),
        groups = plan.Groups.Select(g => new
        {
            g.Name, g.Role, g.Current, g.Proposed, g.AffectedPins, g.OutsideSelection, g.Reason,
            pins = g.Pins.Select(p => new { identity = RuntimeHelpers.GetHashCode(p.Pin), providerIdentity = RuntimeHelpers.GetHashCode(p.Provider),
                color = Hex(p.Before), p.Evidence, p.Attached, p.Locked })
        })
    };

    internal static string Apply(Preview preview, Func<string, object, string> receipt)
    {
        Application.Current.Dispatcher.VerifyAccess();
        var current = Read(preview.Palette, preview.ForcedRole, preview.Scope, preview.DefaultColorsOnly);
        if (!StillMatches(preview, current)) throw new InvalidOperationException("The preview's selection, signal groups, colors, or pins changed. Preview again before applying.");
        var changes = current.Groups.Where(g => g.Changes).ToArray();
        if (changes.Length == 0) return "No proposed colors differ. Nothing changed.";
        var manager = current.Manager;
        foreach (var state in new[] { "IsLoading", "IsInUndo", "IsInEndOperation" })
            if (Required(manager, state) is true) throw new InvalidOperationException($"Designer is busy ({state}). Nothing changed.");
        MethodInfo Method(string n, params Type[] args) => manager.GetType().GetMethod(n, PublicInstance, null, args, null)
            ?? throw new MissingMethodException(manager.GetType().FullName, n);
        var start = Method("StartOperation");
        var finish = Method("EndOperation", typeof(Guid));
        var cancel = Method("CancelOperation");
        var colorProperty = SelectionMover.HostType("QSC.QSys.Design.Pin").GetProperty("Color", PublicInstance);
        if (colorProperty?.PropertyType != typeof(Color) || colorProperty.SetMethod?.IsPublic != true)
            throw new MissingMemberException("Reviewed writable Pin.Color is unavailable.");
        var beforeState = SelectionMover.ReadHostState();
        receipt("signal-color-start", new { plan = Describe(current), hostState = beforeState });
        var operation = (Guid)start.Invoke(manager, null)!;
        if (operation == Guid.Empty) throw new InvalidOperationException("Another Designer operation is active. Nothing changed.");
        bool Exact() => changes.All(g => g.Pins.All(p => (Color)colorProperty.GetValue(p.Pin)! == Parse(g.Proposed)
            && (string)Required(p.Pin, "Label") == p.Evidence.Name));
        var finishing = false;
        try
        {
            foreach (var g in changes)
            {
                // Native setter propagates to all associated Tx/Rx tags. Start on a pin that differs.
                var after = Parse(g.Proposed);
                var seed = g.Pins.First(p => p.Before != after);
                colorProperty.SetValue(seed.Pin, after);
            }
            if (!Exact()) throw new InvalidOperationException("Native color propagation did not match the preview.");
            finishing = true;
            finish.Invoke(manager, new object[] { operation });
        }
        catch (Exception error)
        {
            string rollback;
            if (finishing) rollback = "EndOperation failed; commit state is uncertain. No further mutation attempted.";
            else
            {
                try
                {
                    cancel.Invoke(manager, null);
                    finish.Invoke(manager, new object[] { operation });
                    rollback = changes.All(g => g.Pins.All(p => (Color)colorProperty.GetValue(p.Pin)! == p.Before))
                        ? "Original colors restored through host cancellation."
                        : "Host cancellation did not restore every original color.";
                }
                catch (Exception e) { rollback = "Cancellation failed: " + e.Message; }
            }
            receipt("signal-color-failed", new { error = error.ToString(), rollback, operation });
            throw new InvalidOperationException(rollback, error);
        }
        var exact = Exact();
        try
        {
            receipt("signal-color-completed", new { operation, exactColorsAndNamesAfterEndOperation = exact,
                changedGroups = changes.Length, affectedPins = changes.Sum(g => g.Pins.Length), beforeState,
                afterState = SelectionMover.ReadHostState(), plan = Describe(current),
                after = changes.Select(g => new { g.Name, pins = g.Pins.Select(p => new {
                    identity = RuntimeHelpers.GetHashCode(p.Pin), name = Required(p.Pin, "Label"), color = Hex((Color)colorProperty.GetValue(p.Pin)!) }) }) });
        }
        catch (Exception e) { return "Color edits completed, but the result log could not be saved: " + e.Message; }
        return exact ? $"Colored {changes.Length} signal group(s), including {changes.Sum(g => g.Pins.Length)} associated pins. Undo with Ctrl+Z in Design mode."
            : "Designer changed a value during completion. Inspect the colors and receipt before continuing.";
    }
}
