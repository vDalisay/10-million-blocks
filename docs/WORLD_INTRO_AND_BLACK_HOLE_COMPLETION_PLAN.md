# World Intro Wave + Black-Hole Completion Bonus — Reviewed Implementation Plan

Status: **planning / implementation handoff only**

Branch: `codex/future-world-progression`

This document is the reviewed version of the world-start-wave / timed black-hole reward plan. It deliberately does **not** implement the feature. The plan has been checked against the current gameplay, save, loading, replay, resource-collection, pause, renderer and completion architecture before being committed.

---

## 1. Locked product requirements

The following behavior is considered decided for implementation:

1. Every **playable world start** begins with the cube fully framed and non-interactive.
2. The intro lasts **3 seconds**.
3. The top-facing row/surface of blocks performs a wave from **screen-left to screen-right using the initial camera view**.
4. During the intro, mining, Hover Mining, automation, camera manipulation, skill interaction, world events and other gameplay are locked.
5. **Esc remains available.** Opening the pause menu pauses the intro and all simulation; resuming continues from the same point.
6. The clear-time score begins only when the intro finishes and gameplay unlocks.
7. When the final authoritative block is removed, the clear-time score freezes immediately.
8. Outstanding ordinary pickups are resolved before the completion spectacle.
9. Completion presentation order is:
   - freeze gameplay / score;
   - resolve remaining ordinary pickups;
   - recenter the completion presentation on the old cube center;
   - implode at the center;
   - spawn exactly one miniature visual bonus block for every bonus resource;
   - have those blocks hop radially outward into a circular field;
   - spawn a visual black hole after they settle;
   - spiral/suck every bonus block into the black hole;
   - credit the exact bonus;
   - only then show the completion/results menu.
10. The bonus starts at **100% of the world's initial physical block count** and loses 10 percentage points every five minutes, down to an absolute minimum of **20%**.
11. The results screen shows at least the clear time, score percentage and total bonus resources gained.
12. The visual particle count is **not capped**. If the bonus is 73,448 resources, the completion field contains 73,448 visual bonus blocks.

---

# 2. Review findings and revisions to the first draft

The first draft had the right product sequence but several implementation assumptions should be changed before coding.

## 2.1 Do not build a duplicate pristine world for the intro

The earlier draft proposed rendering a second pristine copy of the world during the intro. That is unnecessary, expensive on large worlds and misleading when revisiting a partially mined save.

Revised rule:

- a **fresh** world naturally shows its complete pristine cube;
- an **unfinished revisit/continue** shows the player's real saved cube, fully framed, then waves the currently visible upward-facing surface;
- do not temporarily resurrect already-mined blocks just for presentation;
- a previously completed/empty world is not treated as a new playable run and does not re-award or replay the start ceremony by default.

This preserves persistence truth while still giving every actual playable world load the requested ceremonial start.

## 2.2 The intro should animate the existing `WorldView`

`WorldView` already batches actual visible blocks into `MultiMesh` instances. The wave should reuse those instances rather than create another renderer.

During batch construction, keep lightweight presentation references only for upward-facing surface instances:

- `MultiMesh` reference;
- instance index;
- immutable base transform;
- world/support position;
- optional linked tree/decorative instance.

When the 3-second intro begins, project the base positions through the locked initial camera and normalize their screen-space X coordinate. That normalized value drives the wave delay, so the wave is guaranteed to travel **left -> right as the player sees it**, independent of world axes.

Only the relatively small top-surface population receives per-frame transform updates for three seconds. The full cube remains in its normal batched renderer.

Trees or other authored features attached to a waving top tile should move with their support tile instead of visibly detaching.

## 2.3 Use an explicit run lifecycle instead of scattered booleans

The current branch assumes that a constructed world is playable immediately, and final-block handling can jump directly into `ShowCompletion()`.

Introduce one authoritative runtime phase, for example:

