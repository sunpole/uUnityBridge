# API / MCP roadmap — strong capability, bounded authority

Status: DESIGN ONLY. The current uUnityBridge v0.2.x has GitHub API **client**
calls but no inbound REST API or MCP server. ChatGPT cannot directly reach a
local Windows service merely because it listens on localhost.

## Proposed layered architecture

1. **uUnityBridge Core**: typed tasks, project registry, idempotent command ledger,
   validation, Git checkpoints, tests, per-project permissions and audit trail.
2. **Local IPC**: Windows Named Pipes restricted to the logged-in user. External
   programs are not granted a generic shell or unrestricted file-system access.
3. **MCP adapter**: a versioned tool API exposing bounded actions to approved AI
   clients. Start read-only; add write tools only after offline regression tests.
4. **ChatGPT connection**: if supported for the account, use a custom MCP plugin
   with **Secure MCP Tunnel** (preferred for the private local endpoint). Do NOT
   expose an unauthenticated localhost API through public port forwarding.
   Plugin installation/tunnel creation requires user action and real testing.
5. **GitHub**: durable exchange, Issues / PRs / CI / releases; no arbitrary script
   execution from Issue body and no constant issue-spam.
6. **Optional DeepSeek**: local agent uses the same allowlisted tools and finite
   budget; failure/exhaustion cannot block the baseline GitHub workflow.

## Candidate tools (API names are proposed, NOT implemented)

Read tools: `system_health`, `projects_list`, `project_status`, `git_diff`,
`issues_pending`, `patch_receipts`, `build_logs_summary`, `unity_status`.

Bounded write tools: `task_create`, `task_cancel`, `patch_apply_verified`,
`tests_run_allowlisted`, `build_run_allowlisted`, `report_publish`,
`checkpoint_create`, `checkpoint_restore`, `unity_scene_update_safe`.

## Non-negotiable constraints

- Explicit allowlisted project directory and operations; deny writes outside it.
- No arbitrary shell, PowerShell, remote commands or secret read/publish tools.
- Per-invocation checks: authenticated caller, project scope, exact task revision,
  expected Git SHA, idempotency key, allowed change class, audit entry.
- Never execute untrusted tool-instruction text from files or Issues.
- Separate identity, permissions, authentication and source provenance checks.
- All nontrivial changes in feature branches, required CI before merge.
- A job state machine with immutable IDs: NEW, RUNNING, SOURCE_UPDATED,
  QA_PASSED, RELEASED, BLOCKED, NEEDS_REVIEW, CLOSED.
- Auto-accept can cover read-only/status/approved project operations, not OS
  policies, installers, admin access, credentials or destructive operations.
- Pause/cancel, concurrent-operation lock, bounded retries and recovery.
- Outputs: structured JSON + public-safe ANS summary; full logs local only.

## Rollout order

1. P0002: reliable queue and ANS receipts.
2. P0003: automated unit and integration tests, fault injection.
3. P0004: versioned signed/verified release updater.
4. P0005: local read-only MCP tools and client capability negotiation.
5. P0006: Secure MCP Tunnel + custom ChatGPT plugin, tested end to end.
6. P0007: bounded write tools and multi-project configuration.
7. P0008: optional DeepSeek trial without permanent dependency.
8. P0009: Unity CLI/Pipeline adapter and game-build tests.

This is a sequence of capabilities, not a claim of functionality.

Official references:
- https://developers.openai.com/api/docs/guides/custom-mcp-server
- https://developers.openai.com/api/docs/guides/tools-connectors-mcp
- https://developers.openai.com/plugins/build/mcp-server
