using Designer.Modules.SelectionProbe;

var palette = SignalColorRules.Defaults();
int passed = 0;
SignalPinEvidence Pin(string name = "Unnamed role", int domain = 1, int direction = 2,
    string component = "gain", string pin = "output", string color = "#D5DCEF", bool? custom = false, string provider = "Gain",
    string current = "#D5DCEF")
    => new(name, domain, direction, component, provider, pin, color, custom, current);
void Check(string title, SignalPinEvidence[] pins, string? expected, SignalRole? forced = null, bool defaultsOnly = false)
{
    var actual = SignalColorRules.Choose(pins, palette, forced, defaultsOnly);
    if (actual.Color != expected) throw new Exception($"{title}: expected {expected ?? "unchanged"}, got {actual.Color}: {actual.Reason}");
    passed++;
}
Check("AEC reference takes precedence over source fill", [Pin(component: "gain", color: "#00FF00"), Pin(direction: 1, component: "acoustic_echo_canceler_simd", pin: "reference", color: "#C060C0")], "#FF00FF");
Check("AEC microphone is not reference magenta", [Pin(direction: 1, component: "acoustic_echo_canceler_simd", pin: "microphone", color: "#C060C0")], "#41F8FF");
Check("AEC processed output stays near end", [Pin(component: "acoustic_echo_canceler_simd", color: "#C060C0")], "#41F8FF");
Check("Bluetooth blue from abbreviated name", [Pin("BT feed L", component: "input_box")], "#0080FF");
Check("Bluetooth blue from component identity", [Pin(component: "bluetooth_input")], "#0080FF");
Check("Control pin on Bluetooth stays control", [Pin(domain: 2, component: "bluetooth_input")], "#3DF436");
Check("Speaker output before far end name", [Pin("FE Speakers Out")], "#FFFF00");
Check("Far end", [Pin("Processed Far-end Caller")], "#0F50FF");
Check("Near end", [Pin("Near-end Microphone")], "#41F8FF");
Check("False Bluetooth substring", [Pin("SUBTOTAL")], null);
Check("Global mute", [Pin("Privacy Mute", 2)], "#FF001D");
Check("Ordinary unspecified mute stays control", [Pin("Mute", 2)], "#3DF436");
Check("Status fault remains red", [Pin("Status Fault", 2)], "#FF0000");
Check("Generic status stays white", [Pin("Device Status", 2)], "#FFFFFF");
Check("LED mute", [Pin("LED Mute", 2, current: "#FF0000")], "#FF0000");
Check("PhoneMute camel case", [Pin("PhoneMute", 2, current: "#009F3C")], "#009F3C");
Check("ConfMute camel case", [Pin("ConfMute", 2, current: "#8000FF")], "#8000FF");
Check("HIDMute acronym case", [Pin("HIDMute", 2, current: "#6EC6F1")], "#6EC6F1");
foreach (var name in new[] { "OnHook", "OffHook", "Call Active" })
    Check("Phone control " + name, [Pin(name, 2)], "#009F3C");
Check("LED endpoint", [Pin(domain: 2, component: "usb_telephony", pin: "spk_led_mute")], "#FF0000");
Check("Phone endpoint", [Pin(domain: 2, component: "usb_telephony", pin: "spk_phone_mute")], "#009F3C");
Check("Privacy name wins over shared phone endpoint", [Pin("Privacy Mute", 2, component: "usb_telephony", pin: "spk_phone_mute")], "#FF001D");
Check("HID name wins over shared LED endpoint", [Pin("HIDMute", 2, component: "usb_telephony", pin: "spk_led_mute")], "#6EC6F1");
Check("Conflicting endpoints skipped", [Pin(domain: 2, component: "usb_telephony", pin: "spk_led_mute"), Pin(domain: 2, component: "usb_telephony", pin: "spk_phone_mute")], null);
Check("Status okay", [Pin("Status Okay", 2)], "#00FF00");
Check("Status compromised source endpoint", [Pin(domain: 2, component: "status_combiner", pin: "compromised")], "#FF7F00");
Check("Status receiver identifies generic status, not a specific severity", [Pin(domain: 2, direction: 1, component: "status_combiner", pin: "input")], "#FFFFFF");
Check("Test status", [Pin("Test Status", 2)], "#CCCCCC");
foreach (var (name, color) in new[] { ("System Startup Trigger", "#00C800"), ("System Shutdown Trigger", "#FF0000"),
    ("System Sleep Trigger", "#0000FF"), ("System Wake Trigger", "#6EC6F1"), ("Paging", "#FF0000"),
    ("Fire Alarm", "#FF0000"), ("Send Email", "#FF0000"), ("Timer Start", "#FF0000") })
    Check(name, [Pin(name, 2)], color);
