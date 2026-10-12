using System.Globalization;
using System.Text.RegularExpressions;

namespace Designer.Modules.SelectionProbe;

internal enum SignalRole
{
    NearEnd, FarEnd, AecReference, AudioOutput, Control, GlobalMute, Status, Bluetooth,
    LedMute, Phone, Conference, HidMute, StatusOkay, StatusCompromised, StatusFault, StatusTest,
    Startup, Shutdown, Sleep, Wake, Paging, FireAlarm, Notification, TimerTrigger
}
internal sealed record SignalPinEvidence(string Name, int Domain, int Direction, string ClassName,
    string ProviderName, string PinName, string ComponentColor, bool? CustomColorApplied,
    string CurrentColor = "#D5DCEF");
internal sealed record SignalColorChoice(string Role, string? Color, string Reason);

/// <summary>Editable starting conventions; these heuristics are not a QSC signal-type contract.</summary>
internal static class SignalColorRules
{
    internal static Dictionary<SignalRole, string> Defaults() => new()
    {
        [SignalRole.NearEnd] = "#41F8FF", [SignalRole.FarEnd] = "#0F50FF",
        [SignalRole.AecReference] = "#FF00FF", [SignalRole.AudioOutput] = "#FFFF00",
        [SignalRole.Control] = "#3DF436", [SignalRole.GlobalMute] = "#FF001D",
        [SignalRole.Status] = "#FFFFFF", [SignalRole.Bluetooth] = "#0080FF",
        [SignalRole.LedMute] = "#FF0000", [SignalRole.Phone] = "#009F3C",
        [SignalRole.Conference] = "#8000FF", [SignalRole.HidMute] = "#6EC6F1",
        [SignalRole.StatusOkay] = "#00FF00", [SignalRole.StatusCompromised] = "#FF7F00",
        [SignalRole.StatusFault] = "#FF0000", [SignalRole.StatusTest] = "#CCCCCC",
        [SignalRole.Startup] = "#00C800", [SignalRole.Shutdown] = "#FF0000",
        [SignalRole.Sleep] = "#0000FF", [SignalRole.Wake] = "#6EC6F1",
        [SignalRole.Paging] = "#FF0000", [SignalRole.FireAlarm] = "#FF0000",
        [SignalRole.Notification] = "#FF0000", [SignalRole.TimerTrigger] = "#FF0000"
    };

    internal static string Title(SignalRole role) => role switch
    {
        SignalRole.NearEnd => "Near-end audio", SignalRole.FarEnd => "Far-end audio",
        SignalRole.AecReference => "AEC reference", SignalRole.AudioOutput => "Audio output",
        SignalRole.GlobalMute => "Global / privacy mute", SignalRole.LedMute => "LED mute indication",
        SignalRole.Phone => "Phone / call control", SignalRole.Conference => "Conference control",
        SignalRole.HidMute => "HID mute", SignalRole.StatusOkay => "Status: okay",
        SignalRole.StatusCompromised => "Status: compromised", SignalRole.StatusFault => "Status: fault",
        SignalRole.StatusTest => "Status: test", SignalRole.FireAlarm => "Fire alarm",
        SignalRole.Notification => "Alert / notification", SignalRole.TimerTrigger => "Timer trigger",
        _ => role.ToString()
    };