```text
PreparingWorld
IntroLocked
Playing
CompletionLocked
Implosion
BonusScatter
BlackHoleSuction
Results
```

The exact enum names may change, but there must be one source of truth.

All gameplay input/process gating is derived from this phase rather than separate ad-hoc flags. This is necessary to prevent mining, automation, camera actions, menus or events from leaking into intro/completion cinematics.

## 2.4 The score clock needs real persistence

`WorldSaveData` already has `ActivePlaySeconds`, but current session capture does not persist the active runtime value back into rebuilt world-save data. The feature should fix that instead of creating a second unrelated timer.

The run clock:

- restores from `ActivePlaySeconds`;
- advances only while the run phase is `Playing`;
- does not advance during the 3-second intro;
- does not advance while `SceneTree.Paused` through Esc;
- freezes permanently for that run on the authoritative final-block removal;
- is persisted on autosave/world leave.

### Offline progression rule

Offline behavior needs to stay compatible with the existing idler simulation without making the speed score trivially exploitable.

Revised policy:

- if no offline mining is applied, time away does not increase `ActivePlaySeconds`;
- if offline automation progression is applied, the simulated elapsed time counts toward the run;
- if offline simulation clears the world, freeze the clear time at the simulated offset where the final block was removed, not blindly at the entire away duration.

If `MinerSimulationService.ApplyOfflineProgress()` cannot currently expose the exact simulated completion offset, revise its return contract from a simple block count to an `OfflineProgressResult` containing at least:

- blocks removed;
- simulated seconds consumed;
- optional seconds-to-world-clear when the world reaches zero.

## 2.5 Freeze and persist the score before starting the cinematic

The earlier draft correctly wanted crash-safe reward behavior but did not make the persistence boundary explicit enough.

As soon as the authoritative remaining block count first reaches zero while `Playing`:

1. set run phase to `CompletionLocked` immediately;
2. freeze `ActivePlaySeconds`;
3. calculate score and exact bonus;
4. write a **pending completion result** into the world save;
5. synchronously save that frozen result before the long cinematic begins.

This means a crash during the implosion/particle/black-hole sequence cannot change the player's earned score on reload.

Suggested additive save fields:

```text
bool ClearReached
 double CompletionClearSeconds
int CompletionScorePercent
long CompletionBonusResources
bool CompletionBonusClaimed
```

`Completed` remains the final committed world-completion state.

Missing fields from older schema-3 saves normalize to defaults, so this can remain an additive development-schema change unless implementation review finds a migration reason to bump the schema.

## 2.6 Completion reward must be one transaction, not one currency event per particle

Visual count and economy event count are separate concerns.

There may be 123,412 visual bonus particles in the current 50³ world, but the economy should **not** process 123,412 `GrantCurrency(1)` calls.

During black-hole suction, the HUD may visually count upward based on cinematic progress. The authoritative reward is committed once at the end:

1. mark the pending bonus claimed in memory;
2. grant the complete bonus through the active ordinary wallet / `MiningService`;
3. mark the world completed/unlock the next world;
4. capture the complete session state;
5. perform one save.

Those state mutations happen synchronously on the main thread before anything can trigger a normal autosave. The close-window/save path must preserve both wallet and `CompletionBonusClaimed` together.

On reload:

- `ClearReached == true`, `CompletionBonusClaimed == false`, `Completed == false` => rerun the completion cinematic using the already-frozen score/bonus;
- `CompletionBonusClaimed == true` / `Completed == true` => never grant it again.

## 2.7 Prefer `GPUParticles3D` for the exact-count bonus field

The first draft preferred `MultiMesh`. After review, a custom GPU particle system is a better primary backend for this specific effect.

Why:

- the effect is literally a short-lived particle field;
- one visual instance per bonus resource is required;
- no physics or collision is needed;
- motion can be completely faked from deterministic shader math;
- a particle shader exposes particle `INDEX`, `RANDOM_SEED`, `CUSTOM`, transform and velocity on the GPU;
- this avoids creating/updating a C# object, node or physics body per bonus unit;
- it avoids a large CPU-side per-instance transform upload for the common path.

