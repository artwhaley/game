# Pre-Milestone C Reliability — Ticket 04 Runner Log Retention and Export

## Implemented contract

The Reference Player and live Preview runner surfaces now retain the last
10,000 physical log lines. They expose `Copy Log`, `Save Log…`, and `Clear Log`.
Copy uses the current log only; Save writes UTF-8 without a BOM and defaults to
`Session-YYYYMMDD-HHMMSS.log`; Clear only empties the visible log and leaves
the active Session/engine state untouched.

Card starts use the structured multiline form:

```text
CARD START
Title: ...
Body:
...
```

## Verification

- `RunnerLogBufferTests`: 10,000-line retention, multiline export, and clear
  behavior.
- WPF XAML exposes the three controls on both runner surfaces.
- Build/test gates are run after the XAML and runner code changes.
- Human gate remains pending: start a runner, generate more than 400 lines,
  verify old lines are retained through the 10,000-line bound, copy/save the
  log, and clear it while confirming the running Session is not restarted or
  reset.
