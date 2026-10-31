# P0002 — reliable queue acceptance

Work is developed in a separate branch, never directly in main.

- [x] Code: fingerprint binds receipt to exact Issue content, commit and action.
- [x] Code: receipt is saved as APPLYING before executing source sync.
- [x] Code: interrupted APPLYING stops instead of silently repeating.
- [x] Code: failed operations are retryable up to 3 attempts.
- [x] Code: exhausted retries become BLOCKED, halting dependent work.
- [x] Code: publish only terminal ANS; correlate with idempotency marker.
- [x] Code: local posted state recorded before closing Issue.
- [x] Code: close successful Issue separately; retry closure without re-posting.
- [x] Code: cap outstanding queue to 5 Issues; reject repeated PATCH IDs.
- [x] Code: keep backup copy of previous local receipt ledger.
- [x] Added dependency-free state/ledger regression test source.
- [ ] CI Windows build succeeds on new branch.
- [ ] CI test run passes on new branch.
- [ ] Real GUI test on user's PC confirms no duplicate handling.
- [ ] Network-interruption tests and account mismatch tests pass.
- [ ] Self-updating Windows EXE works (separate milestone, not P0002).
- [ ] Connected ChatGPT MCP read/write tools work (separate milestone).
- [ ] DeepSeek API connected (separate, opt-in experiment).

Limitations: successful source update is SOURCE_UPDATED, not QA_PASSED or
EXE_UPDATED; this is intentional. No arbitrary shell commands run from GitHub.
