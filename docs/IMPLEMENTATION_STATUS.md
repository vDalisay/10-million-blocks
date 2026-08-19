# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by the real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally iterated**
- Phase 4 — Rendering + picking: **small-world path validated; large-world renderer is in measured optimization**
- Phase 5 — Manual mining: **complete and locally validated**
- Phase 6 — Automation framework: **implemented and locally iterated**
- Phase 7 — Skill-tree runtime: **implemented**
- Phase 8 — Skill-tree editor: **implemented**
- Phase 9 — World completion/progression: **implemented**
- Phase 10 — Save/load/offline foundation: **implemented; aggregate exhausted regions supported**
- Phase 11 — 1000-scale stress/optimization: **first benchmark measured a severe chunk-build bottleneck; remediation implemented and awaiting rerun**
- Phase 12 — Game-feel/reference polish: **partial**
- Phase 13 — final-scale validation: **reframed to the clarified one-million-block total target**
- Phase 14 — tool-class automation/world-event proposal: **foundation started**

The user's locally added forest assets, terrain/presentation tuning, UID files, shovel presentation and
plan additions remain preserved on this branch.

---

## Clarified scale target

The intended gameplay end goal is **1,000,000 mineable blocks total**.

The earlier implementation interpreted a prior "million by million" description too literally and
created diagnostic counters of 1,000,000,000 and 1,000,000,000,000 blocks. That was not the intended
final gameplay target and has been corrected.

Current meanings:

- `stress_1000` is still a deliberately pathological **1000 x 1000 x 1000 logical address-space**
  renderer test, but its authoritative mining target is now exactly **1,000,000 blocks**.
- `final_target_1m` is a separate non-progression validation profile with an exact
  **1,000,000-block** authoritative target and more plausible cube dimensions.
- Exact final world dimensions remain a content/art-direction decision. The architecture no longer
  assumes that the final playable world itself is one million blocks wide or contains billions/trillions of blocks.

---

## First Phase 11 measurement

The first real `stress_1000` run successfully proved several architecture properties but exposed a
catastrophic render-path cost.

Measured baseline:

```text
duration_s=20.43
generator_probes=1920
generator_avg_us=8.430
probe_batch_max_ms=1.654
minimum_observed_fps=1.0
chunk_build_avg_ms=1380.179
chunk_build_last_ms=1341.115
stream_loads=19
stream_unloads=17
aggregate_blocks_mined=56000000
sparse_voxel_overrides=0
exhausted_regions=7
managed_memory_mb=8.2
```

The main result is not the 1 FPS by itself. The profile showed that a streamed chunk build averaged
**~1.38 seconds**, while an isolated procedural query averaged only **~8.4 microseconds**.

### Root cause

The old streamed `WorldView.RebuildChunk` used the small-world exact renderer algorithm:

1. scan every voxel in a 32 x 32 x 32 chunk;
2. call `SampleVoxel` for the voxel;
3. call `IsExposed` for present voxels;
4. `IsExposed` samples up to six neighbours;
5. each sample evaluates the multi-field procedural terrain noise again.

That turns one streamed build into hundreds of thousands of procedural evaluations. The call stack
captured locally (`WorldView.RebuildChunk -> IsExposed -> noise sampling`) matches the measured cost.

The original F7 benchmark also appeared to crash/freeze because `_Process(delta)` was used as its
clock. Godot clamps very large process deltas, so at ~1.4 seconds per frame the nominal 20-second test
required minutes of wall-clock time. There was no thrown runtime exception.

---

## Phase 11 remediation now implemented

### 1. Wall-clock benchmark

`StressBenchmarkController` now uses `Time.GetTicksUsec()` for elapsed benchmark time.

- 20 benchmark seconds now means ~20 real wall-clock seconds even during a severe frame-time regression.
- automated orbit uses real elapsed time as well, with a per-frame jump clamp
- orderly scene/window shutdown writes an `aborted` report if a benchmark is active
- hard OS process termination remains inherently uncatchable

### 2. Dedicated large-world surface sampler

`ProceduralWorldSource.TrySampleOutermostSurfaceVoxel(...)` is a new presentation-oriented fast path.

For one tangential column it:

- evaluates the expensive terrain/climate/hydrology fields once
- derives the outer generated radius directly
- classifies the visible surface block from the same terrain context
- uses only a two-voxel bounded quantization fallback