Godot documents that higher `GPUParticles3D.amount` values increase GPU requirements, so this still requires profiling. The effect happens after the world is empty, which substantially lowers competing world-render cost.

Keep the presentation controller backend-agnostic. If local profiling shows the target renderer/hardware handles an exact-count `MultiMesh` better, an exact-count MultiMesh shader backend may replace the emitter without changing game rules. A fallback is allowed; **reducing the visual count is not**.

Official Godot references:

- MultiMesh: https://docs.godotengine.org/en/stable/classes/class_multimesh.html
- MultiMesh optimization / shader-side instance logic: https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html
- GPUParticles3D: https://docs.godotengine.org/en/stable/classes/class_gpuparticles3d.html
- Particle shaders: https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/particle_shader.html

## 2.8 Add a completion camera step

The prior draft assumed the effect would be visible at the world center. That is not guaranteed after arbitrary player panning/zooming.

When the last block is removed:

- lock camera input immediately;
- snapshot the current camera only if useful for polish;
- smoothly recenter/focus the camera on the old world's bounds center during the pickup-resolution/implosion lead-in;
- choose a stable distance that contains the planned radial bonus field;
- keep this camera locked until the results screen appears.

The bonus scatter plane should use the completion camera's right/up basis so it reads as a circle on screen instead of becoming an arbitrary ellipse because of world orientation.

---

# 3. Score and bonus contract

## 3.1 Score percentage

Let `clearSeconds` be the frozen run clock when the final authoritative block is removed.

```text
fiveMinuteBuckets = floor(clearSeconds / 300)
scorePercent = max(20, 100 - fiveMinuteBuckets * 10)
```

Boundaries:

| Clear time | Score |
|---|---:|
| 0:00 - 4:59.999 | 100% |
| 5:00 - 9:59.999 | 90% |
| 10:00 - 14:59.999 | 80% |
| 15:00 - 19:59.999 | 70% |
| 20:00 - 24:59.999 | 60% |
| 25:00 - 29:59.999 | 50% |
| 30:00 - 34:59.999 | 40% |
| 35:00 - 39:59.999 | 30% |
| 40:00+ | 20% |

## 3.2 Bonus resource amount

Use the world's persisted **initial physical mineable block count**, never an authored approximate target.

```text
bonusResources = ceil(initialMineableBlocks * scorePercent / 100)
```

Ceiling is deliberate: integer rounding must never produce less than the promised percentage.

Examples:

- 10,000 blocks at 17 minutes -> 70% -> **7,000 bonus resources**;
- 6,824 blocks at 17 minutes -> 70% -> **4,777 bonus resources**;
- 123,412 blocks under 5 minutes -> 100% -> **123,412 bonus resources**.

Each bonus resource corresponds to one visual miniature block in the completion field.

---

# 4. Runtime lifecycle and input gating

Add one central `WorldRunPhase` owned by `GameRoot` (or a small lifecycle service owned by it).

A single method such as `ApplyRunPhaseState()` should configure:

- `ManualMiningController.InputEnabled`;
- Hover Mining processing;
- `MinerPlacementController.InputEnabled`;
- `MinerSimulationService.ProcessMode`;
- `WorldEventController.ProcessMode`;
- skill-tree open/buy input;
- automation drawer/buy actions;
- camera manipulation input;
- tutorial/world-event interaction if applicable.

### Intro lock

Allowed:

- Esc;
- Pause -> Resume;
- presentation/settings options;
- Save & Return to Main Menu.

Disallowed until intro finishes:

- mining;
- collection interaction;
- camera orbit/pan/zoom;
- automation placement/purchases;
- skill-tree opening/purchasing;
- world browser transition from gameplay;
- world-event interaction.

