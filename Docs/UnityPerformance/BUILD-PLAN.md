# Conversation Performance V1 — build plan

September 11, 2026 source-audit revision. This replaces the previous packet. This is documentation for review; no implementation is claimed or authorized by this revision alone.

## Outcome

Play an ordinary Card authored in WPF and stored in SQLite, using real Core and a real or representative humanoid rig in Unity:

    Perform: Playful Tease
    Dialogue From Tags: tease
    Dialogue From Tags: instruction
    Wait For Continue

Core plans a legal destination/posture and selects compatible acting ingredients. Unity executes movement, pose, expression and gaze. The active event automatically refreshes acting at dialogue boundaries. Repeating the Card can produce other legal choices. A newly added compatible gesture becomes available without changing the event or Card.

This is the first architectural milestone. Voice, driven motion, player posing, extensive dashboards and standalone packaging wait until it is accepted.

## Source audit: what the previous packet got wrong

The audit inspected current source and documentation, not the source archive. Findings below are source evidence; no new test run or runtime feasibility result is claimed.

| Evidence | Consequence |
|---|---|
| Root README, SQLite content pipeline section; GameContentDefinition.cs; UnityContentGraphBuilder.cs summary | SQLite is canonical, GameContentDefinition is an in-memory snapshot, and Unity's ScriptableObject bridge is temporary. Remove the invented game-content JSON architecture. |
| DotNet/Game.Content.Sqlite/Game.Content.Sqlite.csproj; GameContentSnapshotLoader.Load(DbConnection, bool) | Provider-neutral netstandard2.1 mapping and a no-migration loading path already exist. Reuse them in Unity. |
| Packages/manifest.json; Assets/Scripts/Game/GameManager.cs | No Unity SQLite provider/bootstrap is configured. Prove provider integration early; its compatibility is unverified, not established impossible. |
| CoreServices.cs; IDialogService.ShowAsync; ActionExecutor.cs | Async host services already define the boundary. Core needs semantic requests and decisions, not a delta-time loop. |
| ActionExecutor.ExecuteInstanceAsync and ActionTypeInfo | IsAlwaysBlocking routes to ReduceFlow except for two special cases. Add explicit control/yield classification before adding Perform. |
| ActionExecutor.ExecuteDialogFromTagsAsync | Dialogue selection occurs before host awaits and uses its own RNG. Preserve that behavior when adding automatic acting refresh. |
| GameSessionEngine.ShutdownCoreAsync | Current shutdown handles toy output. Integrate performance cleanup without allowing one service's early return/failure to skip another. |
| DialogCatalogRepository; RelationPickerControl; CardEditBuffer; ActionInstanceCloneUtility | Reuse catalog identities, searchable chips, transactions, undo and buffered Card editing. Do not invent new authoring machinery. |
| GameContentSnapshotLoader and ContentReferenceValidator | Current loading validates the full content snapshot. Keep that baseline; do not hide failures behind a dependency exporter or promise isolated-draft playback. |
| DotNet/Game.Core/Game.Core.csproj | Portable source is physically under Assets/Scripts/Portable and linked into DotNet. Do not duplicate it. |
| Inspected Assets paths and Packages manifest | No .fbx/.anim/.prefab/.wav assets or SQLite provider DLLs were found. Animation Rigging is not declared. Rig acquisition and any new dependency are explicit prerequisites. |

Some documentation contains historical versions and superseded dialogue behavior. The current README itself has older milestone counts alongside newer status. Execution ticket 00 must verify actual schema/tests; do not treat historical passing counts as current evidence.

## Ownership

- WPF/SQLite owns Cards, graphs, dialogue, Performance Tags and reusable Conversation Performance Events.
- Unity owns animation ingredients, tag membership, factual compatibility, anchors and animation bindings. It generates a read-only PresentationCatalog.
- Core owns semantic state, event resolution, compatibility, factored planning, ingredient selection and request/result correlation.
- Unity owns continuous animation evaluation, blends, IK, gaze execution and any purely visual ambience.
- The WPF Reference Player explains the same Core decisions using a simulated performance host.

