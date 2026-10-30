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

Types use their CLR full name. Labels use the selected item's public `Label`
property when present. A selected graphic without that property reads the public
`Label` on its underlying public `obj` design object. This fallback is one level
only; the reported type and position still belong to the selected graphic.
Each capture reads the current label, so a later rename is reflected without
changing an earlier captured report. Missing or null values remain JSON null.
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
JSON escaping, graphic/model labels and rename recapture, and rejection of failing getters/readers, null items, cycles,
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
polling timer. Capture and dispatcher posting run outside the session lock, so
worker notifications do not wait for a UI capture to finish. A nested dispatcher
frame or manual Read cannot reenter an active capture; it requests one following
read. Subscription setup and error reporting also run outside that lock.

The publisher identities are checked before each read. Changed publishers replace
the old subscription set; ordinary document replacement on the same manager
needs no document-specific subscription. Activating the window requests a read
as well. While `IsLoading` is true, the UI replaces its report with a loading
message and waits for a later notification or manual Read. Unknown loading state
is an error, not an assumed usable document. Manager, document and global selection
identities are checked again after serialization. A changed/loading document
discards the report and requests a fresh capture. The window also verifies that
the original output control still belongs to the current window before publishing
a result or error, so a closed window cannot overwrite a reopened report.

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

Core's host compatibility policy and the existing developer-mode gate still
apply. Core 0.1.10+ may also permit a different runtime through an existing
installation-specific exact-build opt-in. That consent does not establish native
acceptance for this feature. The adapter checks event parameter types and public add/remove methods
before subscribing. These are internal host contracts, not a supported vendor
extension API; unknown contracts fall back to manual diagnostics.

The portable suite covers coalescing, worker-thread notification delivery,
latest-state reads, manual cancellation, subscribe/close/reopen, nested dispatcher
and manual-read reentrancy, nonblocking worker notifications, document identity guards,
reader/posting failures, exact reflected instance/static delegates, partial setup
rollback, and cleanup after a removal failure. It compiles the production
`SelectionNotifications` adapter against a test-only Core resolution boundary:
synthetic publishers exercise all four sources, publisher replacement, rollback,
unknown loading state, and close/reopen. This proves the adapter's routing logic
against those inputs, not that Designer emits the assumed events in every mode.

## Recorded 0.17.2 acceptance

The module with SHA-256
`f5180855e84c239ed3639df84f007f69679103f38e1ebf2b7f9a57769530eae8`
was checked on Designer 10.5.0 with Mods Core 0.1.11. These results cover the
label correction and bounded automatic-refresh cases, not every lifecycle or
host configuration:

- Empty, single and multiple selections, plus a rapid sequence ending on a
  distinct component, produced the expected labels automatically. Each bounded
  selection sequence produced one report. The recorded component fields, dirty
  state and history remained unchanged during those selection checks.
- A native transaction changed a selected component's label, position and size.
  The report followed label/position edits, Undo and Redo without reselection.
  Final Undo restored the recorded component fields; manual Read retained those
  fields and a clean document.
- Page switching and native document replacement were exercised. Replacement
  changed the document reference and cleared the previous selection.
- F6/F7 updated mode reporting. Local Emulation was observed; this does not
  establish live Core behavior.
- One diagnostics-window close/reopen cycle produced no new reports during
  selection changes while closed, then two reports on reopening with the current
  component. This is not application cold-reopen/persistence acceptance, repeated
  shutdown-race coverage, or a general one-report-per-action guarantee.

Selection notifications and edit/Undo/Redo transactions used native operations.
Rendered reports, manual Read, page switching, F6/F7 and diagnostics-window
closing/reopening were observed in Designer. The 82 portable checks remain
separate evidence for synthetic report, getter-failure and lifecycle behavior.

After F7, Designer reported `dirty=true` with undo, redo and current history
counts all zero. The recorded fields of the three components matched baseline.
Complete live-document equality was not established. Original and initial-copy
bytes were preserved, and the dirty state was saved separately. The later
controlled 10.5.1 comparison below addresses the dirty-state question separately.

### Designer 10.5.1

The same module hash and Mods Core 0.1.11 passed bounded checks on Designer
10.5.1. Automatic reports followed single, multiple, empty and rapid-final
selections; recorded component fields, dirty state and history stayed unchanged.
A native label/position edit, Undo and Redo updated the report without
reselection. Final Undo restored the recorded fields and a clean document.
Manual Read preserved those fields and history. Native document replacement
cleared selection/history, then reported the correct newly selected component.
One diagnostics close/reopen cycle stopped reports during the observed closed
interval and reported the current component after reopening.

Selection/edit operations used native application operations; displayed reports,
manual Read, page navigation, Emulation and window lifecycle were also observed
in the application. Page navigation alone does not establish receipt routing
for a page-change event. Manual Read and reopen each produced two receipts, so
these checks do not establish one report per physical action. An initial 800 ms
observation was too early; the automatic report arrived about 1.28 seconds after
that selection operation. Waiting for a fresh report resolved the observation
without replaying the selection or changing the product. No latency guarantee
is claimed. The 54 native/file assertions are separate from the GUI observations
and the 82 portable checks.

### Controlled F7 comparison on 10.5.1

The same source design was opened in two application copies: one with the
tested mod and one without product mods. The latter retained profile-isolation
instrumentation; it was not an entirely uninstrumented process. Each arm saved
a load-only control, entered local Emulation, left with F7, and saved separately,
without intervening schematic or control edits.

Both arms became dirty after F7 and had unavailable Undo/Redo commands. The
modded arm's numeric undo/redo/current history counts were all zero; numeric
history was not read in the comparison arm. Each saved result added four
settings files and nine cached control values. The nine recorded values matched
between arms. Recorded component, pin and property fields stayed stable from
each load-only save to its post-F7 save. Both load-only controls shared the same
earlier component-size normalization.

This reproduces the dirty state without Schematic Tools and is consistent with
Designer synchronizing settings when leaving Emulation. The mod does not clear
that state. Settings XML was byte-identical or differed only in GUID values;
the binary global snapshots differed and their semantic equivalence was not
established. Neither complete live-document equality nor every stored field or
wire endpoint was established. This comparison does not imply every F7 action
always makes a document dirty.

Native unsupported-selection/getter-failure recovery, pending-dispatch shutdown
races, full application cold reopen/persistence, and live Core/hardware checks
were not run. These bounded results do not establish every feature or host
configuration. Exact-build consent and compilation do not establish acceptance
of another Designer build.

## Native acceptance procedure

Portable checks and a full WPF build do not establish native automatic-refresh
acceptance. Record the following cases from actual application observations,
separately from synthetic tests or a successful build.

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
