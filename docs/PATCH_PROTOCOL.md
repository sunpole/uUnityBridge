# uUnityBridge PATCH / ANS v1

## GitHub transport

Public repo: `sunpole/uUnityBridge`.

Commands: GitHub Issues that have *exact* title `[PATCH P0001]` and were authored by `sunpole`. The issue body must be valid JSON (or a single fenced JSON block).

```json
{
  "schema": "uub.patch.v1",
  "id": "P0001",
  "action": "report-only",
  "description": "Проверка связи ChatGPT — GitHub — локальная программа"
}
```

For a source update, first merge trusted changes into `main`, then determine the exact 40-character SHA of `main` and publish:

```json
{
  "schema": "uub.patch.v1",
  "id": "P0002",
  "action": "sync-main",
  "commit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "description": "Обновить локальные исходники до указанного коммита main"
}
```

Replace illustrative SHA with the actual Git commit SHA. The client refuses mismatches.

## Local execution policy

- GitHub issue author must be exactly `sunpole`.
- Local gh login must be `sunpole`.
- `sync-main` only works on clean local `main`, in the `sunpole/uUnityBridge` repository.
- Exact remote `origin/main` SHA must equal the patch SHA.
- A `checkpoint/P0002-<timestamp>` branch is created before a fast-forward merge.
- No arbitrary commands are executed from issue text. No PowerShell evaluation. No install, system security changes or auto-merge.
- Upon clicking Apply, result is posted as a public GitHub Issue comment (`ANS`). Reports intentionally include only minimal safe diagnostics.
- If local files are modified, the operation stops. Untracked files also block it.

## Limitations

- This is a source-code synchronizer and public status messenger, not yet a fully verified binary self-updater or unattended code executor.
- GitHub connectivity is user-initiated; no background polling.
- Running the result of remote source updates still requires a separate build/QA step.
- Public issues and comments are visible to anyone. Never attach full logs, personal paths, secrets or access tokens.
- The official Unity CLI/Pipeline/MCP connection is a separate, later integration, after installed Unity 6.x and actual tests.
