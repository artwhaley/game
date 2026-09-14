# Conversation Performance V1 integration results

Updated September 14, 2026.

This records the implementation handoff for the six-ticket Conversation Performance V1 stack. The existing Lara foundation, attachments and character-import choices are preserved; this pass corrected only incomplete or drifting performance integration.

## Delivered

- `Content/GameContent.db` remains the canonical writer-owned source at schema v12. The V1 fixture is idempotently authored through `Game.Content.Sqlite.Tool` and now contains, in order, `Perform`, two blocking `DialogFromTags` instances, `WaitForContinue`, and progress.
- Unity's performance panel now reads one consistent read-only SQLite snapshot through `GameContentSnapshotLoader`, closes the connection before playback, chooses the Session through `SessionSelector`, and runs the ordinary `GameSessionEngine`.
- Unity dialogue is visibly acknowledged one line at a time. The graph's `Continue` remains a separate control boundary. Repeat stops the old engine, reloads SQLite and the generated presentation catalog, resets the known start, and starts a fresh run.
- Core rejects null, rejected, stale, or mismatched performance acknowledgements without committing a false arrival. Stop state is retained when teardown is rejected so a later cleanup can still be attempted.
- Unity Stop works even when the generated catalog is unavailable, and face/gaze/animation dependencies are preflighted before acting mutates the rig.
- The registry setup's `List<string>`/`string[]` compiler drift is corrected.

## Automated evidence

- `dotnet test Game.Workbench.sln --no-restore --verbosity minimal`: **470 passed, 0 failed** (215 Core, 182 SQLite, 11 profile, 62 WPF).
- `dotnet run --project DotNet/Game.Content.Sqlite.Tool --no-restore -- --author-v1-performance`: completed the canonical authoring round trip and reported schema v12, two tagged dialogue actions, five card actions, and the tool's integrity/foreign-key checks.
- `dotnet build .tmp/PerformanceHostCompile/PerformanceHostCompile.csproj --no-restore --verbosity minimal`: **0 errors, 0 warnings**, covering the changed Unity performance host, panel, registry resolver and character foundation against the pinned Unity facades.
- Existing SQLite integration tests cover the caller-owned consistent read transaction and connection release. The Unity editor was left open on the project, so batch Unity tests were not started concurrently with it.

## Hands-on visual gate

The remaining evidence is visual rather than a code blocker. In the open Unity editor:

1. Open `Assets/Scenes/Phase00RigShowcase.unity` and run `TruthCardGame > Performance > Wire Current Scene for Performance` if the disposable stage object is not already present; save that disposable scene.
2. Run `TruthCardGame > Performance > Generate Presentation Catalog` after any registry change, then press Play.
3. Confirm the panel loads `Conversation Performance Setup`, the actor stages and gazes at `PlayerGazeTarget`, each of the two tagged lines appears after a dialogue-start refresh, `Continue` completes the card, and `Repeat` starts from the room-center anchor with a fresh snapshot.
4. Confirm Stop during staging or dialogue leaves no active overlay/gaze. Record any concrete rig/clip/contact defect before changing the intentional attachment/import foundation.

No claim of that visual pass is made here until it is observed in the editor; all source, content and compile/test gates above are complete.
