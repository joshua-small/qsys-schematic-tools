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
            label = item.GetType().GetProperty("Label")?.GetValue(item) as string,
            position = item.GetType().GetProperty("Position")?.GetValue(item)?.ToString()
        }).ToArray();
        return new { selection, hostState = readHostState(), unlockedObjects = readUnlockedObjects() };
    }

    // Failures must reach the caller; a partial report must not look successful.
    internal static string Serialize(object snapshot) =>
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
}
