# Editor shortcut checks

`EditorProbe` is a lab-only module for a blank, isolated copy of the pinned Q-SYS
Designer 10.5 build. It requires `.selection-probe-lab` and a matching
`.editor-lab-probe.json` with `ApplicationDirectory` and a fresh `OutputDirectory`.
Never install the probe into regular Designer or open a user design for this test.

Build the main project and `tests/EditorProbe/EditorProbe.csproj` with .NET 10,
`-p:DesignerDir=<local Designer installation>`, and separate `--artifacts-path`
directories. Run the resulting modules only in the Windows lab copy. The probe
refuses a different host hash, an existing result, or a non-untitled document.
Its `result.json` records command registration and completed checks.

For 0.16.0, the isolated native check passed on 2026-10-13:

- Seven Ctrl+Alt bindings, including D for light/dark mode and 0 for all sidebars;
  92 existing native gesture entries were recorded. Duplicate installation was
  rejected without changing existing binding counts.
- Each individual sidebar toggled and restored. All three collapsed together,
  expanded together, and a mixed layout collapsed first and expanded next.
- Light/dark mode changed and returned to its initial state. Programmer Mode,
  Controls Info forwarding of all seven bindings, and existing search checks passed.
- Both Release builds completed with zero warnings and errors.

These checks invoke registered routed commands, not physical keyboard input.
Alternate keyboard layouts, text/script focus, regrouped/floating/closed sidebar
layouts and other detached windows have not received native application checks.
The all-sidebar implementation excludes closed/floating panels and resolves each
shared tab container once. The test restores the original sidebar and theme state,
does not save a design, and does not connect to a Core.

The theme action invokes Designer's existing `ColorSchemeToggle.Command`, observed
in the pinned assembly, and retains its temporary behavior. Q-SYS documents that
the [title-bar theme toggle](https://help.qsys.com/Content/Schematic_Library/preferences.htm)
reverts to the saved preference on the next launch. The command and sidebar
container access are private implementation details, not a public extension SDK.

Tracking: [display shortcuts](https://github.com/joshua-small/qsys-schematic-tools/issues/2).
