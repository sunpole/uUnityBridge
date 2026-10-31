# P0004 — Windows EXE updater, rollback and UI text

## Goal and status

Development stage. Do not merge or publish a release before Windows QA.
No existing Windows installation is modified simply by merging a PR.

### Features implemented in the branch
- Two self-contained Win-x64 programs: `uUnityBridge.exe` and `uUnityBridge.Updater.exe`.
- On desktop startup, and every 30 minutes thereafter, query stable GitHub Releases
  through the authenticated official GitHub CLI, for `sunpole/uUnityBridge` only.
- Read version tag `vMAJOR.MINOR.PATCH`, require strictly newer version, no
  draft/prerelease, require exactly named EXE and SHA256 assets.
- Bound EXE size to 150 MB, SHA file to 1 KB. Stage in `%LOCALAPPDATA%\uUnityBridge\updates`.
- Verify SHA256 and embedded Win32 file version before helper launch.
- Helper waits for parent to exit, saves the original EXE in `backups`,
  replaces only its sibling `uUnityBridge.exe`, starts the new process, waits for
  window-ready handshake, and on startup failure restores previous EXE.
- Header link `↶ Откат версии` appears when a backup exists for this exact
  installation directory. Manual rollback blocks the reverted version's SHA,
  avoiding automatic reinstallation of the same binary.
- Failing or manually reverted binary SHA is blocked from retries. A fresh
  later release with a different hash remains eligible.
- Existing answers.json and report-cursor.json are untouched.
- Correct `СКОПИРОВАТЬ` hint to `ОТЧЁТ`.

### Trust caveat

The SHA256 file and EXE are both from the *same* GitHub Release. This verifies
transfer integrity relative to that release; it is **not** a digital publisher
signature and it does not protect against someone who controls the repo release.
The production hardening plan is to add Authenticode signing and GitHub
immutable releases; neither is implemented or claimed.

### No auto-update from Issues

A public GitHub Issue cannot install a binary, run a script, call admin tools
or request arbitrary file writes. `sync-main` remains source-only.

## Required QA before merge

- [ ] New Windows CI builds both EXEs and creates SHA256 sidecar.
- [ ] Regression tests pass (queue, report cursor, release tag validation).
- [ ] Launch both EXEs from new, *separate* QA folder (do not overwrite 0.3.0).
- [ ] Confirm title `0.4.0`, one `ОТЧЁТ` button, no old `СКОПИРОВАТЬ` hint.
- [ ] With no published Release, older EXE does not restart unexpectedly.
- [ ] Create a controlled test Release of higher version only **after** additional
      test/approval; verify GitHub assets and SHA256 are correct.
- [ ] Test EXE update, restart, window-ready handshake and local backup.
- [ ] Corrupt a *test* asset and ensure update refuses it without replacing EXE.
- [ ] Exercise manual rollback and ensure old EXE is restored, with previous
      release blocked from reinstalling.
- [ ] Exercise launch-failure rollback in isolated QA directory.
- [ ] Verify existing ANS history and copied report cursor survive restart.
- [ ] Inspect local updater log and that nothing sensitive reached GitHub.
- [ ] Confirm .NET 8 to .NET 10 LTS migration is separately planned.

## Initial migration of user 0.3.0

Existing 0.3.0 has no updater and cannot receive this as a binary patch. Once
P0004 passes validation, user must **once** download both EXEs from the
verified CI artifact and run them from a clean folder. Afterward only
approved **stable Releases** can update the EXE automatically.

## Release publication

Only the manually triggered GitHub Actions workflow
`.github/workflows/release-windows.yml` on `main` publishes a stable version.
It refuses a version mismatch and checks compilation/tests before creating
three assets: main EXE, companion updater EXE and main SHA256 sidecar.
The companion updater EXE remains alongside the installed main program, and is
not currently replaced automatically by the helper itself.

Official CLI documentation:
- https://cli.github.com/manual/gh_release_view
- https://cli.github.com/manual/gh_release_download
- https://cli.github.com/manual/gh_release_create
