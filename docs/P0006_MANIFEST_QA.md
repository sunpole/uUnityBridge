# P0006 — modular file manifest foundation (v0.5, stage 1)

**This is infrastructure only.** It is NOT an executable update mechanism and
does not modify the user's current uUnityBridge 0.4.2 installation.

## Current confirmed Windows installation

`C:\!CODE_CLUB\new 2026\017_uunity\uUnityBridge_App`

This folder must remain the single stable application location. Never produce
`QA-0.5.0` as a permanent installation path. Existing `QA-*` folders may be
archived only after the new system is independently verified.

## Work in this PR

- `src/PackageManifest.cs`: strict v1 manifest schema, case-insensitive
  Windows path validation, SHA-256 digest checks, total size and file limits,
  read-only directory verification, pure download diff planner.
- `tools/PackageManifestBuilder/`: standalone CI-only utility to produce a
  sorted JSON manifest from a trusted .NET publish directory. **No network,
  no local Git and no process execution**.
- `tests/uUnityBridge.Tests/`: tests to prove unchanged files are excluded
  from updates; altered and missing DLLs are selected; path traversal, NTFS
  streams, Windows device names, duplicates and malformed digests are rejected.
- `.github/workflows/v05-modular-manifest.yml`: Windows .NET build and
  upload of a **manifest only**, with no new installer or Release asset.

## Proposed stable app structure (not implemented here)

```text
uUnityBridge_App/
  Launcher.exe                always-started entrypoint
  Updater.exe                 safe out-of-process update engine
  versions/
    0.5.0/
      uUnityBridge.exe
      uUnityBridge.dll
      ...runtime DLLs...
    0.5.1/
      ... new verified version (temporary) ...
  state/current.json          pointer to active slot
```

User preferences and PATCH/ANS receipt state remain under
`%LOCALAPPDATA%\uUnityBridge`; they are not part of a version slot.

## Release and protection requirements (next PR)

**Do not auto-consume these unsigned manifests.** A SHA-256 in an untrusted
manifest is not an authentication mechanism. Add a verifiable signature or
a pinned trusted manifest digest, plus bounded retry, staged downloads,
cross-process lock and last-known-good rollback before any installation
from a remote manifest. Never execute commands contained in GitHub Issues.

Require tests for interruption, hash tampering, removed DLL, update-in-progress,
filesystem link substitution, startup failure and rollback. Keep only the
active version plus one last-known-good slot after successful QA; cleanup may
not delete a running or needed version.

The user should continue opening one stable shortcut; migration from v0.4.2
requires a one-time vetted bootstrap because its current EXE updater supports
whole-EXE releases, not modular manifests.

## QA

- [ ] Windows CI compiles manifest builder and tests.
- [ ] Real publish output generates a valid manifest.
- [ ] All path/security and delta tests PASS.
- [ ] Main/production binaries are unchanged.
- [ ] No new Release and no automatic update action.
