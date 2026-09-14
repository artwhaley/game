# Ticket 01 rig spike — the rig, what it contains, and what drives it

Ticket 01 opened with "no rig assets were found in the inspected `Assets` tree", so
its first deliverable was acquisition and feasibility rather than a mixer
architecture. This records that spike: what the model actually is, what it does and
does not contain, and the constraints that any code built on it has to respect.

**Verdict.** The rig is usable and the driving mechanism is proven end to end — a
pose can be composed, baked, and replayed at runtime with **zero** deviation across
all 96 bones. Two things it needs before ticket 01's visual acceptance can even be
attempted are **assets, not code**: the export contains **no animation clips and no
blend shapes**, and Unity's automatically generated humanoid avatar for it **is
wrong** and has to be fixed or bypassed.

Everything below is a measurement from a run, not an inspection by eye. Every number
came out of `scripts/run-rig-spike.sh` and the two diagnostics, whose logs are named
with each finding.

---

## The rig

`source/luna.fbx`, copied to [`Assets/Characters/Luna/luna.fbx`](../../Assets/Characters/Luna/luna.fbx)
and routed through Git LFS by the existing `.gitattributes` rule for `*.fbx`/`*.png`
(verified with `git check-attr`). Its import settings are pinned by code —
`TruthCardGame → Diagnostics → Rig Spike → Configure Luna Import` — rather than by hand-editing the
importer: humanoid animation type, avatar created from this model, `optimizeGameObjects`
off so bones stay addressable, meshes readable so tests can measure them, materials
kept inside the prefab.

It is a Blender metarig export of a DAZ Genesis figure: bone names like `spine.001`,
`foot.L`, `f_middle.01.L`, and a `night gown` mesh.

| | |
|---|---|
| Sub-assets | 224 — 1 avatar, 10 meshes, **0 clips** |
| Vertices | 41,842 across 10 meshes |
| Morph targets | **0**, on every one of the 10 meshes |
| Skinned renderers | 10 (`luna`, `head`, `hair`, `brow`, `eyelash`, `Gums`, `night gown`, `Sphere_001_Eye_0`, `Sphere_Glass_0`, `choker`), 84 bones each |
| Avatar | `lunaAvatar`, valid, human; 55 mapped human bones of a 96-bone skeleton |
| Mapped anchors | Hips ← `spine`, Head ← `face`, LeftFoot ← `foot.L`, RightFoot ← `foot.R`, hands ← `hand.L`/`hand.R` |
| Size | bounds 1.198 × 1.923 × 1.178; feet→head 1.581; hips 0.931 — human sized |

## What it does not contain — and it is the blocker, not a detail

**No animation.** Zero clips, so there is no stand, no sit, no walk and no gesture to
mix. The fixtures below are therefore *spike inputs* that exist to measure the
mechanism, and they are explicitly marked as such in the source: delete them when
real animation lands.

**No facial presets.** Zero morph targets on all ten meshes, so ticket 01's "simple
facial preset" cannot be driven from this export at all. It needs a re-export with
blend shapes or a different facial mechanism — that is an asset decision, and the
report says so in as many words rather than failing later.

## Four constraints on driving it, each measured

These are the findings worth more than the fixtures, because each one silently
corrupts work built on top of it.

### 1. Headless, an Animator poses nothing unless culling is switched off

The default `AnimatorCullingMode` is `CullUpdateTransforms`, which skips writing the
pose to the transforms whenever the Animator is not visible. Off screen — which is
every headless run, and any unattended capture — nothing is ever visible, so a
`PlayableGraph` evaluate writes **nothing at all**: not for a wrong clip, not for a
generic clip, not for a humanoid clip, and not through Unity's own
`AnimationPlayableUtilities.PlayClip`.

Measured (`Logs/RigDiag2-20260912194840.log`), one clip, one rig, seven routes:

| route | result |
|---|---|
| `SampleAnimation` (editor route) | applies |
| bare `graph.Evaluate` | **writes nothing** |
| `AlwaysAnimate` + `graph.Evaluate` | applies |
| `graph.Evaluate` + `Animator.Update(0)` | **writes nothing** |
| `AlwaysAnimate` + `graph.Evaluate` + `Update(0)` | applies |
| `Animator.Rebind()` + `graph.Evaluate` + `Update(0)` | **writes nothing** |
| `AnimationPlayableUtilities.PlayClip` + `graph.Evaluate` | **writes nothing** |

The Animator state that explains it: `cullingMode=CullUpdateTransforms, renderers=10,
firstVisible=False`. Anything that poses this rig without it being on screen must set
`AlwaysAnimate` first.

### 2. Unity's generated humanoid avatar for this rig is not trustworthy

This one is the reason to be careful with the whole plan. Two independent symptoms,
both measured:

**Its muscle space disagrees with its own geometry.** Reading the pose of the rig as
exported — a plainly standing figure, feet on the ground, head at 1.67 — reports
**49 of 95 muscles displaced beyond ±0.25**: `Left Lower Leg Stretch` at **+0.975**
(knee at its stop on a straight leg), `Left Upper Leg Front-Back` at **+0.563**,
thigh and shin **twist** channels at ±0.8, and `Jaw Close` at **1.029**, past the
range a muscle covers. That is the avatar's assumed rest frame disagreeing with the
bind pose, which is exactly what a Blender metarig exports.

