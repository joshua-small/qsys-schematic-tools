using Designer.Modules.SelectionProbe;

var passed = 0;
void Check(string name, Action test)
{
    test();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Equal(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 0.000001) throw new Exception($"Expected {expected}, got {actual}");
}
void Reject(Action test)
{
    try { test(); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected invalid layout to be rejected before applying positions.");
}
PackingItem[] items = [new(0, 100, 100, 80, 92), new(1, 300, 400, 100, 120), new(2, 400, 600, 60, 28)];

Check("Left preserves the leftmost anchor and clears exposed-pin heights", () =>
{
    var plan = PackingLayout.Plan(items, PackAlignment.Left, 20);
    Equal(100, plan[0].Y); Equal(212, plan[1].Y); Equal(352, plan[2].Y);
    foreach (var p in plan) Equal(100, p.X);
});
Check("Right preserves the original rightmost edge for unequal widths", () =>
{
    var plan = PackingLayout.Plan(items, PackAlignment.Right, 10);
    foreach (var p in plan) Equal(460, p.X + items[p.Id].Width);
    Equal(202, plan[1].Y); Equal(332, plan[2].Y);
});
Check("Center uses the original mean of component centers with host rounding", () =>
{
    var plan = PackingLayout.Plan(items, PackAlignment.Center, 20);
    Equal(267, plan[0].X); Equal(257, plan[1].X); Equal(277, plan[2].X);
});
Check("Stack alignments preserve vertical order even when horizontal order differs", () =>
{
    PackingItem[] scrambled = [new(2, 100, 600, 60, 28), new(0, 400, 100, 80, 92), new(1, 300, 400, 100, 120)];
    foreach (var alignment in Enum.GetValues<PackAlignment>().Where(a => !PackingLayout.IsHorizontalRow(a)))
    {
        var plan = PackingLayout.Plan(scrambled, alignment, 20);
        if (!plan.Select(p => p.Id).SequenceEqual(new[] { 0, 1, 2 })) throw new Exception("Order changed.");
    }
});
Check("Zero gap is flush and fractional heights round upward without overlap", () =>
{
    PackingItem[] fractional = [new(0, 100, 100, 80, 92.25), new(1, 100, 400, 80, 28)];
    var plan = PackingLayout.Plan(fractional, PackAlignment.Left, 0);
    Equal(193, plan[1].Y);
    var flush = PackingLayout.Plan(items, PackAlignment.Left, 0);
    Equal(192, flush[1].Y); Equal(312, flush[2].Y);
});
Check("A fresh plan reflects changed exposed-pin height", () =>
{
    var expanded = items.ToArray();
    expanded[0] = expanded[0] with { Height = 152 };
    var plan = PackingLayout.Plan(expanded, PackAlignment.Left, 20);
    Equal(272, plan[1].Y); Equal(412, plan[2].Y);
});
Check("Already packed integral layouts are idempotent", () =>
{
    foreach (var alignment in Enum.GetValues<PackAlignment>())
    {
        var first = PackingLayout.Plan(items, alignment, 20);
        var next = first.Select(p => items[p.Id] with { X = p.X, Y = p.Y }).ToArray();
        var second = PackingLayout.Plan(next, alignment, 20);
        if (!first.SequenceEqual(second)) throw new Exception("Repeated pack drifted.");
    }
});
Check("Bad gaps, duplicate identities, and fewer than two items are refused", () =>
{
    Reject(() => PackingLayout.Plan(items, PackAlignment.Left, -1));
    Reject(() => PackingLayout.Plan(items, PackAlignment.Left, 10001));
    Reject(() => PackingLayout.Plan([items[0], items[0]], PackAlignment.Left, 20));
    Reject(() => PackingLayout.Plan([items[0]], PackAlignment.Left, 20));
});
Check("Invalid geometry and negative proposed positions are refused", () =>
{
    Reject(() => PackingLayout.Plan([items[0], items[1] with { Height = double.NaN }], PackAlignment.Left, 20));
    Reject(() => PackingLayout.Plan([items[0], items[1] with { Width = 0 }], PackAlignment.Left, 20));
    Reject(() => PackingLayout.Plan([items[0], items[1] with { X = 1.25 }], PackAlignment.Left, 20));
    Reject(() => PackingLayout.Plan([new(0, 0, 0, 1000, 10), new(1, 0, 100, 10, 10)], PackAlignment.Center, 20));
});
Check("Rounded model sizes accept fractional drawn labels on either side of the integer", () =>
{
    if (!PackingLayout.FootprintIsReady(129, 24, 129.15, 46)
        || !PackingLayout.FootprintIsReady(129, 24, 128.85, 46)
        || PackingLayout.FootprintIsReady(129, 24, 120, 46)
        || PackingLayout.FootprintIsReady(129, 24, 128.85, 0))
        throw new Exception("Rounded-body footprint validation is wrong.");
});
Check("Observed Designer footprints give exact drawer clearance and bounded horizontal rounding", () =>
{
    PackingItem[] actual = [new(0, 246, 166, 82, 114), new(1, 654, 144, 82, 110), new(2, 420, 248, 129.15, 46)];
    foreach (var alignment in Enum.GetValues<PackAlignment>().Where(a => !PackingLayout.IsHorizontalRow(a)))
    {
        var plan = PackingLayout.Plan(actual, alignment, 20);
        Equal(144, plan[0].Y); Equal(274, plan[1].Y); Equal(408, plan[2].Y);
        var again = PackingLayout.Plan(plan.Select(p => actual[p.Id] with { X = p.X, Y = p.Y }).ToArray(), alignment, 20);
        if (!plan.SequenceEqual(again)) throw new Exception("Observed footprints drifted on repeat packing.");
        var anchor = alignment == PackAlignment.Center ? actual.Average(i => i.X + i.Width / 2) : 736;
        if (alignment != PackAlignment.Left)
            foreach (var p in plan)
            {
                var edge = p.X + actual[p.Id].Width / (alignment == PackAlignment.Center ? 2 : 1);
                if (Math.Abs(edge - anchor) > 0.5) throw new Exception("Alignment exceeded host rounding tolerance.");
            }
    }
});
Check("Top packs left to right with horizontal gaps and a common top", () =>
{
    var plan = PackingLayout.Plan(items, PackAlignment.Top, 20);
    Equal(100, plan[0].X); Equal(200, plan[1].X); Equal(320, plan[2].X);
    foreach (var p in plan) Equal(100, p.Y);
});
Check("Bottom aligns the full exposed-pin footprint", () =>
{
    var plan = PackingLayout.Plan(items, PackAlignment.Bottom, 10);
    Equal(100, plan[0].X); Equal(190, plan[1].X); Equal(300, plan[2].X);
    foreach (var p in plan) Equal(628, p.Y + items[p.Id].Height);
});
Check("Vertical center uses mean drawn centers and preserves horizontal order", () =>
{
    var plan = PackingLayout.Plan(items.Reverse().ToArray(), PackAlignment.VerticalCenter, 0);
    if (!plan.Select(p => p.Id).SequenceEqual(new[] { 0, 1, 2 })) throw new Exception("Horizontal order changed.");
    Equal(361, plan[0].Y); Equal(347, plan[1].Y); Equal(393, plan[2].Y);
    Equal(100, plan[0].X); Equal(180, plan[1].X); Equal(280, plan[2].X);
});
Check("Fractional row widths round gaps upward and changed pin heights change bottom alignment", () =>
{
    PackingItem[] row = [new(0, 100, 100, 82, 284), new(1, 300, 100, 129.15, 46), new(2, 600, 100, 82, 110)];
    var plan = PackingLayout.Plan(row, PackAlignment.Bottom, 20);
    Equal(100, plan[0].X); Equal(202, plan[1].X); Equal(352, plan[2].X);
    Equal(100, plan[0].Y); Equal(338, plan[1].Y); Equal(274, plan[2].Y);
    var again = PackingLayout.Plan(plan.Select(p => row[p.Id] with { X = p.X, Y = p.Y }).ToArray(), PackAlignment.Bottom, 20);
    if (!plan.SequenceEqual(again)) throw new Exception("Fractional row drifted on repeat.");
});
Check("Internal connector pairs ignore the configured gap in every direction", () =>
{
    PackingItem[] connectors = [new(0, 100, 100, 30, 10, true), new(1, 200, 200, 30, 10, true), new(2, 300, 300, 30, 10, true)];
    foreach (var alignment in Enum.GetValues<PackAlignment>())
    {
        var result = PackingLayout.Plan(connectors, alignment, 37);
        bool row = PackingLayout.IsHorizontalRow(alignment);
        Equal(row ? 30 : 10, row ? result[1].X - result[0].X : result[1].Y - result[0].Y);
        Equal(row ? 30 : 10, row ? result[2].X - result[1].X : result[2].Y - result[1].Y);
    }
});
Check("Mixed stacks and rows keep the gap at both component boundaries", () =>
{
    PackingItem[] mixed = [new(0, 100, 100, 30, 10, true), new(1, 200, 200, 30, 10, true),
        new(2, 300, 300, 80, 92), new(3, 400, 400, 30, 10, true), new(4, 500, 500, 30, 10, true)];
    foreach (var alignment in Enum.GetValues<PackAlignment>())
    {
        var result = PackingLayout.Plan(mixed, alignment, 25);
        bool row = PackingLayout.IsHorizontalRow(alignment);
        for (int i = 1; i < result.Length; i++)
            Equal(i is 1 or 4 ? 0 : 25,
                row ? result[i].X - result[i - 1].X - mixed[i - 1].Width
                    : result[i].Y - result[i - 1].Y - mixed[i - 1].Height);
        var again = PackingLayout.Plan(result.Select(p => mixed[p.Id] with { X = p.X, Y = p.Y }).ToArray(), alignment, 25);
        if (!result.SequenceEqual(again)) throw new Exception("Mixed connector packing drifted on repeat.");
    }
});
Check("Connector policy follows sorted adjacency and preserves nonnegative validation", () =>
{
    PackingItem[] shuffled = [new(0, 100, 300, 30, 10, true), new(1, 100, 100, 30, 10, true), new(2, 100, 200, 80, 92)];
    var result = PackingLayout.Plan(shuffled, PackAlignment.Left, 20);
    Equal(100, result[0].Y); Equal(130, result[1].Y); Equal(242, result[2].Y);
    Reject(() => PackingLayout.Plan(shuffled, PackAlignment.Left, -1));
});
Console.WriteLine($"{passed} packing contract checks passed.");
