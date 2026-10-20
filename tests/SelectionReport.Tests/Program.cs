using System.Collections;
using System.Reflection;
using System.Text.Json;
using Designer.Modules.SelectionProbe;

var tests = new (string Name, Action Run)[]
{
    ("empty selection preserves the report schema", () =>
    {
        var report = Report(Array.Empty<object>());
        Sequence(new[] { "selection", "hostState", "unlockedObjects" },
            report.EnumerateObject().Select(p => p.Name));
        Equal(0, report.GetProperty("selection").GetArrayLength());
        Equal(JsonValueKind.Null, report.GetProperty("hostState").ValueKind);
        Equal(JsonValueKind.Null, report.GetProperty("unlockedObjects").ValueKind);
    }),
    ("single selection captures type label and position", () =>
    {
        var entry = Entries(new[] { new Item { Label = "Gain", Position = new PointText() } })[0];
        Sequence(new[] { "type", "label", "position" }, entry.EnumerateObject().Select(p => p.Name));
        Equal(typeof(Item).FullName, entry.GetProperty("type").GetString());
        Equal("Gain", entry.GetProperty("label").GetString());
        Equal("12.5, -20", entry.GetProperty("position").GetString());
    }),
    ("multiple selections retain order and duplicates", () =>
    {
        var first = new Item { Label = "first" };
        var entries = Entries(new object[] { first, new Item { Label = "second" }, first });
        Sequence(new[] { "first", "second", "first" },
            entries.EnumerateArray().Select(e => e.GetProperty("label").GetString()));
    }),
    ("non-generic enumerables are accepted", () =>
    {
        Equal(2, Entries(new ArrayList { new object(), new object() }).GetArrayLength());
    }),
    ("255 selections are retained", () => Equal(255, Entries(Items(255)).GetArrayLength())),
    ("256 selections are retained", () => Equal(256, Entries(Items(256)).GetArrayLength())),
    ("257 selections are bounded in input order", () =>
    {
        var entries = Entries(Enumerable.Range(0, 257).Select(i => new Item { Label = i.ToString() }));
        Equal(256, entries.GetArrayLength());
        Equal("0", entries[0].GetProperty("label").GetString());
        Equal("255", entries[255].GetProperty("label").GetString());
    }),
    ("selection beyond the bound is neither read nor advanced", () =>
    {
        var source = new BoundedSource();
        Equal(256, Entries(source).GetArrayLength());
        Equal(256, source.Yielded);
        Equal(true, source.Disposed);
    }),
    ("each item is read once and selection values are captured eagerly", () =>
    {
        var item = new CountingItem();
        var snapshot = SelectionReport.Capture(new[] { item }, () => null, () => null);
        Equal(1, item.LabelReads);
        Equal(1, item.PositionReads);
        item.Value = "after";
        using var json = JsonDocument.Parse(SelectionReport.Serialize(snapshot));
        Equal("before", json.RootElement.GetProperty("selection")[0].GetProperty("label").GetString());
        Equal(1, item.LabelReads);
        Equal(1, item.PositionReads);
    }),
    ("missing label and position remain explicit nulls", () =>
    {
        var entry = Entries(new[] { new object() })[0];
        Equal("System.Object", entry.GetProperty("type").GetString());
        Equal(JsonValueKind.Null, entry.GetProperty("label").ValueKind);
        Equal(JsonValueKind.Null, entry.GetProperty("position").ValueKind);
    }),
    ("null label and position remain explicit nulls", () =>
    {
        var entry = Entries(new[] { new Item() })[0];
        Equal(JsonValueKind.Null, entry.GetProperty("label").ValueKind);
        Equal(JsonValueKind.Null, entry.GetProperty("position").ValueKind);
    }),
    ("non-string labels are not converted", () =>
    {
        var entry = Entries(new[] { new UnknownLabel() })[0];
        Equal(JsonValueKind.Null, entry.GetProperty("label").ValueKind);
    }),
    ("private properties are not exposed", () =>
    {
        var entry = Entries(new[] { new PrivateProperties() })[0];
        Equal(JsonValueKind.Null, entry.GetProperty("label").ValueKind);
        Equal(JsonValueKind.Null, entry.GetProperty("position").ValueKind);
    }),
    ("inherited public properties are reported", () =>
    {
        Equal("inherited", Entries(new[] { new DerivedItem { Label = "inherited" } })[0]
            .GetProperty("label").GetString());
    }),
    ("empty strings are distinct from unknown values", () =>
    {
        var entry = Entries(new[] { new Item { Label = "", Position = "" } })[0];
        Equal("", entry.GetProperty("label").GetString());
        Equal("", entry.GetProperty("position").GetString());
    }),
    ("position string conversion can return null", () =>
    {
        Equal(JsonValueKind.Null, Entries(new[] { new Item { Position = new NullText() } })[0]
            .GetProperty("position").ValueKind);
    }),
    ("selection is captured before host readers run once in order", () =>
    {
        var events = new List<string>();
        IEnumerable Source()
        {
            events.Add("selection");
            yield return new object();
        }
        SelectionReport.Capture(Source(),
            () => { events.Add("host"); return null; },
            () => { events.Add("objects"); return null; });
        Sequence(new[] { "selection", "host", "objects" }, events);
    }),
    ("host and unlocked payloads preserve nested values", () =>
    {
        var report = Report(Array.Empty<object>(),
            () => new { mode = "Design", canUndo = false, unknown = (string?)null },
            () => new[] { new { label = "one", x = -12.5, locked = false } });
        var state = report.GetProperty("hostState");
        Equal("Design", state.GetProperty("mode").GetString());
        Equal(false, state.GetProperty("canUndo").GetBoolean());
        Equal(JsonValueKind.Null, state.GetProperty("unknown").ValueKind);
        var item = report.GetProperty("unlockedObjects")[0];
        Equal("one", item.GetProperty("label").GetString());
        Equal(-12.5, item.GetProperty("x").GetDouble());
        Equal(false, item.GetProperty("locked").GetBoolean());
    }),
    ("unlocked objects are not subject to the selection bound", () =>
    {
        var report = Report(Array.Empty<object>(), () => null, () => Enumerable.Range(0, 257).ToArray());
        Equal(257, report.GetProperty("unlockedObjects").GetArrayLength());
    }),
    ("serialized labels round-trip quotes newlines unicode and markup", () =>
    {
        const string label = "A \"quoted\"\\path\n雪 <tag> & \t";
        var snapshot = SelectionReport.Capture(new[] { new Item { Label = label } }, () => null, () => null);
        var text = SelectionReport.Serialize(snapshot);
        Equal(true, text.Contains('\n'));
        using var json = JsonDocument.Parse(text);
        Equal(label, json.RootElement.GetProperty("selection")[0].GetProperty("label").GetString());
    }),
    ("selection getter failures propagate before host readers", () =>
    {
        var reads = 0;
        var error = Throws<TargetInvocationException>(() => SelectionReport.Capture(
            new[] { new ThrowingLabel() }, () => ++reads, () => ++reads));
        Equal("label failed", error.GetBaseException().Message);
        Equal(0, reads);
    }),
    ("position getter failures propagate", () =>
    {
        var error = Throws<TargetInvocationException>(() => Entries(new[] { new ThrowingPosition() }));
        Equal("position failed", error.GetBaseException().Message);
    }),
    ("position conversion failures propagate", () =>
    {
        var error = Throws<InvalidOperationException>(() =>
            Entries(new[] { new Item { Position = new ThrowingText() } }));
        Equal("conversion failed", error.Message);
    }),
    ("null items are rejected without invoking host readers", () =>
    {
        var reads = 0;
        Throws<NullReferenceException>(() => SelectionReport.Capture(
            new object[] { null! }, () => ++reads, () => ++reads));
        Equal(0, reads);
    }),
    ("null selection is rejected", () =>
    {
        Throws<ArgumentNullException>(() => SelectionReport.Capture(null!, () => null, () => null));
    }),
    ("enumeration failures propagate and dispose the enumerator", () =>
    {
        var disposed = false;
        IEnumerable Source()
        {
            try { yield return new object(); throw new InvalidOperationException("enumeration failed"); }
            finally { disposed = true; }
        }
        var reads = 0;
        var error = Throws<InvalidOperationException>(() =>
            SelectionReport.Capture(Source(), () => ++reads, () => ++reads));
        Equal("enumeration failed", error.Message);
        Equal(true, disposed);
        Equal(0, reads);
    }),
    ("host-state failure prevents the unlocked-object read", () =>
    {
        var reads = 0;
        var error = Throws<InvalidOperationException>(() => SelectionReport.Capture(
            Array.Empty<object>(),
            () => throw new InvalidOperationException("host failed"),
            () => ++reads));
        Equal("host failed", error.Message);
        Equal(0, reads);
    }),
    ("unlocked-object reader failure propagates", () =>
    {
        var error = Throws<InvalidOperationException>(() => SelectionReport.Capture(
            Array.Empty<object>(), () => null, () => throw new InvalidOperationException("objects failed")));
        Equal("objects failed", error.Message);
    }),
    ("cyclic host payload fails serialization", () =>
    {
        var cycle = new Cycle();
        cycle.Next = cycle;
        var snapshot = SelectionReport.Capture(Array.Empty<object>(), () => cycle, () => null);
        Throws<JsonException>(() => SelectionReport.Serialize(snapshot));
    }),
    ("unsupported payload type fails serialization", () =>
    {
        var snapshot = SelectionReport.Capture(Array.Empty<object>(), () => typeof(Item), () => null);
        Throws<NotSupportedException>(() => SelectionReport.Serialize(snapshot));
    }),
    ("non-finite values fail serialization instead of producing invalid JSON", () =>
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var snapshot = SelectionReport.Capture(Array.Empty<object>(), () => null, () => new { x = value });
            Throws<ArgumentException>(() => SelectionReport.Serialize(snapshot));
        }
    }),
    ("payload getter failure remains visible at the serialization boundary", () =>
    {
        var snapshot = SelectionReport.Capture(Array.Empty<object>(), () => new ThrowingLabel(), () => null);
        var error = Throws<InvalidOperationException>(() => SelectionReport.Serialize(snapshot));
        Equal("label failed", error.GetBaseException().Message);
    }),
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} selection-report checks passed.");
return failed == 0 ? 0 : 1;