The pause overlay already uses `ProcessMode.Always` and `SceneTree.Paused`, so the intro controller itself should **not** use `Always`. The 3-second wave freezes naturally while paused and resumes at the same animation time.

### Completion cinematic lock

The same gameplay systems remain locked after the final block.

Esc may still pause/resume or Save & Return to Main Menu. World-switch/browser actions should remain disabled until the completion reward is committed, preventing a second world transition from racing the pending transaction.

---

# 5. Three-second world-start wave

## 5.1 Start condition

Do not start the 3-second clock merely because `WorldView` exists.

Wait until:

- `WorldView.InitialPresentationReady == true`;
- the loading overlay is no longer covering gameplay;
- the default initial camera preset has been applied;
- all relevant gameplay systems are already in `IntroLocked`.

Then begin exactly one intro sequence for this session load.

## 5.2 Framing

At intro start:

- world is fully visible at the default presentation distance;
- camera is centered;
- no idle orbit is allowed;
- HUD may be visible but should read as inactive/standby if needed;
- player input cannot change framing until the wave ends.

## 5.3 Wave membership

Track the currently rendered **upward-facing exposed surface** rather than assuming a flat `Y == max` layer. This allows the wave to work on terrain and the reviewed 20/40/50 worlds.

For each wave-eligible instance retain its immutable base transform.

When the intro starts:

1. project its world position with the initial camera;
2. find min/max screen X among eligible instances;
3. calculate `waveX = inverseLerp(minX, maxX, screenX)`;
4. use `waveX` as the per-instance delay.

This guarantees left-to-right travel from the initial player view.

## 5.4 Timing

Initial tuning target:

```text
0.00 - 0.25s   hold complete/current cube
0.25 - 2.70s   wavefront travels left -> right
2.70 - 3.00s   final blocks settle
3.00s          restore exact base transforms, unlock Playing, start score clock
```

Each block performs one smooth vertical rise/fall with no permanent transform drift.

Suggested amplitude: approximately `0.30-0.45 * BlockSpacing`, tuned visually.

Use a smooth sine/Bezier pulse rather than physics.

Reduced Motion keeps the same three-second lock/timer contract but lowers wave amplitude substantially and removes secondary overshoot.

## 5.5 Resume/revisit behavior

- fresh unfinished world: waves the pristine top surface;
- partially mined Continue/Revisit: waves the real current exposed top surface;
- pausing/resuming the same session does not restart the intro;
- completed empty world revisit does not reconstruct mined blocks or re-award the ceremony.

---

# 6. Final-block transition

The first successful authoritative mining operation that reaches `RemainingMineableBlocks == 0` while phase is `Playing` becomes the only completion trigger.

`OnBlockMined`, `OnBulkMined`, resource pickup callbacks and offline simulation may all observe zero, but they must converge into an idempotent `BeginCompletionSequence()` guard.

Order inside the trigger:

1. phase -> `CompletionLocked`;
2. freeze active clear time;
3. calculate score/bonus once;
4. set/save `ClearReached` + frozen result;
5. close skill/automation interaction;
6. disable manual/placement/automation/world events/camera input;
7. begin normal-pickup resolution and camera recenter.

No completion menu appears here.

---

# 7. Resolve ordinary pending pickups first

The current collector already exposes `CollectAllPending()`, but blindly emitting every normal presentation flight at world end may create a noisy delay before the requested ceremony.

Add an explicit completion-resolution path, for example:

```text
ResolveAllForCompletion()
```

Requirements:

- every pending ordinary reward is credited exactly once;
- zero-value mined materials are safely discarded after their block-count presentation obligation is complete;
- no pickup can remain authoritative after this stage;
- visual cleanup may be compressed into a short global pull/sweep instead of hundreds/thousands of individual HUD flights;
- this stage does **not** contribute to clear time;
- `PendingCount == 0` is asserted before the implosion begins.

