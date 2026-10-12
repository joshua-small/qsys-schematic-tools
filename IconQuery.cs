namespace Designer.Modules.SelectionProbe;

internal static class IconQuery
{
    // Search-only vocabulary keyed by the native description. Never replace the
    // displayed name or encoded glyph. State-specific words belong only to the
    // corresponding artwork: Volume None is quiet; Volume Strike is crossed out.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microphone"] = "mic voice input",
        ["Microphone None"] = "mic mute muted microphone off",
        ["Volume"] = "speaker audio sound",
        ["Volume None"] = "speaker audio sound quiet silent zero",
        ["Volume Strike"] = "speaker audio sound mute muted off",
        ["Widget"] = "settings gear cog preferences configuration",
        ["Wrench"] = "tools setup maintenance",
        ["Magnifying Glass"] = "search find magnifier",
        ["Page Search"] = "document find",
        ["Alert"] = "warning caution attention error",
        ["Info"] = "information help",
        ["X"] = "close cancel dismiss remove",
        ["X Circle"] = "close cancel dismiss error",
        ["Trash"] = "delete bin recycle",
        ["Page Delete"] = "document remove",
        ["Check"] = "tick checkmark confirm done success",
        ["Checkbox"] = "tick checkmark checked",
        ["Prohibited"] = "blocked forbidden disabled ban",
        ["Power"] = "on off standby shutdown",
        ["Home"] = "house main dashboard",
        ["Link"] = "chain hyperlink url",
        ["Unlink"] = "broken chain disconnect hyperlink",
        ["Lock"] = "locked secure padlock",
        ["Unlock"] = "unlocked padlock",
        ["Loop"] = "sync synchronize repeat reload",
        ["Refresh"] = "reload reset",
        ["Rewind"] = "backward reverse",
        ["Fast Forward"] = "fastforward",
        ["Next"] = "skip next track",
        ["Previous"] = "back skip previous track",
        ["Play"] = "playback start",
        ["Play Circle"] = "playback start",
        ["Play Pause"] = "playback toggle",
        ["Play Video"] = "playback start",
        ["Pause"] = "playback",
        ["Stop"] = "playback",
        ["Record"] = "recording capture",
        ["Projection Screen"] = "projector presentation",
        ["Monitor"] = "screen display",
        ["Laptop"] = "computer notebook",
        ["Mobile"] = "phone smartphone",
        ["Mobile Signal"] = "phone smartphone wireless",
        ["Telephone"] = "phone call",
        ["Telephone In"] = "phone incoming call",
        ["Telephone Out"] = "phone outgoing call",
        ["Telephone Down"] = "phone hangup end call",
        ["Camera"] = "photo photograph snapshot",
        ["Video"] = "camera movie film",
        ["Photo"] = "image picture photograph",
        ["Mail"] = "email envelope message"
    };

    internal static string Normalize(string text) => string.Join(" ",
        new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    internal static bool Matches(string name, string query)
    {
        string normalized = Normalize(Aliases.TryGetValue(name, out var aliases) ? name + " " + aliases : name);
        return Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(part => normalized.Contains(part, StringComparison.Ordinal));
    }
}
