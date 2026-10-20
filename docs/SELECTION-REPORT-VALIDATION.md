# Selection report validation

Run the portable checks from the repository root:

```sh
dotnet run --project tests/SelectionReport.Tests/SelectionReport.Tests.csproj --configuration Release
```

Use the .NET SDK selected by `global.json`. This console suite returns a nonzero
exit code on failure and links the same `SelectionReport.cs` compiled into the
mod. It needs no package feed, adjacent Core checkout, vendor assemblies, WPF,
editor process, or saved design. All inputs are synthetic.

## Portable contract

The report retains the existing `selection`, `hostState`, and `unlockedObjects`
JSON fields. Each selected entry contains `type`, `label`, and `position`.
Selection order and duplicates are preserved; at most 256 entries are read.
The bound applies only to the selected entries, not the unlocked-object payload.

Types use their CLR full name. Missing or null properties remain JSON null.
A label of another type remains null; a non-null position uses its own
`ToString()` representation. Empty strings remain empty. These are diagnostics,
not inferred schematic geometry or invented labels.

Selected values are captured before the host-state and unlocked-object readers,
which run once each, in that order. Enumeration, reflection, reader and
serialization failures propagate to the existing window error handler. No
partial report is returned as a successful capture; a serialization failure
prevents the existing success receipt call. The UI still displays the base
exception message and calls its existing error-reporting path.

The checks cover empty/single/multiple selections, 255/256/257 entry boundaries,
bounded enumeration and disposal, unknown values, nested host payloads,
JSON escaping, and rejection of failing getters/readers, null items, cycles,
unsupported values and non-finite numbers. Linking the helper proves these
portable behaviors; it does not validate the native adapter or window lifecycle.

## Separate native acceptance

Automatic refresh remains the separate feature in
[issue #6](https://github.com/joshua-small/qsys-schematic-tools/issues/6).
This change adds no selection subscription, polling, dispatcher scheduling or
document-lifecycle behavior.

Before accepting the adapter in Designer, use a disposable design and check:

1. Ordinary startup hides diagnostics. The existing developer-mode option
   reveals the command without enabling native developer mode as a side effect.
2. Opening the window and pressing **Read current selection** report empty,
   single and multiple selections, types/labels/positions, and current host/undo
   context. Reading never moves objects or creates an undo step.
3. Close/reopen the report, change the active document and explicitly read
   again. Confirm the window uses the active host context and releases closed
   window references. Confirm error text/reporting and success receipts through
   the native adapter; portable exception tests do not establish that behavior.
4. Check the separate **Debug: Move selection down 20** command only through an
   explicit action, with its existing guards, grouped undo and required receipts.

When automatic refresh is implemented, its additional acceptance must establish
the actual host notification and dispatcher rules, rapid-change handling,
document/session replacement, unsubscribe on close, no duplicate subscriptions
on reopen, and no mutation on selection changes. Retain explicit manual Read.
Those checks remain unperformed by this portable suite.
