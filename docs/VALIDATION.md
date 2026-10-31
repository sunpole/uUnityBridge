# QA acceptance checklist (not yet executed)

- [ ] `dotnet publish` passes on Windows 10 Pro with .NET 8 SDK.
- [ ] WinForms EXE launches without console and without admin.
- [ ] `gh auth status` and owner account check pass.
- [ ] No authenticating token ever appears in the UI or published issue.
- [ ] Wrong GitHub account is refused.
- [ ] Public issue from another author is ignored even when JSON imitates a patch.
- [ ] Malformed patch and JSON code injection are ignored.
- [ ] report-only patch P0001 gets `ANS P0001` reply in GitHub.
- [ ] Public status report includes no hostname/usernames/user paths/full logs.
- [ ] sync-main rejects incorrect SHA and wrong origin/branch.
- [ ] sync-main rejects dirty worktree, never discards local edits.
- [ ] sync-main creates checkpoint branch and applies only fast-forward.
- [ ] Network error is shown but not misreported as successful.
- [ ] Application can be rebuilt after source update.
- [ ] Future: source release / binary update verification, automatic rollback, Unity CLI/Pipeline integration.