The score/black-hole bonus is based only on initial world block count and clear time; unclaimed ordinary pickup value is separate and must not change the score.

---

# 8. Completion camera + implosion

## 8.1 Camera

During the pickup-resolution lead-in, smoothly place the camera at a stable completion framing aimed at `world.GetWorldBounds().GetCenter()` (or the equivalent actual bounds center).

Do not assume the world center is `(0,0,0)`.

The chosen distance must contain:

- implosion core;
- full bonus scatter radius;
- black-hole suction field.

## 8.2 Implosion

Create a dedicated presentation-only `WorldImplosionView` / ceremony controller.

Target duration: roughly `0.55-0.75s`, tuned visually.

Effect ingredients may include:

- inward-moving streaks/fragments;
- radial light contraction;
- brief center flash;
- subtle camera impulse if Reduced Motion is off.

No gameplay physics, collision or authoritative state belongs in this effect.

The world is already empty, so there is no need to physically destroy geometry again.

---

# 9. Exact-count bonus particle field

## 9.1 Core invariant

```text
visualBonusParticleCount == CompletionBonusResources
```

No visual cap, aggregation or substitution is permitted for this ceremony.

The current reviewed maximum in the Steam demo is approximately the 50³ physical total (123,412), while the future 100³ destination requires profiling at up to roughly one million exact instances.

## 9.2 Primary backend: `GPUParticles3D`

Create a `CompletionBonusParticleField` that owns a very small number of GPU particle emitters, not one node per bonus block.

Use at most a handful of representative world block appearances. Split the exact total deterministically across those emitters; their `Amount` values must sum exactly to the earned bonus.

Each emitter:

- has no collision;
- casts no shadows;
- uses a miniature low-cost block mesh/material;
- uses `OneShot`/high explosiveness or an equivalent custom emission setup;
- has an explicit visibility AABB large enough for the complete scatter/suction path;
- runs one custom particle shader.

No per-particle C# `_Process`, `Node3D`, `RigidBody3D`, `Area3D` or collision object.

## 9.3 Deterministic GPU seed

Use particle `INDEX` / `RANDOM_SEED` in the particle shader to derive:

- polar angle;
- target radius;
- tiny start delay;
- hop height;
- rotation axis/rate;
- suction spiral phase.

The CPU only supplies global ceremony uniforms and the known particle count.

## 9.4 Circular hop-out phase

The distribution plane is defined by the completion camera's `right` and `up` axes so the field visually reads as circular from the locked results camera.

For each particle:

```text
angle = hash(index).x * TAU
radius = mix(innerRadius, outerRadius, sqrt(hash(index).y))
target = center + cameraRight*cos(angle)*radius + cameraUp*sin(angle)*radius
```

The `sqrt()` radius distribution prevents everything from clustering at the center.

Motion is faked rather than simulated:

- horizontal/radial distance eases from center to target;
- a parabolic/sine component pushes the particle toward camera/world-normal for the visual "hop";
- particles have small deterministic delays;
- all settle into a stable circular field.

Initial visual target: roughly `1.6-2.2s` from center burst to settled field.

## 9.5 Black-hole spawn

Only after the bonus field has visually landed:

- spawn/scale in a dark central core;
- add a rotating accretion ring/disc;
- add restrained emission/glow;
- optional subtle distortion is allowed only if it is cheap and renderer-safe.

Do not use a physics attractor.

## 9.6 Suction phase

The same particle shader transitions into suction mode.

Each particle:

- begins at its settled radial position;
- adds increasing angular rotation;
- collapses radius toward zero using an accelerating exponential/ease curve;
- shrinks near the core;
- disappears only at the black-hole center.

Stagger suction slightly by deterministic particle seed so the field visibly drains rather than vanishing as one sheet.

Target visual duration: approximately `1.8-2.8s`, tuned after profiling.

