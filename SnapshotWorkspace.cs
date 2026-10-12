using System.Windows;
using System.Windows.Media;

namespace Designer.Modules.SelectionProbe;

internal static class SnapshotWorkspace
{
    internal static object Create(SnapshotRow[] requested, string title)
    {
        if (SnapshotHost.Get(SnapshotHost.Type("Tools.Set"), "IsEditToolActive") is not true)
            throw new InvalidOperationException("Create a control page in Design mode.");
        var document = SnapshotHost.Document ?? throw new InvalidOperationException("Open a design first.");
        SnapshotTools.Refresh();
        var current = SnapshotTools.Model.Rows;
        var valid = requested.Select(old => current.FirstOrDefault(r => r.Key == old.Key && ReferenceEquals(r.BankObject, old.BankObject)
            && ReferenceEquals(r.ComponentObject, old.ComponentObject))).ToArray();
        if (valid.Any(r => r is null)) throw new InvalidOperationException("Snapshot membership changed. Review the refreshed results before creating a page.");
        requested = valid.OfType<SnapshotRow>().ToArray();
        var rows = requested.DistinctBy(r => r.Key).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("There are no controls in these results.");
        if (rows.Length > 1000) throw new InvalidOperationException("Choose a bank or filter to gather at most 1,000 controls per page.");
        // Prepare a detached page. Native ControlStyle objects retain the source
        // component/control identity and require no wrapper DSP, code or Named Controls.
        var page = Activator.CreateInstance(SnapshotHost.Type("Design.Canvas"))!;
        var pages = SnapshotHost.Get(document, "Pages")!;
        string baseTitle = "Snapshot controls - " + title, uniqueTitle = baseTitle; int suffix = 2;
        var names = SnapshotHost.Items(pages).Select(p => SnapshotHost.Text(p, "Title")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (names.Contains(uniqueTitle)) uniqueTitle = baseTitle + " " + suffix++;
        SnapshotHost.Set(page, "Title", uniqueTitle);
        // Persist contrast with the page instead of relying on this editor's theme.
        SnapshotHost.Set(page, "PageBackgroundColor", 0xff282b2eu);
        var layer = SnapshotHost.Get(page, "CurrentLayer")!;
        double y = 30; int count = 0; var skipped = new List<string>();
        Label(layer, "Snapshot controls · " + title, 30, y, 1000, 32, 18); y += 45;
        Label(layer, "Linked to the original controls. Membership shown here reflects when this page was created.", 30, y, 1000, 28, 11); y += 45;
        foreach (var group in rows.GroupBy(r => r.ComponentObject))
        {
            var first = group.First();
            Label(layer, first.Path + " / " + first.Component, 30, y, 1000, 36, 15); y += 46;
            foreach (var row in group)
            {
                if (row.Descriptor is null) { skipped.Add(row.Control); continue; }
                var style = SnapshotHost.Call(SnapshotHost.Type("Design.ComponentHelper"), "CreateControlStyle", row.ComponentObject, row.Descriptor, false);
                if (style is null) { skipped.Add(row.Control); continue; }
                SnapshotHost.Set(style, "IsInControlPanel", false);
                var size = (Size)SnapshotHost.Get(style, "Size")!;
                if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("This control has no usable native size: " + row.Control);
                double height = Math.Max(50, size.Height + 10);
                Label(layer, row.Control + "\n" + row.ControlId, 30, y, 290, height, 12);
                SnapshotHost.Set(style, "Position", new Point(340, y + 5));
                SnapshotHost.Call(layer, "AddChild", style);
                string banks = string.Join(", ", requested.Where(r => r.Key == row.Key).Select(r => r.Bank).Distinct());
                Label(layer, banks, 360 + size.Width, y, 400, height, 11);
                y += height + 15; count++;
            }
            y += 25;
        }
        if (count == 0) throw new InvalidOperationException("These controls do not expose native visual controls to gather.");
        if (skipped.Count > 0) Label(layer, "No native visual available: " + string.Join(", ", skipped), 30, y, 1000, Math.Max(60, skipped.Count * 16), 11);
        var manager = SnapshotHost.Manager!;
        SnapshotHost.Call(manager, "EndOperation");
        try
        {
            SnapshotHost.Call(pages, "Add", page);
            SnapshotHost.Call(manager, "EndOperation");
        }
        catch
        {
            if (SnapshotHost.Items(pages).Any(p => ReferenceEquals(p, page))) SnapshotHost.Call(pages, "Remove", page);
            SnapshotHost.Call(manager, "EndOperation"); throw;
        }
        SnapshotHost.Call(SnapshotHost.Get(SnapshotHost.Type("App"), "Window")!, "OpenPage", page);
        return page;
    }
    private static void Label(object layer, string text, double x, double y, double width, double height, double fontSize)
    {
        var label = Activator.CreateInstance(SnapshotHost.Type("Design.Graphics.TextBlock"))!;
        SnapshotHost.Set(label, "Label", text); SnapshotHost.Set(label, "Position", new Point(x, y));
        SnapshotHost.Set(label, "Size", new Size(width, height)); SnapshotHost.Set(label, "FontSize", fontSize);
        SnapshotHost.Set(label, "Color", Colors.White); SnapshotHost.Set(label, "Fill", Colors.Transparent);
        SnapshotHost.Set(label, "HorizontalTextAlignment", Enum.Parse(label.GetType().GetProperty("HorizontalTextAlignment")!.PropertyType, "Left"));
        SnapshotHost.Call(layer, "AddChild", label);
    }
}
