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


## v0.2.0 acceptance (not yet verified on user Windows PC)

- Exactly three buttons: ИЗ ЧАТА, ПЕРЕДАТЬ ЧАТУ, СКОПИРОВАТЬ.
- Receiving processes all new approved Issues in order, without an apply button.
- No duplicate application after repeated click or restart (local history).
- FAIL halts processing of subsequent patches.
- Sending posts minimal ANS comments and records each confirmed send.
- Copy produces one short notification for ChatGPT.
- Public reports contain no OS paths, environment variables, tokens or raw errors.
- Build and actual GUI still require local QA; no claim of successful Windows test.