The CPU updates only a tiny set of global uniforms such as ceremony phase/time/progress.

## 9.7 Renderer/performance fallback

If local benchmark data shows exact-count GPU particles are inferior on a required renderer, keep the controller API and replace the backend with exact-count shader-driven MultiMesh instances.

For MultiMesh fallback:

- use shader-side `INSTANCE_ID` / `INSTANCE_CUSTOM` motion;
- use bulk buffer upload rather than hundreds of thousands of C# transform calls where practical;
- provide `custom_aabb` explicitly;
- retain exact particle count.

The fallback may change implementation, never the product invariant.

---

# 10. Visual Resources count-up vs authoritative reward

During suction, animate the resource HUD toward the earned final amount:

```text
visualDisplayedBonus = floor(totalBonus * suctionProgress)
```

This is presentation only.

Do not emit one currency event per disappearing block.

When the final suction phase completes, call one guarded `CommitCompletionBonus()` transaction.

The final resource container gets one stronger confirmation pulse after the authoritative grant succeeds.

---

# 11. Crash-safe completion transaction

## 11.1 Before cinematic

Persist:

```text
ClearReached = true
CompletionClearSeconds = frozen run time
CompletionScorePercent = calculated score
CompletionBonusResources = exact bonus
CompletionBonusClaimed = false
Completed = false
```

The zero-block world state must be saved in the same checkpoint.

## 11.2 Commit point

At the end of black-hole suction:

1. guard `CompletionBonusClaimed == false`;
2. set `CompletionBonusClaimed = true` in memory;
3. grant `CompletionBonusResources` once to the current ordinary wallet;
4. mark `Completed = true` and add world ID to `CompletedWorldIds`;
5. unlock the next progression world where applicable;
6. capture world/session state;
7. save once;
8. only after successful commit, phase -> `Results` and show the completion menu.

If saving the commit fails, do not pretend the results are finalized. Keep the player in the completion state and surface an actionable save failure.

## 11.3 Reload behavior

| Saved state | Reload behavior |
|---|---|
| unfinished, blocks remain | normal 3-second intro -> Playing |
| zero blocks + `ClearReached`, bonus unclaimed | restore frozen result -> rerun completion ceremony -> commit |
| completed + bonus claimed | never grant again; expose stored results/revisit/replay behavior |

This is the key duplicate-reward invariant.

---

# 12. Results screen revision

`WorldCompleteView` must no longer appear directly on the final mining event.

It appears only after the black-hole bonus has been successfully committed.

Primary hierarchy:

```text
WORLD CLEARED

CLEAR TIME          17:42
SPEED SCORE           70%
BLACK HOLE BONUS   +47,757
TOTAL RESOURCES     63,208
```

Secondary information may retain:

- blocks removed;
- manual vs automation vs world-event share;
- next-cube copy;
- replay action.

Buttons:

- Continue / Browse Completed Worlds;
- Watch Replay where valid;
- final-demo Main Menu action.

No Continue/next-world action becomes available before the bonus transaction has completed successfully.

The existing final 50³ Steam-demo-specific messaging remains, but it moves beneath the new score/bonus hierarchy.

---

# 13. Pause / leave semantics

## Intro

Esc is allowed and pauses everything.

Pause menu may expose:

- Resume;
- Settings;
- Save & Return to Main Menu.

Do not expose a world-switch action that can race the locked intro transition.

## Completion cinematic

Esc should also remain safe to use.

Pause freezes cinematic time because the ceremony uses normal process mode, not `Always`.

Save & Return to Main Menu is allowed. Because the frozen pending completion result was already saved, returning later resumes/replays the completion ceremony without recalculating or duplicating the reward.

World Browser / competing world transitions remain disabled until the bonus commit finishes.

---

# 14. Replay scope

Do **not** record:

- top-wave per-instance transforms;
- implosion fragments;
- bonus particle transforms;
- black-hole particle suction.

