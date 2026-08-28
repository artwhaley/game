# User Profile — Deferred Design (Ticket 20)

**Status: deferred by design.** No profile features are built yet. This document
captures the boundary, the future shape, and the exact prohibition that keeps
player data out of the content database.

## Boundary that IS implemented

- **`UserProfilePaths`** (`DotNet/Game.ReferenceHost.Wpf/UserProfilePaths.cs`)
  resolves the FUTURE profile DB path under OS user application data
  (`%LocalApplicationData%/TruthCardGame/UserProfile.db`), never inside the
  install directory. Replacing or unzipping a new game version therefore can
  never destroy player data.
- **Spawn seam**: every session start path (Workbench Preview, Reference
  Player, Core `GameSessionEngine`) accepts `SessionSpawnOptions`. A future
  profile loader supplies `TemperatureOverrides` there; with no override the
  content definition's default applies (Happiness 50).
- **Boundary tests** (`UserProfileBoundaryTests`) prove `GameContent.db`
  (fresh schema AND the committed canonical file) contains no per-user tables:
  no profile / toy / kink / equipment / preference / device / player-state /
  temperature-state / user / save tables.

## Explicit prohibition

`GameContent.db` is **content only** and must never contain per-user data:

- Kink preferences;
- Equipment ownership;
- Smart Toy configuration;
- Persistent Happiness (or any persistent temperature/stat value).

Runtime state (`TemperatureState`, `PlayerStats`) lives in memory for the
duration of a session only. If persistent state is ever needed, it belongs in
the profile DB at `UserProfilePaths.ProfileDatabasePath()`.

## Future data (schema deferred until feature implementation)

These are the known future profile domains. **No schema is defined yet** — the
exact tables, columns, and migrations will be designed and built together with
each feature, in the profile DB only.

- **Equipment inventory** — owned items, quantities, unlock state.
- **Smart Toy actual devices / capabilities** — not configuration of a single
  device; a capability-description record the game queries at runtime.
- **Kink preference enum** — a curated, versioned enum (additive only).
- **Hard "Don't Consent" semantics** — consent flags must be stored explicitly
  and consulted BEFORE any session/content selection; absence of a flag is not
  consent. This is a safety boundary, not a content concern.
- **Persistent temperature possibility** — if Happiness (or other
  temperatures) ever persists across sessions, they live here as per-user
  spawn overrides fed through the existing `SessionSpawnOptions` seam, and the
  content DB stays untouched.

## Migration plan

Profile schema lands as its own migration chain in `UserProfile.db` (a
dedicated `CoreMigrator`-style ledger), versioned independently from the
content DB so content updates never block or conflict with profile upgrades.
