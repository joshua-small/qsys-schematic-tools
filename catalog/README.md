# Mod Manager content

This repository owns the content displayed for Schematic Tools in Q-SYS Mod Manager.

- Edit `mod.json` for the description, GitHub username, artwork path and help link.
- Keep artwork in this directory. `icon.ico` is the existing Q-SYS Mod Manager mark, copied here as starter artwork; replace it with a mod-specific PNG, JPEG or ICO (up to 1 MiB).
- Add `releases/<tag>.md` before tagging a release (for example `v0.15.0.md`). These UTF-8 notes are displayed verbatim as plain text; no AI summary or README scraping is involved.
- Publish a stable GitHub release with exactly one already-built `.qdsmod` asset. The release workflow verifies it using the manager's approved package rules, then attaches `qmm-catalog.json`.
- The manager's scheduled catalog workflow reads that record, verifies its source commit, image and package, and proposes a catalog update for review. Publishing a mod release alone does not approve it for the manager.
- The record pins content to a commit and cannot be replaced. Correct content in the next release; never replace a published package or delete historical assets.

For the existing v0.15.0 release, run **Mod catalog → Run workflow** on main after this change is merged, choose `v0.15.0`, and set **Content ref** to the merged content commit. This explicit migration supplies content that the original tag predates, without moving the tag or rebuilding its package. For future releases, leave Content ref empty to use the release tag.

The ID, canonical package name, module filenames, supported Designer build and shared dependency hashes remain manager-controlled approval rules. A new mod or support change still requires manager review. Workflow tooling is pinned to a reviewed manager commit; update both workflow references together when adopting a new contract.
