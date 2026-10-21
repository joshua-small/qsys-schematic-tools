# Selection report validation

Run the portable checks from the repository root:

```sh
dotnet run --project tests/SelectionReport.Tests/SelectionReport.Tests.csproj --configuration Release
```

Use the .NET SDK selected by `global.json`. This console suite returns a nonzero
exit code on failure and links the same report, refresh-session, event-subscription
and notification-adapter sources compiled into the mod. It needs no package
feed, adjacent Core checkout, vendor assemblies, WPF,
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
portable behaviors; it does not validate Designer.

## Automatic refresh contract

The implementation for [issue #6](https://github.com/joshua-small/qsys-schematic-tools/issues/6)
subscribes only while the developer diagnostics window is open:

- The current global selection's `SelectionChanged` event.
- The design manager's `PropertyChanged` for `Document` and `IsLoading`,
  including an all-properties notification.
- The manager's `OnEndOperation` event, to refresh positions and undo state
  after edits and undo/redo without requiring a new selection.
- The connection manager's static `ConnectionChanged` event.

Notifications only queue a callback. The WPF adapter posts at `ContextIdle`,
below the host selection notification's background priority, and verifies UI
thread access before capture. Repeated requests coalesce while a read is pending.
The callback captures the current selection and host context; event arguments,
document objects and selected models are not stored for later use. There is no
polling timer.

The publisher identities are checked before each read. Changed publishers replace
the old subscription set; ordinary document replacement on the same manager
needs no document-specific subscription. Activating the window requests a read
as well. While `IsLoading` is true, the UI replaces its report with a loading
message and waits for a later notification or manual Read. Unknown loading state
is an error, not an assumed usable document.

Manual Read invalidates an older queued read. Close invalidates all queued work,
clears UI callbacks, and removes the exact subscribed delegates. Reopen starts
a new session. Partial subscription setup rolls back; if setup or rebind fails,
automatic refresh stops and manual Read remains. Report failures show the existing
base-exception error and can recover on a later notification.

The existing report schema, missing-value behavior, and 256 selected-entry bound
are unchanged. Core's independent unlocked-model reader still rejects more than
256 unique models and unsupported position reads; this mod does not silently
truncate that error or infer values. The existing explicit Debug Move button is
the only movement action. Automatic and manual reads do not start transactions,
move objects, save a design, or deploy to a Core.

Core's exact supported-host guard and the existing developer-mode gate still
apply. The adapter checks event parameter types and public add/remove methods
before subscribing. These are internal host contracts, not a supported vendor
extension API; unknown contracts fall back to manual diagnostics.

The portable suite covers coalescing, worker-thread notification delivery,
latest-state reads, manual cancellation, subscribe/close/reopen, reentrant changes,
reader/posting failures, exact reflected instance/static delegates, partial setup
rollback, and cleanup after a removal failure. It compiles the production
`SelectionNotifications` adapter against a test-only Core resolution boundary:
synthetic publishers exercise all four sources, publisher replacement, rollback,
unknown loading state, and close/reopen. This proves the adapter's routing logic
against those inputs, not that Designer emits the assumed events in every mode.

## Separate native acceptance

Portable checks and a full WPF build have passed for this candidate. Native
automatic-refresh acceptance is pending; do not mark the following cases passed
from synthetic tests or a successful build.

Before accepting the adapter in Designer, use a disposable design and check:

1. Ordinary startup hides diagnostics. The existing developer-mode option
   reveals the command without enabling native developer mode as a side effect.
2. Keep diagnostics open while changing empty, single and multiple selections,
   including a rapid sequence and clearing selection. Without pressing Read,
   confirm the latest types/labels/positions appear and the window stays responsive.
   Check manual Read separately. Selection and reads must leave positions, dirty
   state and the undo stack unchanged.
3. Edit a selected object's position/label through Designer, then undo and redo.
   Confirm the report follows the edit and host/undo state without reselection.
   Switch pages, create/open another disposable document and observe any loading
   state. No previous-document selection or error may remain after loading ends.
4. Enter and exit Emulate without closing diagnostics. Confirm current mode and
   selection after each transition. Test a live connection only in an authorized
   lab fixture; no deployment is needed for this read-only feature.
5. Close diagnostics during selection changes, change selection while closed,
   then reopen several times. Confirm one active subscription set, no late closed
   window updates and no accumulating duplicate reads. A normal burst may include
   separate host events; verify source counts/receipts rather than assuming every
   physical action emits exactly one event.
6. Exercise an unsupported/bounded selection if available, then select a supported
   object and confirm recovery. Confirm success/error receipts and loading/manual
   fallback messages. Do not alter installed vendor binaries to force a failure.
7. Check the separate **Debug: Move selection down 20** command only through an
   explicit action, with its existing guards, grouped undo and required receipts.

Record the candidate binary hash, Core version, exact Designer build, mode and
observed result for each case. Preserve earlier report/Move acceptance separately;
it does not establish acceptance of these new event and lifetime paths.
