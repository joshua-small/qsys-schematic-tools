# Schematic Tools

Schematic Tools 0.17 is an optional selection-diagnostics mod for developers. Start Designer with `/mods-dev`, or use an already-enabled native development mode, to reveal **Tools > Q-SYS Mods > Schematic Tools — Selection Diagnostics**. The mod never enables native `/dev` itself. Ordinary startup hides its entry.

The former user tools now live in separate repositories:

- [Better Packing](https://github.com/joshua-small/qsys-better-packing)
- [Named Signal Colors](https://github.com/joshua-small/qsys-named-signal-colors)
- [Snapshot Inspector](https://github.com/joshua-small/qsys-snapshot-inspector)
- [Better Shortcuts](https://github.com/joshua-small/qsys-better-shortcuts)
- [Color Palette Aligned](https://github.com/joshua-small/qsys-color-palette-aligned)
- [Icons Filter](https://github.com/joshua-small/qsys-icons-filter)
- [Component Controls Info Filter](https://github.com/joshua-small/qsys-component-controls-info-filter)

All use [Q-SYS Mods Core](https://github.com/joshua-small/qsys-mods-core) 0.1.x. Automatic Geometry remains independent. Preferences appear in the native File > Preferences dialog and operation windows under Tools > Q-SYS Mods.

Use Q-SYS Mod Manager 0.8+ and **Split Schematic Tools** to migrate the older bundle. The transaction preserves unrelated pins, profile identity, package history, and Undo. Named Signal Colors imports the existing palette without modifying its original file. Do not manually enable a pre-0.17 bundle beside the replacements.

The public history through commit `13c52e6` preserves the bundled implementation, its tests, and prior application evidence. The diagnostics window reports selected object types/labels/positions and host/undo state, with a 256-object limit. Its separate **Debug: Move selection down 20** button retains the original explicit mutation, validation, grouped undo, and required diagnostic receipts. Opening or reading the window does not move objects.

While the diagnostics window is open, selection changes refresh its report automatically. Document loading, completed edits/undo, connection changes, and returning to the window also request a refresh. Bursts share one deferred UI-thread read of the current state. Closing the window detaches its notifications and cancels its pending refresh; reopening creates a fresh subscription.

**Read current selection** remains available for an immediate read. If automatic notification setup fails, the window shows a manual-only status. A capture failure replaces the report with the error; a later valid selection can recover. During document loading, the window shows a temporary loading message rather than the previous document's report.

Build `Designer.Modules.SelectionProbe.csproj` with .NET 10.0.112, an adjacent Core checkout, and local `DesignerDir` references. The exact approved Designer 10.5 binaries are required; no new Designer or hardware compatibility is implied.

Run the portable selection-report regressions without Designer or Core:

```sh
dotnet run --project tests/SelectionReport.Tests/SelectionReport.Tests.csproj --configuration Release
```

The suite tests the production report helper, refresh lifecycle, and notification
adapter using synthetic publishers. See
[selection report validation](docs/SELECTION-REPORT-VALIDATION.md) for coverage,
error boundaries, and the separate native lifecycle acceptance procedure.
The automatic-refresh source builds, but its native Designer acceptance remains
pending. Portable checks do not establish native event timing or compatibility.

[Split and release tracking](https://github.com/joshua-small/qsys-schematic-tools/issues/4).
