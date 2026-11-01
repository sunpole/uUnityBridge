# uUnityBridge v0.5 — file-manifest launcher architecture (decision proposal)

## Why the original update design is inefficient

Today both `uUnityBridge.exe` and `uUnityBridge.Updater.exe` are
`--self-contained true` `PublishSingleFile=true` .NET packages.
A tiny change in C# frequently requires redownloading the complete EXE.
This caused a v0.4.1 single-file asset of 161,774,656 bytes, which v0.4.0's
150 MB asset limit rightfully refused.

Compression is a temporary compatibility patch, not a sustainable incremental
updater strategy.

## Benchmark before choosing

Compare on Windows GitHub Actions:
1. self-contained single-file (current)
2. self-contained multi-file (same runtime included but libraries are separate)
3. framework-dependent multi-file (small app distribution, requires suitable
   **Microsoft Windows Desktop Runtime**, not merely SDK or CLI)

Workflow: `.github/workflows/package-layout-comparison.yml`, which builds
each variant, records file names/sizes/SHA-256, deletes built binaries and
uploads only JSON measurements. This experiment MUST NOT install or update
anything on the user's PC.

## Proposed default: self-contained multi-file, version slots

```text
uUnityBridge/
  Launcher.exe                 <- small, stable bootstrap; no SDK or compiler
  Updater.exe                  <- checks trusted manifest and stages updates
  versions/
    0.5.0/
      uUnityBridge.exe         <- ordinary apphost, no bundled runtime
      uUnityBridge.dll         <- compiled app code
      uUnityBridge.deps.json
      uUnityBridge.runtimeconfig.json
      *.dll                    <- required runtime and dependencies
    0.5.1/                     <- complete verified new slot
      ...
  state/
    current.json               <- selected verified version
  logs/                        <- local safe diagnostics
```

Do **not** store credentials, GitHub tokens, chat contents or user data under
mutable version folders. Keep patch ledger, report cursor, preferences and
API keys (protected with Windows credentials) in scoped LocalAppData.

## Incremental manifest update

The CI runner builds and publishes the binaries; **the compiler remains in
GitHub Actions**. A user installs only finished Windows runtime components.

Each stable release is described by a signed or pinned manifest:
- schema version, version ID, target architecture and minimum supported platform
- list of relative normalized paths (no `..`, rooted paths, symlinks, ADS, or
  path traversal), bytes and SHA-256 per file
- launcher/updater minimum version and compatibility mode
- optional package chunk digests for large assets
- issuer signature / trusted key identifier (planned for production)

Update process:
1. Check manifest (60s task polling stays independent from release polling).
2. Compare current slot file sizes and SHA-256 with trusted manifest.
3. Download only missing or modified files to staging, with resumable retries.
4. Rehash every downloaded file; reject unrecognized files/paths.
5. Assemble a complete **new version directory** reusing unchanged files from
   current verified slot. Validate resulting complete manifest before use.
6. If another patch is processing, wait for the patch to finish.
7. Atomically switch `current.json` only when the new slot has passed tests.
8. Restart into new slot; verify health handshake.
9. If health check fails, point back at previous complete slot. Keep logs and
   preserve the ability to launch the last known-good version.

No in-place patching of running EXE or DLL, and never deleting last known good
unless retention and free space conditions are satisfied. Ensure only one
updater executes by cross-process lock/mutex.

Two-stage bootstrap: existing 0.4.x single-file updater CANNOT interpret a new
manifest; initial migration to v0.5 requires a one-time vetted installer or
bootstrap that installs Launcher/Updater and new slot side by side. Do not
silently switch formats through a GitHub Issue.

## Trade-offs

- Self-contained multi-file: one-time large initial transfer, many files, no
  global runtime install. Small application-only updates afterward.
- Framework-dependent multi-file: smallest package, depends on installed
  Microsoft.WindowsDesktop.App runtime; coordinate .NET patches separately.
- Native launcher: smallest independent bootstrap but more implementation work
  and another language/toolchain; not required for first experiment.

Only claim actual sizes or performance after CI measurements and Windows QA.
Avoid blanket PublishTrimmed=true for WinForms unless compatibility and dynamic
code reflection are validated. Consider upgrading to supported .NET 10 LTS
separately.

## Security

SHA-256 of files **in the same release** provides integrity checks but does not
authenticate the publisher. Stronger guarantees require protection of publishing
permissions and ideally signatures for manifest and binaries. Untrusted GitHub
Issues must not be able to change the distribution channel, launch commands or
rewrite the file list. Never change Windows security policies automatically.
DeepSeek is a separate optional one-time $5 trial and NOT a runtime dependency.

## Acceptance for an implementation phase (not complete)

- [ ] Size comparison CI verified on all 3 layouts.
- [ ] Launcher/updater state-machine and file validation tests.
- [ ] Controlled migration from 0.4.x on a disposable QA folder.
- [ ] Differential update with only one changed DLL (verify bytes downloaded).
- [ ] Missing/corrupted DLL healing and recovery from interrupted download.
- [ ] Rollback after failed health handshake.
- [ ] No duplicate update while a PATCH is still running.
- [ ] No loss of ANS/report state; no file writes outside allowlisted folders.
- [ ] Sign and verify update manifest before production automatic use.

References:
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/dotnet/articles/core/deploying/
- https://docs.github.com/en/rest/releases/releases
