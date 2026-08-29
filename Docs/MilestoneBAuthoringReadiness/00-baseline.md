# Milestone B authoring-readiness baseline

Captured on 2026-08-28 before the readiness patch.

- Branch: `milestone-b-cards-profile-selection`
- Commit: `ee7377d fix: wire the user profile through every selection surface + consistency audit`
- Working-tree change already present: `Content/GameContent.db` (binary, preserved; not used as a verification fixture).
- Runtime: .NET SDK `10.0.400`.
- `dotnet test Game.Workbench.sln --no-restore --verbosity minimal`: passed — Profile 11, Core 135, WPF 14, Content.Sqlite 122 passed / 1 skipped (the skipped test is the canonical DB playback test because the canonical DB is intentionally dirty).
- WPF host build: passed with 0 warnings and 0 errors.

## Baseline observations

The Card inspector already builds an `ActionSequenceEditorViewModel`, but its `CardActionSequenceHost` only assigned `Content`. The graph path assigns the same object with `ContentTemplate="{StaticResource ActionSequenceTemplate}"`; the Card path therefore renders the view-model object's `ToString()` instead of the real action rows. This is the first user-visible defect fixed by the patch.

The action vocabulary is explicit in `ActionTypeRegistry`; the baseline incorrectly allowed `PhaseGoto` in `CardSequence`. PromptChoice persistence already supports nested option sequences, but the WPF editor did not expose those options. Card and Phase relation fields used title strings/comma-separated text, which was unsafe for duplicate titles and ambiguous for multi-value input. The existing player window was a useful engine host, but did not show full BodyText, candidate diagnostics, semantic action lifecycle logging, or a deterministic two-second cutscene stand-in.

The baseline gate was initially blocked by a stale WPF host process holding output DLLs. Only that identified process was stopped; no repository data was reset or discarded.
