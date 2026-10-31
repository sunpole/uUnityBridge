# uUnityBridge · Windows desktop patch transport

An early **Windows Forms .NET 8** prototype for exchanging `PATCH` / `ANS` messages via public GitHub Issues and applying tightly restricted, Git-verified source updates.

**Status: prototype, source written but Windows build and GUI are NOT verified yet.** It is not a fully tested installer/updater or Unity integration.

## Buttons

- **В ЧАТ →** — send a minimal, de-identified status message as a GitHub Issue or reply to a selected PATCH issue.
- **← ИЗ ЧАТА** — retrieve open issues titled `[PATCH P0001]` authored by `sunpole`, parse schema-controlled JSON.
- **ПРИМЕНИТЬ ПАТЧ** — only `report-only` or trusted `sync-main`, verified against an exact SHA and `origin/main`. Posts ANS automatically.

## Requirements

- Windows 10/11 x64
- `.NET 8 SDK` for build
- Git and GitHub CLI (`gh`) installed
- Authenticated GitHub CLI account `sunpole` (`gh auth status`; native/browser auth if needed)
- Public GitHub repository `sunpole/uUnityBridge` created by owner

## Build and launch

In PowerShell, from the extracted folder, run:

```powershell
& '.\Build-And-Start.ps1'
```

This compiles a self-contained Windows EXE inside `dist/win-x64`, creates a desktop shortcut (if not already present), and launches it. No software is installed; no elevation needed. If the SDK cannot restore the runtime packs, report the exact error.

To create the new PUBLIC GitHub repository explicitly, after inspecting files and ensuring no personal data is present:

```powershell
& '.\Build-And-Start.ps1' -PublishPublic
```

Only the allowlisted project files are staged for the first commit. If the repository or local `.git` exists, publishing is aborted rather than overwriting it.

## Security and honest limits

The public repo is not a trustworthy instruction source by itself. Only issues from `sunpole` are considered, and those cannot provide arbitrary shell commands. Git updates refuse dirty worktrees and non-fast-forward operations and preserve a named rollback branch.

**This prototype has not been built/tested on the user's Windows PC.** It cannot install Unity or automatically execute arbitrary changes. For a complete unattended updater and Unity automation, add signed releases, QA, rollback and Unity CLI/Pipeline integration in later iterations.

Official references: https://cli.github.com/manual/gh_issue_list ; https://cli.github.com/manual/gh_issue_comment ; https://docs.unity.com/en-us/unity-cli

## CI

A Windows build workflow runs on push to `main` and attaches a test-only artifact to the GitHub Actions run. A successful CI build **does not prove the app is fully functional**; the UI and GitHub operations need local QA. No automatic release or silent binary update exists yet.