The replay file remains an authoritative mining-removal stream.

The ceremony is deterministic presentation derived from:

- world identity/seed;
- initial block count;
- stored clear time/score/bonus;
- fixed effect constants.

Replaying the black-hole ceremony at the end of a read-only replay can be considered separately; it is not required for the first implementation and must never grant resources during replay.

---

# 15. Reduced Motion

Reduced Motion changes presentation intensity, not game rules.

Intro:

- remains exactly 3 seconds;
- uses smaller wave amplitude;
- removes extra settle/overshoot.

Completion:

- may reduce camera impulse, particle spin and black-hole ring motion;
- does not change exact visual particle count;
- does not change score timing or bonus amount;
- does not grant before the visual sequence is logically finished.

---

# 16. Suggested code structure

New types are preferable to putting another large cinematic directly into `GameRoot`.

Suggested structure:

```text
src/Progression/WorldRunPhase.cs
src/Progression/CompletionBonusCalculator.cs
src/Presentation/WorldIntroWaveController.cs
src/Presentation/WorldCompletionCeremony.cs
src/Presentation/CompletionBonusParticleField.cs
src/Presentation/BlackHoleVisual.cs
```

Likely existing files touched:

```text
src/App/GameRoot.cs
src/App/GameRoot.ResourceCollection.cs
src/App/GameRoot.PauseMenu.cs
src/App/GameRoot.Lifecycle.cs
src/World/Rendering/WorldView.cs
src/Presentation/OrbitCameraController.cs
src/Collection/ResourceCollectionField.cs
src/Automation/MinerSimulationService.cs
src/Save/SaveService.cs
src/UI/WorldCompleteView.cs
src/UI/MiningHud.cs   (resource count-up/presentation target only)
```

Keep scoring/reward calculation in a pure non-Node service so it can be exhaustively tested without a running scene tree.

---

# 17. Implementation order

## Phase A — pure contracts first

1. Add `CompletionBonusCalculator`.
2. Add exact boundary tests.
3. Add additive save fields and normalization.
4. Persist/restore `ActivePlaySeconds` correctly.
5. Add pending-vs-claimed completion invariants.

No new animation yet.

## Phase B — central run lifecycle

1. Add `WorldRunPhase`.
2. Gate manual mining, collection, automation, placement, skill tree, camera and world events centrally.
3. Allow Esc pause during `IntroLocked` and completion ceremony states.
4. Disable conflicting world transitions during those locked phases.

## Phase C — 3-second intro wave

1. Extend `WorldView` to retain top-surface presentation instance refs.
2. Wait for `InitialPresentationReady`.
3. Lock/default camera.
4. Calculate screen-X wave ordering.
5. Run/restore the exact 3-second wave.
6. Enter `Playing` and start clock only after full restore.

## Phase D — frozen completion checkpoint

1. Replace direct `TryCompleteWorld() -> ShowCompletion()` with guarded `BeginCompletionSequence()`.
2. Freeze and save clear result immediately.
3. Add completion-safe ordinary pickup resolution.
4. Extend offline progression result if exact offline clear offset is required.

## Phase E — camera + implosion

1. Recenter completion camera.
2. Add implosion presentation at real world-bounds center.
3. Verify Esc pauses/resumes the effect safely.

## Phase F — exact-count GPU bonus field

1. Prototype exact 123,412-particle 50³ field in the debug build.
2. Implement deterministic circular hop shader.
3. Add settled phase.
4. Add black-hole visual.
5. Add shader-driven suction.
6. Add visual resource counter count-up.
7. Benchmark 1,000,000 exact particles locally for future 100³.
8. Only switch to exact-count MultiMesh backend if measured results justify it.

## Phase G — reward transaction + results

1. Implement single guarded bonus commit.
2. Persist claimed/completed atomically.
3. Update `WorldCompleteView` with clear time / score / bonus.
4. Preserve Replay / Continue / demo-complete routing.
5. Add save-failure behavior.