**It retargets playback into a different pose than the clip holds.** Baked clips that
replay **exactly** on this rig with the avatar cleared — worst bone deviation
**0.00000** across 96 bones — are thrown up to a metre away with the avatar in place:
fingertips off by **1.02**, the right shin off by **0.96**, a pinky off by **1.50**
(`Logs/RigSpikeRun-fixtures-*.log`).

And it is not only baked transform clips. A hand-authored **humanoid** clip is
faithful in storage — every one of its 95 muscle curves was verified to equal the
value it was authored from — and `AnimationClip.SampleAnimation` replays it to the
authored pose (feet 0.033 against an authored 0.032), yet the Animator replays the
*same clip* with the feet lifted onto the hips (feet 0.9265). Authoring order and
asset reimport make no difference (all three variants identical,
`Logs/RigBake8-20260912195250.log`).

**What this means for the build.** Because the clips belong to this rig and there is
nothing to retarget *from*, the avatar buys nothing here and costs correctness.
Two ways forward, neither of them code-heavy:

- **Bypass it** — clear the `Animator`'s avatar on the runtime rig (`animator.avatar = null`).
  This is what the fixtures are verified against, and it is how playback is exact today.
  The model still imports as Humanoid, so the muscle vocabulary is still available for
  *authoring* and measuring poses.
- **Fix it** — correct the avatar's bone mapping and rest pose in Unity's Avatar
  configuration, and re-measure. Required if clips from another character
  (Mixamo, Unity-chan) are ever to be retargeted onto this rig.

This should be decided before ticket 02 builds animation on top of it.

### 3. The lowest point of the skinned mesh is not a sole

Ground contact was going to be measured from the lowest vertex of the skinned body
mesh — the real sole rather than the foot bone origin. That measurement is unstable as
soon as the legs fold: it sits **0.0237 below the toe bone at rest**, which is the
shoe, but **0.44 below** it in a flexed pose, because it is tracking hanging geometry
rather than a foot. It is still reported, and contact is now solved against the foot
and toe **bones** instead: the sit foundation keeps the feet within **9.4mm** of the
rest contact height while dropping the hips **0.39**
(`Logs/RigSpikeRun-fixtures-*.log`).

### 4. Every sign is measured, never assumed

Knee flexion, hip flexion and arm raise each pick their direction by measurement, and
the assumption was wrong every time it was tried:

- knee `Left Lower Leg Stretch` keeps **−0.9**: hip→foot reach 0.900 at +0.9 versus **0.235** at −0.9
- thigh `Left Upper Leg Front-Back` keeps **−0.9**: forward reach −0.104 at +0.9 versus **0.487** at −0.9
- arm `Right Arm Down-Up` at +0.9 raised the hand, so +0.9 stands (hand lift 0.89)

The rig even answers which way it faces from its own toe bone, rather than being told.

## The fixtures that now exist

All under `Assets/Characters/Luna/Spike/`, all generated — no hand-written assets and
no hand-written `.meta` files; Unity authored every one.

| asset | what it holds |
|---|---|
| `Rig_Stand.anim` | the rest foundation: every muscle at zero, hips solved for ground contact |
| `Rig_Sit.anim` | hips flexed −0.9, knees solved −0.9, hips solved for the same contact |
| `Rig_ArmRaise.anim` | right arm raised, head turned |
| `UpperBody.mask` | humanoid mask: legs and feet excluded, body/head/arms/fingers included |

Poses are composed in **humanoid muscle space** — the only rig-agnostic way to say
"raise the right arm" — from a **rest baseline of zero muscles** with the export's
root placement, so a clip carries only the joints it means to move. Clips are baked
as **transform curves** and verified by replaying them on a fresh instance through a
real `PlayableGraph`, comparing **all 96 bones** bone for bone: `3/3` fixtures verify
at deviation `0.00000`.

`UpperBody.mask` is written but **not exercised**: the fixtures are transform clips,
so humanoid body-part masking only becomes testable once an `AnimatorController` with
layers exists. Recorded here rather than claimed.

## Running it

```
scripts/run-rig-spike.sh [--diagnose] [--label LABEL]
```

Pins the import settings, reports what imported, builds and verifies the fixtures, and
(`--diagnose`) re-runs the two experiments behind findings 1–3. Logs land in
`Logs/<label>-<step>-<timestamp>.log`; the log is the artefact, which is the point of
running it headlessly. It refuses to start if an editor has the project open.

## What this establishes, and what it does not

**Established.** A rig exists and imports cleanly as humanoid; its contents are known
by measurement; poses can be composed in muscle space by measurement rather than
assumption; ground contact is solved and holds to under a centimetre; and a composed
pose survives the round trip to a runtime-verified clip exactly. The constraints that
would have silently corrupted downstream work — culling, the avatar, the false sole —
are each pinned with a number.

**Not established, and not claimed.** Ticket 01's remaining work is untouched:
travel/pose change, a facial preset (impossible from this export), player gaze, masked
layers driving a real controller, transition bindings, the ingredient registry and the
`PresentationCatalog`. Nothing here exercises an `AnimatorController`, a
`PlayableDirector`, or a player build.

**Open decisions for whoever picks this up.** Fix the avatar or bypass it (finding 2);
re-export with blend shapes if the facial preset stays in scope; and bring actual
animation, because until then every pose in this project is a fixture.