The exact small-world `SampleVoxel` behavior remains intact.

### 3. Streamed chunks no longer scan their volume

Large-world detail rendering now has its own builder instead of reusing the eager small-world path.

For each streamed chunk it:

- samples tangential surface columns only
- emits the one currently visible outer block for each column
- does **not** call `IsExposed` on every candidate
- does **not** scan the entire chunk volume
- only performs a short inward exact search for columns that the player has actually modified/mined
- continues to use MultiMesh batches for the resulting visible blocks

Trees remain on the exact small-world path for now; large-world tree/detail LOD should be added after
this performance path is validated rather than reintroducing expensive feature sampling prematurely.

### 4. Smaller stress chunks

`stress_1000` now uses:

- 8-voxel chunks instead of 32
- 16 x 16 x 16 chunk regions
- automatic inward detail depth sufficient to cover the terrain relief band
- a fixed 24-cell-per-face macro resolution

The working set is still bounded and independent of the 1000-wide logical volume.

### 5. Camera drag cannot be blocked by detail catch-up

`OrbitCameraController` exposes whether RMB/MMB manipulation is active.

While the user is actively orbiting or panning:

- desired streaming focus may change
- the always-present macro world remains visible
- **no detailed chunk builds are run**

After the drag is released, detail catches up with a ~2.5 ms per-frame build budget and at most four
cheap chunk builds in one frame.

### 6. Macro proxy holes/ridges addressed

The first proxy deliberately left spacing between its coarse cells and used very shallow boxes. With
large terrain-height differences this exposed black gaps and produced the Rubik's-cube ridge pattern
seen in the local screenshots.

The macro proxy now:

- uses the direct one-sample surface-column API rather than an inward search loop
- always emits all configured face cells, with a conservative seam fallback
- slightly overlaps neighbouring cells tangentially
- gives cells a deep inward skirt so height differences do not expose empty space
- keeps the outer macro face inset below detailed blocks
- disables specular response

It remains a diagnostic/far representation, not final large-world art.

---

## Hierarchical mining/state remains valid

The first benchmark did validate the non-render architecture:

- managed memory stayed small
- sparse voxel overrides stayed at zero during aggregate mining
- region exhaustion represented many logical blocks with a few markers
- no full logical world allocation occurred

`WorldStateStore`, `VirtualWorld` region quotas, `MiningService.TryExhaustRegion`, save/load aggregate
markers and bounded offline architecture remain in place. Only the authored total has been corrected
to the one-million-block gameplay target.

---

## Phase 14 foundation retained

The locally expanded plan's specialised automation work remains available:

- miner `toolClass`
- per-block-tag rate multipliers
- allowed material tags
- affinity-aware live/offline work-credit scheduler
- tangential `surface_strip` pattern
- provisional shovel miner content

Still intentionally deferred until the renderer checkpoint passes:

- final shovel placement/unlock UX
- tree-feature clearing for axe automation
- axe/pickaxe final visuals/content
- deterministic bomb blocks and region-aware blast rendering
- gem-pocket placement/content

---

## Required local checkpoint

The next local test is deliberately narrow: verify that the measured 1.38-second streamed chunk path
is actually gone before building more systems on top of it.

### Normal regression

Run:

```bat
play_game.bat
```

A quick launch/mine/orbit check on Verdant is sufficient.

### Stress regression

1. Press **F8** to enter `stress_1000`.
2. Press **F9**.
3. Confirm the counter is now `1,000,000` total rather than `1,000,000,000`.
4. RMB-orbit continuously for several seconds. Camera movement should remain responsive because detail
   builds are suspended during the drag.
5. Release RMB and watch the detail queue catch up.
6. Check the macro world for the previous black missing faces / pronounced Rubik-grid gaps.
7. Record `chunk build ms last/avg` after the detail working set has populated.
8. Press **F7**. It should now finish after roughly 20 wall-clock seconds and write the report.
9. Send the F7 report and one F9 screenshot.

### Success criterion for continuing without another renderer rewrite

The key number is streamed chunk build time. The immediate target is:

- average below **4 ms**
- routine builds below **12 ms**
- no pointer-drag stalls from chunk construction

If builds are still materially above budget, the next step is worker-thread surface sampling / cached
column contexts rather than adding more visual systems.