Performance Tags are a separate namespace from Card Tags and Dialog Tags. Unity reads them automatically from the same content database. There is no manual ID copying or parallel editable tag vocabulary.

## Small authoring surface

V1 adds one action: Perform(eventId), always awaited until the requested staging and acting are ready. Its event has a name, semantic ALL/ANY tag query, simple staging policy, optional posture/location constraints and refresh-at-dialogue-start enabled by default. No action-local overrides or additional mood state.

An event expresses intent such as playful or stern. If intent changes between lines, another Perform can select another reusable event. Do not assign clips to lines, store sampled combinations or add public reroll controls.

Unity gesture intake should normally require only clip, semantic tags and verified compatible posture. Name/ID, enabled state, standard body mask and technical timings use defaults or inference. Exceptional location/prop/head constraints appear only when needed. Measure the actual number of fields touched.

## Factored planning

Represent state as anchor plus posture. Anchors declare capabilities such as supporting sitting. Transition capabilities describe reusable operations such as Stand, Move while standing and Sit at a sit-capable anchor. Core composes operations; Unity executes bindings and coordinates.

Do not require an authored node for every anchor/posture pair or a unique edge for every conversion. A small anchor adjacency graph and explicit exceptional transitions are acceptable. Reverse pose operations must be supported explicitly.

Start with two reachable anchors and standing/sitting, proving a composed stand–move–sit plan. Room center, Door, Window, Chair, Side of bed and Foot of bed are later bedroom fixtures, not engine enums or a mandatory nine-state schema.

## Content and iteration

Unity reads Content/GameContent.db through the existing provider-neutral loader, without migrations on runtime connections. WPF remains the writer. Play/Repeat starts a fresh read transaction, loads committed content and closes the connection; the active run retains its in-memory snapshot. WPF can stay open.

Unity-generated presentation descriptors are the only new exchange artifact. Generate them atomically after valid ingredient changes; WPF refreshes read-only diagnostics without losing dirty Card edits. Unity Play/Repeat uses current validated descriptors directly.

No game-content JSON publication, dependency-closure export or stale development snapshot protocol. If the provider cannot load canonical content under the pinned Unity editor, record the actual failure and stop that integration for review. Do not change the content architecture to bypass it.

The simplest useful tester runs a small ordinary Session that draws the representative Card through the production engine. Repeat restarts that Session with fresh performance randomness in the loaded scene. This avoids a new standalone Card interpreter. Use existing profile and eligibility rules.

## Incremental delivery

[The six-ticket stack](../../Tickets/UnityPerformanceExecutionStack/TICKET-STACK.md) covers:

1. Source baseline, SQLite integration prerequisite and explicit action classification.
2. Genuine rig spike, minimal ingredient registry and portable catalog.
3. Minimal Core event planner and async host contract.
4. Typed persistence, Performance Tags and a simple WPF event editor.
5. Unity host and real SQLite-authored Card playback.
6. Visual/workflow correction and V1 acceptance.

The [architecture contract](../../Tickets/UnityPerformanceExecutionStack/ARCHITECTURE-CONTRACT.md) defines V1 decisions. Ticket 04 ends the first integrated visual proof; ticket 05 corrects it and demonstrates cheap content expansion. Do not add editor polish ahead of the proof.

## Performance Phase 2 / Driven Motion — deferred

After V1 acceptance, write a separate packet for oscillator/scalar-driven paths and funscript if still desired. Discover API, persistence and layer requirements then. V1 contains no driver fields, curve tables, sampled-window API or reserved pelvis driver channel.

Other later candidates: broader postures/locations, player posing/visibility, voice/lipsync, richer arbitration/idle behavior, comprehensive coverage views and standalone packaging. Reusable semantic behavior graphs may be considered if real content demonstrates a need. A generated read-only Nodify plan view is useful now when inexpensive; it is not an authored choreography graph.