Check("Conflicting name roles skipped", [Pin("Startup Shutdown Trigger", 2)], null);
Check("Page visibility is not paging", [Pin("UI PIN Page Visibility", 2)], "#3DF436");
Check("Phone substring not a role", [Pin("Microphone Gain", 2)], "#3DF436");
Check("Phone audio is not control green", [Pin("FE Phone Audio")], "#0F50FF");
Check("Default setting recolors custom color", [Pin("PhoneMute", 2, current: "#123456")], "#009F3C");
Check("Default setting still normalizes audio", [Pin("NE Mic", current: "#00FFFF")], "#41F8FF");
Check("Optional filter preserves custom color", [Pin("PhoneMute", 2, current: "#123456")], null, defaultsOnly: true);
Check("Optional filter colors default", [Pin("PhoneMute", 2, current: "#d5dcef")], "#009F3C", defaultsOnly: true);
Check("Optional filter skips complete mixed-color group", [Pin("PhoneMute", 2), Pin("PhoneMute", 2, current: "#123456")], null, defaultsOnly: true);
Check("Optional filter applies to audio", [Pin("NE Mic", current: "#00FFFF")], null, defaultsOnly: true);
Check("Optional filter applies to manual role", [Pin(current: "#FFFFFF")], null, SignalRole.Bluetooth, defaultsOnly: true);
Check("Manual role colors default through filter", [Pin()], "#0080FF", SignalRole.Bluetooth, defaultsOnly: true);
Check("Manual role can recolor custom with filter off", [Pin(current: "#FFFFFF")], "#0080FF", SignalRole.Bluetooth);
Check("Speaker domain", [Pin(domain: 4)], "#FFFF00");
Check("Unsupported domain not silently audio", [Pin(domain: 8)], null);
Check("Mixed domains reject even forced role", [Pin(), Pin(domain: 2)], null, SignalRole.Bluetooth);
Check("Manual role overrides inference", [Pin("AEC Ref Mix")], "#0080FF", SignalRole.Bluetooth);
Check("Source component color clue", [Pin(color: "#7040C0")], "#7040C0");
Check("Custom fill excluded from fallback", [Pin(color: "#7040C0", custom: true)], null);
Check("Unknown custom status excluded", [Pin(color: "#7040C0", custom: null)], null);
Check("Receiver fill not used as source color", [Pin(direction: 1, color: "#7040C0")], null);
Check("Conflicting source fills skipped", [Pin(color: "#7040C0"), Pin(color: "#0080FF")], null);
Check("Neutral source fill skipped", [Pin(color: "#808080")], null);
Check("No pins skipped", [], null);
palette[SignalRole.Bluetooth] = "#1256aB";
Check("Editable role color", [Pin("Bluetooth")], "#1256AB");
foreach (var bad in new[] { "red", "#FFF", "#0080FF80", "#GG00FF", "0080FF" })
{
    try { SignalColorRules.Normalize(bad); throw new Exception("Accepted invalid color: " + bad); }
    catch (ArgumentException) { passed++; }
}
if (HostFields.OptionalText(new PinWithCodeName(null), "CodeName") != "")
    throw new Exception("An existing null CodeName must be treated as absent evidence, not a missing field.");
passed++;
if (HostFields.OptionalText(new PinWithCodeName("reference"), "CodeName") != "reference")
    throw new Exception("A populated pin CodeName must retain its role evidence.");
passed++;
try
{
    HostFields.OptionalText(new object(), "CodeName");
    throw new Exception("A genuinely missing CodeName field must still reject an incompatible host object.");
}
catch (MissingFieldException) { passed++; }
Console.WriteLine($"Passed {passed} signal color rule and metadata checks.");

sealed class PinWithCodeName(string? codeName)
{
    public string? CodeName = codeName;
}
