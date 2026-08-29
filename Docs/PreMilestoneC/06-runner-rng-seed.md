# Pre-Milestone C Reliability — Ticket 06 RNG Seed

## Implemented contract

- The Workbench toolbar, embedded Preview, and Reference Player expose a
  signed 32-bit integer Seed field and a Randomize button.
- Start and Restart construct `SessionSpawnOptions` with the displayed seed;
  the runner logs an exact `Seed: <integer>` line.
- `SeededRandomDomains` derives separate stable streams for Session selection
  and PhaseRun/Card selection. Session selection never consumes a PhaseRun
  stream, and every PhaseReference entrance still receives its own independent
  card RNG.
- Fixed-seed wording was not added to the UI; the seed is explicit and visible.

## Verification

- Focused Core tests: 24 passed (`SpawnAndSelectionTests`,
  `RandomSourceTests`, and `SpawnOptionsTests`).
- WPF build: passed with 0 warnings and 0 errors.
- The rebuilt WPF app is running for the human trace gate.

## Human gate

Run the same Session twice with the same displayed seed and compare the
Reference Player/Preview trace, then change the seed and confirm the legal
choices may change. Confirm the exact `Seed: <integer>` line is present in the
log.
