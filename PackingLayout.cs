namespace Designer.Modules.SelectionProbe;

internal enum PackAlignment { Left, Center, Right, Top, VerticalCenter, Bottom }
internal sealed record PackingItem(int Id, double X, double Y, double Width, double Height, bool IsInternalConnector = false);
internal sealed record PackedPosition(int Id, double X, double Y);

/// <summary>Pure coordinate planning using each item's drawn bounds, including exposed control pins.</summary>
internal static class PackingLayout
{
    internal static bool IsHorizontalRow(PackAlignment alignment) => alignment is
        PackAlignment.Top or PackAlignment.VerticalCenter or PackAlignment.Bottom;

    internal static bool FootprintIsReady(double bodyWidth, double bodyHeight, double drawnWidth, double drawnHeight)
    {
        // Design.Object.Size rounds each dimension; Graphic.PinProvider.Size keeps fractional text widths.
        // The drawn footprint may therefore be up to half a unit smaller than the rounded body.
        return new[] { bodyWidth, bodyHeight, drawnWidth, drawnHeight }.All(v => double.IsFinite(v) && v > 0)
            && drawnWidth + 0.5000001 >= bodyWidth && drawnHeight + 0.5000001 >= bodyHeight;
    }

    internal static PackedPosition[] Plan(IReadOnlyList<PackingItem> items, PackAlignment alignment, int gap)
    {
        // Transpose the same geometry rules so horizontal rows retain left-to-right order
        // and align the complete drawn height, including exposed control pins.
        if (IsHorizontalRow(alignment))
        {
            var transposedAlignment = alignment == PackAlignment.Top ? PackAlignment.Left
                : alignment == PackAlignment.Bottom ? PackAlignment.Right : PackAlignment.Center;
            return Plan(items.Select(i => new PackingItem(i.Id, i.Y, i.X, i.Height, i.Width, i.IsInternalConnector)).ToArray(),
                transposedAlignment, gap).Select(p => new PackedPosition(p.Id, p.Y, p.X)).ToArray();
        }
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (gap < 0 || gap > 10000) throw new ArgumentOutOfRangeException(nameof(gap), "Gap must be 0 to 10000 whole schematic units.");
        if (items.Count < 2) throw new ArgumentException("Select at least two items to pack.");
        if (items.Select(i => i.Id).Distinct().Count() != items.Count)
            throw new ArgumentException("Duplicate packing identities.");
        foreach (var item in items)
            if (!Coordinate(item.X) || !Coordinate(item.Y) || !double.IsFinite(item.Width)
                || !double.IsFinite(item.Height) || item.Width <= 0 || item.Height <= 0)
                throw new ArgumentException("Items need valid whole-unit positions and positive drawn sizes.");

        // All alignments keep the same top-to-bottom order, breaking ties left-to-right.
        var ordered = items.OrderBy(i => i.Y).ThenBy(i => i.X).ThenBy(i => i.Id).ToArray();
        var anchor = alignment switch
        {
            PackAlignment.Left => items.Min(i => i.X),
            PackAlignment.Center => items.Average(i => i.X + i.Width / 2),
            PackAlignment.Right => items.Max(i => i.X + i.Width),
            _ => throw new ArgumentOutOfRangeException(nameof(alignment))
        };
        var y = items.Min(i => i.Y);
        var result = new List<PackedPosition>();
        PackingItem? previous = null;
        foreach (var item in ordered)
        {
            // Flush internal connectors to each other, preserving the configured
            // clearance at connector/component boundaries and between components.
            if (previous is not null)
                y = Math.Ceiling(y + previous.Height
                    + (previous.IsInternalConnector && item.IsInternalConnector ? 0 : gap));
            var x = anchor - (alignment == PackAlignment.Left ? 0 :
                alignment == PackAlignment.Center ? item.Width / 2 : item.Width);
            // Match the host's nonnegative whole-unit position setter; do not silently clamp.
            x = Math.Floor(x + 0.5);
            if (!Coordinate(x) || !Coordinate(y))
                throw new ArgumentException("Packing would move an item outside the supported coordinate range. Nothing moved.");
            result.Add(new PackedPosition(item.Id, x, y));
            previous = item;
        }
        return result.ToArray();
    }

    private static bool Coordinate(double value) => double.IsFinite(value)
        && value >= 0 && value < int.MaxValue && value == Math.Truncate(value);
}
