# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by the real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally iterated**
- Phase 4 — Rendering + picking: **small-world near renderer validated; bounded large-world streaming/far proxy now implemented**
- Phase 5 — Manual mining: **complete and locally validated**
- Phase 6 — Automation framework: **implemented and locally iterated**
- Phase 7 — Skill-tree runtime: **implemented**
- Phase 8 — Skill-tree editor: **implemented**
- Phase 9 — World completion/progression: **implemented**
- Phase 10 — Save/load/offline foundation: **implemented; save schema now also supports aggregate exhausted regions**
- Phase 11 — 1000-scale stress/optimization milestone: **architecture + instrumentation implemented; actual local performance measurement required**
- Phase 12 — Game-feel/reference polish: **partial**
- Phase 13 — final-scale logical architecture validation: **implemented as startup/self-test architecture; no attempt is made to render a million-wide world in block detail**
- Phase 14 — tool-class automation/world-event proposal: **foundation started; surface pattern + block affinity schema/scheduler implemented**

The user's latest local changes were pulled into the branch before this work. In particular, the added KayKit forest variants, local terrain/presentation tuning, Godot UID files, and current shovel-based miner presentation were preserved rather than overwritten.

The next checkpoint is no longer subjective design approval. It is a real performance/compatibility checkpoint: Phase 11 now has enough implementation that the remaining unknowns are actual Godot frame time, managed memory, chunk-build cost and rendering correctness on the user's machine.

---

## Phase 11 — bounded 1000-scale rendering

Large logical worlds now take a separate rendering path instead of calling the eager small-world chunk builder.

### World profile controls

`WorldProfile` now has explicit large-scale controls:

- `targetMineableBlocks`
- `aggregateRewardPerBlock`
- `regionSizeInChunks`
- `streamingThresholdMaxCoordinate`
- `streamingChunkRadius`
- `detailedSurfaceDepthChunks`
- `macroResolution`

Small authored worlds keep exact startup counting and the existing detailed renderer. Large worlds use an authored exact logical total and bounded rendering/state paths.

### Stress profile

`stress_1000` remains a 1000 × 1000 × 1000 logical address-space test but now has:

- exact authored 64-bit target counter instead of a startup world scan
- 32-voxel chunks
- 8 × 8 × 8 chunk regions
- bounded detailed streaming radius
- one detailed surface-depth chunk
- fixed-resolution whole-world macro representation

Starting this profile therefore does **not** allocate every logical voxel, chunk, region, Node3D or collider.

### Camera-driven detailed working set

`WorldView` now has two modes:

1. **eager detail** for the current small authored cubes;
2. **streamed macro + detail** for large profiles.

The streamed path:

- determines the cube face currently facing the camera
- derives a bounded surface chunk focus
- keeps only the configured tangential chunk radius/depth resident
- queues missing detailed chunks
- unloads chunks that leave the working set
- limits streamed chunk construction to one chunk per frame
- retains the existing dirty-chunk path for mining changes
- skips known exhausted regions without rebuilding their detailed voxels
- exposes streaming/build metrics

With the default stress settings the intended detailed working set is roughly `(2r+1)^2 * depth` chunks, rather than a function of the 1000-wide world volume.

### Far representation

`MacroWorldProxy` provides a bounded whole-world representation:

- samples a fixed grid on all six cube faces
- searches only a bounded distance inward for generated terrain
- groups cells into grass, sand, shallow-water, water, deep-water and stone families
- batches them through MultiMesh
- is deliberately slightly inset beneath detailed blocks
- its cost is `6 * macroResolution^2` samples/cells, independent of logical world width

This is the first far representation. It is intentionally a coarse proxy, not final LOD art.

### Picking optimization

The logical voxel raycaster no longer starts DDA traversal at the camera and walks through potentially enormous empty space. It now:

1. ray-intersects the world AABB;
2. jumps to the AABB entry point;
3. performs voxel DDA only inside the logical bounds.

This matters once camera distance and world radius become hundreds or hundreds of thousands of logical cells.

---

## Phase 11 — hierarchical mining/state

Large-world progress can now be represented without one sparse entry per mined block.

### Region quotas

For profiles with `targetMineableBlocks`, `VirtualWorld` deterministically partitions the exact total across the region address space with quotient/remainder arithmetic.

Properties:

- no per-region table is allocated at world creation
- any region's quota is available in O(1)
- the quotas sum exactly to the authored 64-bit block total
- individual mining is prevented from exceeding a region's logical quota
- once sparse mining reaches a region quota, that region compacts to one exhausted marker

### World state indexes

`WorldStateStore` now tracks:

- sparse mined local indices for partially modified chunks
- O(1) sparse-mined count per modified region
- modified chunks indexed per region
- one aggregate marker for each fully exhausted region

Exhausting a region removes its per-chunk sparse overrides and replaces them with one aggregate count. Region compaction is proportional to modified chunks in that region rather than every modified chunk in the world.

### Authoritative bulk API

`MiningService.TryExhaustRegion(...)` is the hierarchical mining API. It:

- routes through the same authoritative world state
- updates exact mined/remaining counters
- grants aggregate resources
- emits one `BulkMiningResult`
- avoids one gameplay event for every logical block represented by the region

This is currently exercised by the stress benchmark and is the path future extremely high-rate/offline tools can use.

### Save/load

World save data now stores both:

- sparse modified chunks
- exhausted region markers

Untouched deterministic terrain remains absent from the save. An exhausted region can therefore represent millions of logical changes with one small record.

---

## Performance instrumentation

`docs/PERFORMANCE_BUDGETS.md` contains the engineering budgets and measurement table.

Debug controls:

- **F8** — toggle from the normal authored world into the non-persistent `stress_1000` world and back
- **F9** — performance HUD
- **F7** — on a streaming world, run the 20-second automated stress benchmark
- **F10** — existing completion/Continue preview on an authored world

The F9 HUD reports:

- FPS
- managed memory
- GC collection counters
- eager vs streamed renderer
- camera distance
- detailed chunks loaded / queued / dirty
- last and average detailed chunk-build milliseconds
- total chunk builds and voxel candidates examined
- stream loads/unloads
- macro proxy cell count/build time
- sparse voxel count
- modified chunks
- exhausted regions
- exact mined/remaining totals

The F7 benchmark combines:

- automated camera orbit to force streaming changes
- 128 deterministic near-shell generator queries per frame
- periodic region exhaustion through `MiningService`
- FPS/memory/generator/chunk/streaming measurements

It prints a report and writes:

```text
user://stress_benchmark_latest.txt
```

Actual values in `docs/PERFORMANCE_BUDGETS.md` intentionally remain blank until measured in a local Godot run.

---

## Phase 13 — million-scale logical validation

A non-progression profile named `final_scale_1m` now exists for architecture validation.

It has:

- logical dimensions of 1,000,000 on each configured address axis
- exact authored gameplay target of **1,000,000,000,000 blocks**
- 64-voxel chunks
- 8 × 8 × 8 chunk regions
- deterministic procedural querying
- the same bounded renderer settings if it is ever loaded for diagnostics

Startup self-tests now prove, without traversing that world:

- construction leaves sparse chunk/region state empty
- the hierarchy can expose billions of addressable regions without allocating them
- arbitrary far procedural coordinates are deterministic
- a distant region quota can be calculated directly
- that distant region can be exhausted through one aggregate operation
- the exact remaining counter changes correctly
- no sparse per-voxel state is created by the aggregate operation
- quotient/remainder region accounting reconstructs the exact 1e12 target

This is the intended interpretation of Phase 13: prove the address-space and progress architecture, not create/render a trillion block instances.

---

## Phase 12 — safe game-feel work completed so far

Manual mining now reuses the existing block-aware debris presentation:

- manual clicks emit representative fragments
- grass emits mostly brown dirt with occasional green turf
- stone/ores/sand/water use their own representative colors
- multi-block manual skills cap the presentation at three bursts per click so logical mining power does not linearly multiply particle work

Further camera/UI/sound/reference polish is intentionally left until the new streaming path is measured rather than piling presentation work on top of an unprofiled renderer.

---

## Phase 14 foundation from the locally expanded plan

The plan now includes specialised tool-class automation. The underlying generic systems have been extended without forcing final balancing/content decisions.

### Miner definitions

`MinerDefinition` now supports:

- `toolClass`
- `tagRateMultipliers`
- the existing optional allowed-tag list

For example, a shovel can work at ordinary rate on generic terrain but have a 2.5× affinity for `soil` and 3× for `sand`.

### Affinity scheduler

Live and bounded offline miner scheduling now uses a work-credit model:

- one normal block costs one accumulated work unit
- a 2.5× affinity block costs 0.4 units
- the unused 0.6 work is credited back immediately
- affinity therefore composes with global miner-speed skill multipliers
- the existing per-frame operation budget remains the hard presentation/CPU cap

Allowed block tags are also honored if a future tool is restricted to a material family.

### Surface mining pattern

`surface_strip` is now a registered pure mining pattern. It walks tangentially across the local cube face in a snaking strip rather than boring inward.

A provisional `shovel_miner` content definition demonstrates:

- `surface_strip`
- surface/soil/sand targeting
- material affinity multipliers

It is not yet wired into a final skill-tree unlock or placement control. That is deliberate: the current local shovel-based presentation is preserved, while final tool roster/UX can still be authored after the additional axe/pickaxe assets and balance decisions exist.

### Not implemented from Phase 14 yet

- persistent tree-feature clearing for the axe
- axe/pickaxe final placeable content/visuals
- deterministic multi-hit bomb blocks
- region-aware blast dirtying/presentation
- gem pocket assets/content placement rules

Those are not needed to validate Phase 11/13 and should not be allowed to hide a streaming/performance regression.

---

## Required local checkpoint

A local run is now required before pushing deeper into Phase 12/14, because the unresolved questions are measurements and actual render behavior rather than code structure.

### First: normal world regression

Run:

```bat
play_game.bat
```

Confirm the normal Verdant/Lakebound path still launches and that the user's local visual/input tweaks remain intact. A quick mining/orbit check is enough; the previously validated gameplay does not need a full retest.

### Then: Phase 11 stress world

1. Press **F8**. `stress_1000` should load without trying to build the entire world.
2. Press **F9**. The HUD should say `streamed macro+detail`; detailed loaded chunks should remain a small bounded number rather than growing with the world size.
3. Orbit with RMB for ~10 seconds. The detailed patch should follow the visible cube face while the coarse whole-world proxy remains present.
4. Press **F7** and leave the benchmark running for its full ~20 seconds.
5. If there are no errors, send either:
   - the terminal output beginning with `Stress benchmark complete`, or
   - the contents of `user://stress_benchmark_latest.txt`.
6. A screenshot of the stress world with the **F9 HUD visible** is also useful because proxy/detail overlap and streaming holes are visual issues that metrics cannot reveal.
7. Press **F8** again. It should return to the authored progression world without treating stress progress as player save progress.

If entering F8 freezes/crashes or build errors occur, the error/log is the checkpoint result; no further manual investigation is expected.

Once this passes, Phase 11 has real measurements and the implementation can continue with measured LOD/cache tuning, followed by deeper Phase 12 polish and the remaining Phase 14 tool/event systems.
