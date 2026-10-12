using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Designer.Modules.SelectionProbe;

internal static class SelectionPacker
{
    private sealed record Item(object Model, Point Position, Size Body, Size Drawn, bool IsInternalConnector);

    // Normal 10.5 Schematic Elements > Layout entries, plus ordinary components.
    // Generic Graphic.Object also includes UCI controls and unreviewed object types.
    private static readonly IReadOnlyDictionary<string, string> GraphicTypes = new Dictionary<string, string>
    {
        ["QSC.QSys.Design.Component"] = "QSC.QSys.Graphic.Component",
        ["QSC.QSys.Design.Container"] = "QSC.QSys.Graphic.Container",
        ["QSC.QSys.Design.ChannelGroup"] = "QSC.QSys.Graphic.ChannelGroup",
        ["QSC.QSys.Design.ContainerPin"] = "QSC.QSys.Graphic.ContainerPin",
        ["QSC.QSys.Design.Graphics.Popup"] = "QSC.QSys.Graphic.Popup.PopupButton",
        ["QSC.QSys.Design.Mux.Muxer"] = "QSC.QSys.Graphic.Mux.Muxer",
        ["QSC.QSys.Design.Mux.Demuxer"] = "QSC.QSys.Graphic.Mux.Muxer"
    };

    private static string? GraphicTypeName(Type type)
    {
        // Host component locations can derive from Component; preserve that support.
        for (Type? current = type; current is not null; current = current.BaseType)
            if (GraphicTypes.TryGetValue(current.FullName ?? "", out var name)) return name;
        return null;
    }

    private static Item[] ReadItems(object? expectedCanvas = null)
    {
        Application.Current.Dispatcher.VerifyAccess();
        var models = SelectionMover.SelectedModels();
        var objectType = SelectionMover.HostType("QSC.QSys.Graphic.Object");
        var graphics = ((IEnumerable)SelectionMover.GlobalSelection).Cast<object>()
            .Where(objectType.IsInstanceOfType).ToArray();
        var result = new List<Item>();
        object? canvas = null;
        foreach (var model in models)
        {
            var graphicName = GraphicTypeName(model.GetType());
            if (graphicName is null)
                throw new InvalidOperationException($"Packing supports schematic components, Layout items and internal input/output connectors. Unsupported selection: {model.GetType().FullName}. Nothing moved.");
            var graphicType = SelectionMover.HostType(graphicName);
            var matches = graphics.Where(g => graphicType.IsInstanceOfType(g)
                && ReferenceEquals(SelectionMover.RequiredProperty(g, "obj"), model)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("A selected item does not have one unambiguous drawn instance. Nothing moved.");
            var graphic = matches[0];
            var currentCanvas = SelectionMover.RequiredProperty(graphic, "GraphicCanvas");
            if (SelectionMover.RequiredProperty(graphic, "IsCanvasLocked") is not false
                || (expectedCanvas is not null && !ReferenceEquals(currentCanvas, expectedCanvas)))
                throw new InvalidOperationException("The selected items must be on the same unlocked schematic canvas. Nothing moved.");
            if (canvas is null && !SelectionMover.IsSchematicCanvas(currentCanvas))
                throw new InvalidOperationException("Select items on a schematic page or inside a Container or Channel Group schematic. Use native packing for UCI and control panels. Nothing moved.");
            canvas ??= currentCanvas;
            if (!ReferenceEquals(canvas, currentCanvas))
                throw new InvalidOperationException("Select items on one schematic canvas. Nothing moved.");
            var position = (Point)SelectionMover.RequiredProperty(model, "Position");
            var graphicPosition = (Point)SelectionMover.RequiredProperty(graphic, "Position");
            var body = (Size)SelectionMover.RequiredProperty(model, "Size");
            var drawn = (Size)SelectionMover.RequiredProperty(graphic, "Size");
            if (graphicPosition != position || !PackingLayout.FootprintIsReady(body.Width, body.Height, drawn.Width, drawn.Height))
                throw new InvalidOperationException("The drawn item geometry is not ready or uses a different coordinate space. Nothing moved.");
            result.Add(new Item(model, position, body, drawn,
                SelectionMover.HostType("QSC.QSys.Design.ContainerPin").IsInstanceOfType(model)));
        }
        return result.ToArray();
    }

    private static object Describe(Item item) => new
    {
        type = item.Model.GetType().FullName,
        isInternalConnector = item.IsInternalConnector,
        sessionIdentity = RuntimeHelpers.GetHashCode(item.Model),
        position = new { x = item.Position.X, y = item.Position.Y },
        bodySize = new { width = item.Body.Width, height = item.Body.Height },
        drawnSize = new { width = item.Drawn.Width, height = item.Drawn.Height },
        extraDrawnHeight = item.Drawn.Height - item.Body.Height
    };

    internal static object ReadFootprints()
    {
        // Keep the receipt field name for existing consumers; type identifies Layout items.
        try { return new { components = ReadItems().Select(Describe).ToArray(), error = (string?)null }; }
        catch (Exception error) { return new { components = Array.Empty<object>(), error = error.Message }; }
    }

    internal static bool CanPackOn(object canvas)
    {
        try { return ReadItems(canvas).Length >= 2; }
        catch (Exception) { return false; }
    }

    internal static string Pack(PackAlignment alignment, int gap, Func<string, object, string> receipt,
        object? expectedCanvas = null, string invocation = "button")
    {
        var items = ReadItems(expectedCanvas);
        var plan = PackingLayout.Plan(items.Select((c, i) =>
            new PackingItem(i, c.Position.X, c.Position.Y, c.Drawn.Width, c.Drawn.Height, c.IsInternalConnector)).ToArray(), alignment, gap);
        var targets = plan.Select(p => (items[p.Id].Model, new Point(p.X, p.Y))).ToArray();
        return SelectionMover.ApplyPositions(targets, "pack", new
        {
            alignment = alignment.ToString(), gap, invocation,
            connectorGap = 0, gapRule = "Zero between adjacent internal connectors; configured gap for every other pair",
            footprint = "Native Graphic.Object.Size; component control-pin drawers included; Popup Button body only",
            components = items.Select(Describe).ToArray()
        }, $"Packed {items.Length} items {alignment.ToString().ToLowerInvariant()}. Gap: {gap}; zero between input/output connectors.", receipt);
    }
}