## Phase H — regression / performance pass

Run the full 1³ -> 50³ progression and verify all acceptance criteria below.

---

# 18. Automated tests / contract checks

## Score boundaries

Explicit tests at:

- `0`;
- `299.999`;
- `300`;
- `599.999`;
- `600`;
- every later five-minute threshold;
- `2399.999`;
- `2400`;
- very large clear times.

Expected minimum never falls below 20%.

## Bonus rounding

Test exact integer totals including counts not divisible by 10/100.

## Timer

Verify:

- intro time adds zero;
- Playing advances;
- pause adds zero;
- completion cinematic adds zero;
- saved ActivePlaySeconds resumes correctly.

## Completion idempotency

Simulate reload at each important boundary:

- final block removed before pending-result save;
- pending-result save complete before implosion;
- during scatter;
- during suction;
- immediately before bonus commit;
- after bonus commit.

The bonus is granted exactly once.

## Input lock

During intro/completion:

- click mining fails;
- Hover Mining does not tick;
- automation does not progress;
- camera does not move;
- placement and purchases do not occur;
- Esc still opens pause.

## Rendering state

After intro completion, every tracked wave instance must equal its immutable base transform within epsilon.

No permanent world geometry mutation is allowed from the intro.

---

# 19. Local performance gates

CI can verify contracts/builds, but the high-count GPU effect needs an actual renderer.

Add a debug benchmark that can spawn the completion field without replaying a whole run.

Required local cases:

- 25 instances (5³ minimum-scale sanity);
- 6,824;
- 61,225;
- 123,412 current 50³ worst-case demo bonus;
- 1,000,000 future stress case.

Record at least:

- setup/allocation time;
- peak frame time during burst;
- peak frame time during settled field;
- peak frame time during suction;
- GPU memory delta if available;
- renderer/backend name.

The current Steam-demo 123,412 case is a release gate for this feature. The 1,000,000 case is a future-readiness diagnostic, not automatically a Steam-demo blocker.

---

# 20. Acceptance criteria

The implementation is complete only when all of the following are true:

1. Loading a fresh playable world shows the complete cube before interaction.
2. A visible left-to-right top-surface wave runs for three seconds.
3. No gameplay/camera progression occurs before the wave finishes.
4. Esc pauses and resumes the intro correctly.
5. Clear-time scoring begins only after intro unlock.
6. A partially mined revisit uses the saved world state, not a fake pristine reconstruction.
7. The final block freezes clear time immediately.
8. Remaining normal pickups resolve before the implosion.
9. Camera centers the old world so the ceremony cannot occur off-screen.
10. Implosion is centered on the actual old world bounds.
11. Visual bonus particle count exactly equals the earned integer bonus.
12. Bonus particles visibly hop outward into a circular field and settle.
13. The black hole appears only after settling.
14. Every bonus particle spirals/sucks into the black hole without gameplay physics.
15. The resource HUD visibly counts the bonus during suction.
16. The economy receives the bonus once, in one authoritative transaction.
17. Force-closing/reloading cannot reroll the score or duplicate the bonus.
18. Continue/Replay/results buttons appear only after successful reward commit.
19. Results show clear time, score percentage and exact bonus gained.
20. The 100/90/.../20 score thresholds match the five-minute contract exactly.
21. Current 50³ exact-count particle presentation is locally performant enough for release.
22. Normal CI remains green for content validation, Release build, deterministic generation and replay contracts.

---

# 21. Out of scope for this pass

Do not expand this implementation into:

- changing the underlying world generation;
- adding a second competitive leaderboard score system;
- replay file storage of cinematic particle transforms;
- physics simulation for bonus blocks;
- per-particle authoritative resource events;
- redesigning the existing progression economy around the bonus before real playtest data exists.

The black-hole bonus should be a strong world-completion reward layer on top of the current progression, not a new economy architecture.
