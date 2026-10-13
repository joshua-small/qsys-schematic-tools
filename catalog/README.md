# Mod Manager content

This repository owns the content displayed for Schematic Tools in Q-SYS Mod Manager.

- Edit `mod.json` for the description, GitHub username, artwork path and repository help link.
- Keep artwork in this directory as PNG, JPEG or ICO (at most 1 MiB). The thumbnail uses the supplied Avitex icon; source artwork and provenance are in [artwork/](artwork/README.md).
- Add `releases/<tag>.md` before tagging. Notes are shown verbatim as plain text.
- Publish a stable GitHub release containing exactly one already-built `.qdsmod` asset. The pinned release workflow verifies it and attaches an immutable `qmm-catalog.json`.
- The separate [approved catalog](https://github.com/joshua-small/qsys-mod-catalog) verifies the source commit, artwork and package, then publishes routine releases automatically. New mod identities and file-layout changes require initial catalog review; they do not require manager code changes.
- Never replace a published package or descriptor or delete historical assets. Correct presentation in the next release. The explicit workflow `content_ref` input can supply content for historical tags without moving them.

Version 0.15.1 uses a schema-2 package without an enable marker. QMM installs or removes the module to enable or disable it. Exact Designer build checks remain. Release it only after a manager with schema-2 and signed-feed support (0.7.0 or newer) is available; older managers cannot read the new package format.

The supported Designer hashes, repository identity and permitted file layouts come from reviewed catalog approvals. Harmony and its license remain shared dependencies of this mod, but are not required of unrelated mods.

For packaging and onboarding details, see the [package contract](https://github.com/joshua-small/qsys-mod-catalog/blob/main/PACKAGE-CONTRACT.md). Both reusable-workflow and tool references must use the same reviewed manager commit.