    internal static string Normalize(string text)
    {
        var value = text.Trim();
        if (!Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Enter an opaque RGB color as #RRGGBB, for example #0080FF.");
        return value.ToUpperInvariant();
    }

    private static string Words(string text) => Regex.Replace(
        Regex.Replace(text, @"([a-z0-9])([A-Z])", "$1 $2", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        @"([A-Z])([A-Z][a-z])", "$1 $2", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Replace('_', ' ');
    private static bool Match(string text, string pattern) => Regex.IsMatch(Words(text), pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool Aec(SignalPinEvidence p) => p.ClassName.StartsWith("acoustic_echo_canceler", StringComparison.OrdinalIgnoreCase);
    private static bool UsefulComponentColor(string color)
    {
        if (color == "#D5DCEF") return false; // Common uninformative fill in the inspected designs.
        var c = Normalize(color);
        var channels = new[] { c.Substring(1, 2), c.Substring(3, 2), c.Substring(5, 2) }
            .Select(x => int.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
        return channels.Max() - channels.Min() >= 32;
    }

    internal static SignalColorChoice Choose(IReadOnlyList<SignalPinEvidence> pins,
        IReadOnlyDictionary<SignalRole, string> palette, SignalRole? forced = null, bool defaultColorsOnly = false)
    {
        SignalColorChoice Pick(SignalRole role, string reason) => new(Title(role), Normalize(palette[role]), reason);
        SignalColorChoice Skip(string reason) => new("Unchanged", null, reason);
        if (pins.Count == 0) return Skip("No associated pins.");
        var domains = pins.Select(p => p.Domain).Distinct().ToArray();
        if (domains.Length != 1 || domains[0] is not (1 or 2 or 4))
            return Skip("Mixed or unsupported signal domain; select an audio, control, or loudspeaker group.");
        if (defaultColorsOnly && pins.Any(p => Normalize(p.CurrentColor) != "#D5DCEF"))
            return Skip("Preserved: this group contains a non-default color. Only change default #D5DCEF is enabled.");
        if (forced is { } explicitRole) return Pick(explicitRole, "Role chosen explicitly for this preview.");
        var domain = domains[0];
        var name = pins[0].Name;
        if (domain == 2)
        {
            if (Match(name, @"\b(global|privacy|master)\s+mute\b"))
                return Pick(SignalRole.GlobalMute, "Signal name identifies a global/privacy/master mute.");
            // Fixed conventions researched from reference designs, not learned from current colors.
            // Explicit names take precedence over endpoints shared by several control purposes.
            var namedRoles = new (string Pattern, SignalRole Role)[]
            {
                (@"\bLED\s+mute\b", SignalRole.LedMute),
                (@"\bHID\s+mute\b", SignalRole.HidMute),
                (@"\b(conf|conference|conferencing)\b", SignalRole.Conference),
                (@"\b(phone|telephony|on\s*hook|off\s*hook)\b|\bcall\s+(active|mute|state|status|ringing|connected)\b", SignalRole.Phone),
                (@"\b(test\s+status|status\s+test)\b", SignalRole.StatusTest),
                (@"\bstatus\s+(okay|ok)\b", SignalRole.StatusOkay),
                (@"\bstatus\s+compromised\b", SignalRole.StatusCompromised),
                (@"\bstatus\s+fault\b", SignalRole.StatusFault),
                (@"\bfire\s+alarm\b", SignalRole.FireAlarm),
                (@"\b(paging|page\s+(active|trigger|enable))\b", SignalRole.Paging),
                (@"\b(startup|start\s+up)\b", SignalRole.Startup),
                (@"\b(shutdown|shut\s+down)\b", SignalRole.Shutdown),
                (@"\bsleep\b", SignalRole.Sleep), (@"\bwake\b", SignalRole.Wake),
                (@"\b(send\s+e[- ]?mail|email\s+(alert|trigger))\b", SignalRole.Notification),
                (@"\btimer\s+(start|trigger)\b", SignalRole.TimerTrigger)
            };
            var matches = namedRoles.Where(r => Match(name, r.Pattern)).Select(r => r.Role).Distinct().ToArray();
            if (matches.Length > 1) return Skip("Name suggests conflicting control roles. Choose a role explicitly.");
            if (matches.Length == 1) return Pick(matches[0], "Fixed control-purpose rule matched the signal name.");

            var endpointRoles = new HashSet<SignalRole>();
            foreach (var p in pins)
            {
                if (p.ClassName.Equals("usb_telephony", StringComparison.OrdinalIgnoreCase))
                {
                    if (p.PinName.Equals("spk_led_mute", StringComparison.OrdinalIgnoreCase)) endpointRoles.Add(SignalRole.LedMute);
                    if (p.PinName.Equals("spk_phone_mute", StringComparison.OrdinalIgnoreCase)) endpointRoles.Add(SignalRole.Phone);
                }
                if (p.Direction == 2 && p.ClassName.Equals("status_combiner", StringComparison.OrdinalIgnoreCase))
                {
                    if (p.PinName.Equals("ok", StringComparison.OrdinalIgnoreCase)) endpointRoles.Add(SignalRole.StatusOkay);
                    if (p.PinName.Equals("compromised", StringComparison.OrdinalIgnoreCase)) endpointRoles.Add(SignalRole.StatusCompromised);
                    if (p.PinName.Equals("fault", StringComparison.OrdinalIgnoreCase)) endpointRoles.Add(SignalRole.StatusFault);
                }
            }
            if (endpointRoles.Count > 1) return Skip("Connected pins suggest conflicting control roles. Choose a role explicitly.");
            if (endpointRoles.Count == 1) return Pick(endpointRoles.Single(), "Reviewed control endpoint identifies the purpose.");
            if (Match(name, @"\b(status|online|offline|fault|compromised)\b")
                || pins.Any(p => p.ClassName.Equals("status", StringComparison.OrdinalIgnoreCase)
                    || (p.ClassName.Equals("status_combiner", StringComparison.OrdinalIgnoreCase)
                        && p.PinName.Equals("input", StringComparison.OrdinalIgnoreCase))
                    || Match(p.PinName, @"\bstatus\b")))
                return Pick(SignalRole.Status, "Control name or a status endpoint identifies status feedback.");
            return Pick(SignalRole.Control, "Control domain; no more specific role matched.");
        }
        if (domain == 4) return Pick(SignalRole.AudioOutput, "Loudspeaker signal domain.");
        if (pins.Any(p => Aec(p) && p.Direction == 1 && Match(p.PinName, @"\breference\b")))
            return Pick(SignalRole.AecReference, "Group includes an AEC reference input.");
        if (Match(name, @"\bAEC\s+(ref|reference)\b"))
            return Pick(SignalRole.AecReference, "Signal name identifies an AEC reference.");
        if (Match(name, @"\b(bluetooth|BT)\b") || pins.Any(p =>
            Match(p.ClassName, @"\bbluetooth\b") || Match(p.ProviderName, @"\bbluetooth\b")))
            return Pick(SignalRole.Bluetooth, "Audio name/component identifies Bluetooth.");
        if (Match(name, @"\b(speaker|speakers|spkr)\b|\baudio\s+out(put)?\b"))
            return Pick(SignalRole.AudioOutput, "Signal name identifies a speaker/audio output.");
        if (pins.Any(p => Aec(p) && (p.Direction == 2 || Match(p.PinName, @"\bmicrophone\b"))))
            return Pick(SignalRole.NearEnd, "AEC microphone input or processed microphone output.");
        if (Match(name, @"\b(FE|far[- ]?end|caller)\b"))
            return Pick(SignalRole.FarEnd, "Signal name identifies far-end/caller audio.");
        if (Match(name, @"\b(NE|near[- ]?end|mic|microphone)\b"))
            return Pick(SignalRole.NearEnd, "Signal name identifies near-end/microphone audio.");

        // A component's fill is only a fallback clue. Never infer reference from an AEC fill alone.
        var sources = pins.Where(p => p.Direction == 2).ToArray();
        if (sources.Length > 0 && sources.All(p => p.CustomColorApplied == false && !Aec(p)))
        {
            var colors = sources.Select(p => Normalize(p.ComponentColor)).Distinct().ToArray();
            if (colors.Length == 1 && UsefulComponentColor(colors[0]))
                return new("Source component color", colors[0],
                    "Fallback: source fills agree and report no custom color; review this provisional suggestion.");
        }
        return Skip("No clear role or useful unmodified source fill. Choose a role explicitly.");
    }
}
