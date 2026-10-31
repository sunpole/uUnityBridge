# DeepSeek: one-time $5 trial (optional)

## Principle

The owner has approximately **$5 USD of pre-purchased DeepSeek API credits**.
This is a one-time experiment. uUnityBridge MUST work without DeepSeek before,
during, and after the trial. Do not assume new credits will ever be purchased.

Current state: **NOT CONNECTED, NO API KEY STORED, NO CALLS MADE BY THIS PROJECT.**

## Controls required before any paid request

1. Explicit local opt-in in settings. Default OFF.
2. Key entry only through native desktop UI, never a chat, GitHub Issue, command-line
   argument, process output, crash report or repository file.
3. Store the API key locally using Windows DPAPI / Credential Manager. Never log it.
4. Explicit per-project opt-in before sending any source files to DeepSeek.
5. Trial cap: **$4.50 USD total maximum measured spend**, with a $0.50 buffer against
   unfinalized usage. Default cap can only be changed by the user in settings.
6. Before requests: check the provider's balance endpoint and local spend ledger.
   On HTTP 402, insufficient balance, missing key, unreliable pricing, or budget
   exhausted: STOP paid calls and show a clear status. Never automatically top up.
7. After every request: record provider-reported input/output/cache token usage,
   model, request ID, duration, and computed estimated cost. Check balance again
   when possible. Provider bill remains the source of truth for actual charges.
8. Rate-limit retries; no endless automatic retries or unbounded context loops.
9. Limit first test to a benign, small, read-only prompt with a tiny output budget.
10. No OpenAI API charges: the ChatGPT subscription does not fund OpenAI API usage.

## Suggested experiment

- Test 1: balance/authentication read-only, spending $0 if provider supports it.
- Test 2: short safe task with deepseek-flash, capped per-request.
- Test 3: read-only review of an allowlisted public repository file.
- Test 4: optional draft patch to a disposable branch; tests and Git review must pass.
- Compare code quality, cost, duration, errors and manual interventions.
- Stop when trial budget is used. Keep the same patch queue and GitHub workflow
  operating normally with no DeepSeek.

Current model identifiers, capabilities and pricing must be read from the official
DeepSeek docs at implementation time (they can change).

Official docs:
- https://api-docs.deepseek.com/
- https://api-docs.deepseek.com/api/get-user-balance/
- https://api-docs.deepseek.com/quick_start/error_codes/
