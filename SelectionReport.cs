using System.Collections;
using System.Text.Json;

namespace Designer.Modules.SelectionProbe;

/// <summary>Builds the diagnostic payload without depending on the editor UI.</summary>
internal static class SelectionReport
{
    internal const int MaximumItems = 256;

    internal static object Capture(
        IEnumerable items,
        Func<object?> readHostState,
        Func<object?> readUnlockedObjects)
    {
        var selection = items.Cast<object>().Take(MaximumItems).Select(item => new
        {
            type = item.GetType().FullName,
            label = ReadLabel(item),
            position = item.GetType().GetProperty("Position")?.GetValue(item)?.ToString()
        }).ToArray();
        return new { selection, hostState = readHostState(), unlockedObjects = readUnlockedObjects() };
    }

    private static string? ReadLabel(object item)
    {
        var label = item.GetType().GetProperty("Label");
        if (label is not null) return label.GetValue(item) as string;
        // Selected graphics expose the labeled design object through obj.
        var model = item.GetType().GetProperty("obj")?.GetValue(item);
        return model?.GetType().GetProperty("Label")?.GetValue(model) as string;
    }

    // Failures must reach the caller; a partial report must not look successful.
    internal static string Serialize(object snapshot) =>
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
}