static JsonElement Report(IEnumerable items, Func<object?>? host = null, Func<object?>? unlocked = null)
{
    var snapshot = SelectionReport.Capture(items, host ?? (() => null), unlocked ?? (() => null));
    using var document = JsonDocument.Parse(SelectionReport.Serialize(snapshot));
    return document.RootElement.Clone();
}

static JsonElement Entries(IEnumerable items) => Report(items).GetProperty("selection");
static IEnumerable<object> Items(int count) => Enumerable.Range(0, count).Select(_ => new object());
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}
static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual)) throw new Exception("Sequence mismatch.");
}
static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T error) { return error; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

public class Item { public string? Label { get; set; } public object? Position { get; set; } }
public sealed class DerivedItem : Item;
public sealed class PointText { public override string ToString() => "12.5, -20"; }
public sealed class NullText { public override string? ToString() => null; }
public sealed class UnknownLabel { public int Label => 17; }
public sealed class PrivateProperties { private string Label => "hidden"; private string Position => "hidden"; }
public sealed class ThrowingLabel { public string Label => throw new InvalidOperationException("label failed"); }
public sealed class ThrowingPosition { public object Position => throw new InvalidOperationException("position failed"); }
public sealed class ThrowingText { public override string ToString() => throw new InvalidOperationException("conversion failed"); }
public sealed class Cycle { public Cycle? Next { get; set; } }
public sealed class CountingItem
{
    public string Value = "before";
    public int LabelReads;
    public int PositionReads;
    public string Label { get { LabelReads++; return Value; } }
    public object Position { get { PositionReads++; return "position"; } }
}
public sealed class BoundedSource : IEnumerable
{
    public int Yielded;
    public bool Disposed;
    public IEnumerator GetEnumerator()
    {
        try
        {
            for (var i = 0; i < 256; i++) { Yielded++; yield return new object(); }
            throw new InvalidOperationException("Read beyond the selection limit.");
        }
        finally { Disposed = true; }
    }
}
