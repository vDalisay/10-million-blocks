# Large-world performance review

This review targets the 40³/50³ demo worlds and the 100³ / 1,000,000-block destination. It is based on the usual high-impact real-time game optimization categories and the current Godot/C# architecture rather than generic micro-optimizations.

## Ten high-impact areas and project action

1. **Profile before guessing** — retained the F9 runtime HUD and expanded it with pooled mining-FX active/pool/drop counters. Existing chunk-build, GC, cache, resident/presented chunk, automation-presentation, and feedback metrics remain the primary regression signal.
2. **Batch repeated geometry / reduce draw calls** — terrain already uses per-chunk MultiMeshes. Mining debris now uses one MultiMesh per burst rather than 7–9 MeshInstance3D nodes; the entire mining-selection footprint is also one MultiMesh.
3. **Pool short-lived effects** — manual/replay mine-pop meshes and debris bursts are pooled with hard active caps. Existing incremental pickup flights were already pooled. Automation now routes through the same WorldView debris pool instead of allocating its own burst containers.
4. **Cull work that cannot contribute pixels** — mining pop/debris is rejected before acquisition when behind the camera or outside a padded viewport. Full-surface chunk visibility remains camera-dependent and now skips redundant rescans when the camera and resident set did not change.
5. **Reduce managed allocations and GC pressure** — replay dirty-voxel lists and renderer dirty-chunk sets are reused. Per-pop Tweens were removed. Large save JSON is streamed directly to/from files instead of materializing a complete JSON string first.
6. **Use compact/data-oriented state** — per-chunk mined state now uses a fixed bitset instead of HashSet<int>. Save snapshot compatibility is preserved by converting the bitset to the existing sorted-index representation only when a snapshot is requested.
7. **Time-slice expensive world work** — existing chunk loading/rebuild queues already stage expensive initial and modified chunk work across frames with explicit build budgets. This remains preferable to creating Godot rendering nodes from worker threads.
8. **Spatial partitioning / streaming** — existing chunk + region addressing, shell rendering, streamed detail, macro context, and deferred off-screen automation are retained. These are the correct foundations for larger-than-demo worlds.
9. **Reuse immutable resources** — debris fragments share one BoxMesh and one material globally; block assets remain registry-cached/preloaded. Drill/Axe/Pickaxe primitive meshes and materials are now shared by every physical automation instance as well.
10. **Bound cosmetic work independently from simulation** — authoritative mining is never dropped, but cosmetic pop/debris has active caps and off-screen suppression. This prevents high automation/replay speeds from turning presentation into the simulation bottleneck.

## Second implementation review pass

A second static review followed the first pooling/batching pass and focused on work that still happened once per block or once per frame even after the renderer had been optimized.

- **Stationary hover raycasts are cached.** Manual mining no longer runs the voxel DDA every frame when mouse position, camera pose, viewport size and camera FOV are unchanged. Mining and skill changes invalidate the cache so newly exposed layers and footprint upgrades remain immediate.
- **Manual footprint templates are immutable caches.** Plus3 / 3×3 / 10×10 offsets are built once instead of rebuilding arrays/lists on every hover resolution. Surrounding footprint cells use one exposure query instead of a redundant `IsPresent` + `IsExposed` pair.
- **Mining removes duplicate center/exposure sampling.** `MiningService` passes its already-inspected `BlockSample` into `VirtualWorld.TryMine`, and ordinary exposed mining performs the six-neighbour exposure check once rather than twice. Bombs retain their explicit exposed-only hit policy.
- **Dirty-chunk fanout follows chunk borders.** A mined voxel always dirties its own chunk, but neighbouring chunks are now touched only when that voxel lies on the corresponding chunk boundary. Batch/replay and blast invalidation reuse a scratch `HashSet<ChunkCoord>` and blast spheres coalesce to unique chunks before scheduling rebuilds.
- **Automation uses the same border-aware invalidation.** Hidden/deferred and visible automation no longer compute/insert six neighbour chunk addresses when all six voxel neighbours are still inside the current chunk. Deferred promotion scans also skip unchanged camera/set states and reuse scratch storage.
- **Visibility scans avoid iterator churn.** Full-surface camera-facing tests directly test the six shell conditions instead of constructing the `RelevantFullSurfaceNormals()` yield iterator once per resident chunk.
- **High-rate HUD work is coalesced.** `MiningHud`, incremental counters and the skill tree no longer rebuild strings, layouts, prerequisite states and progress values once for both `BlockMined` and `CurrencyChanged` on every mined block. Dirty flags collapse a frame's events into one UI refresh. Hidden automation/skill panels do no detailed refresh work until opened.
- **Incremental counter pulses no longer allocate Tweens per block.** Pulses are centrally advanced/reused, pickup-ready scratch lists are retained, and pickup aggregation keys no longer allocate `"block:" + id` / `"special:" + id` strings.
- **Mining debris no longer allocates `System.Random` per burst.** A tiny deterministic stack-local PRNG preserves seeded variation while keeping the pooled effect path allocation-free.
- **Tree feedback is rejected cheaply first.** Deep/non-grass blocks and off-screen mining are rejected before procedural tree sampling, preventing invisible automation from spending generation work merely to discover that no tree pickup can be shown.
- **Automation attention scans are throttled.** A continuously advancing miner fleet can fire `Changed` every frame; the stopped-miner UI now coalesces those signals and performs its O(miner-count) attention scan at a bounded interval while `MinerStopped` remains immediate.

These changes deliberately preserve authoritative mining, rewards, save data, replay order, generation and visual semantics. They target scheduling, duplicate work, allocation rate and hidden presentation only.

## Third implementation review pass

The third pass concentrated on automation fleets and the state/event paths that become disproportionately expensive once dozens or hundreds of physical miners are active.

- **Automation instance lookup is indexed.** A persistent instance-id dictionary replaces repeated linear `FirstOrDefault` / `Find` / `Contains` searches in rotor animation, attention highlighting and move validation. Per-frame rotor work is now O(rotors) rather than O(rotors × miners).
- **Built-in mining-pattern indexing is O(1).** `line`, `wide_line` and `surface_strip` candidates are calculated directly from `CandidateIndex`; only unknown future custom patterns retain the enumerator fallback. Long-lived generic miners no longer re-enumerate their entire route prefix for every step.
- **Automation debris uses the shared WorldView pool.** Live miners now submit explicit-direction debris to the same capped/cullable pool as manual/replay mining. This removes the last per-action `DrillDebrisBurst` container allocation and avoids a redundant procedural normal lookup at cube seams.
- **Automation visual updates are coalesced.** A fast miner may advance several work units in one rendered frame, and offline progress may execute tens of thousands of operations. Transform/scale/visibility work is now deferred and flushed once per touched miner rather than once per work unit.
- **Automation geometry/material resources are immutable and shared.** Drill housing, rotor, blades, axe and pickaxe meshes/materials are authored in one-block units and scaled per world. Placing another machine creates scene nodes, but no longer creates another full set of Mesh and Material resources.
- **Simulation scheduling is fair under saturation.** All active miners accrue elapsed time every frame, then the mutation budget is consumed round-robin from a persistent cursor. A fleet larger than `MaxMiningOperationsPerFrame` can no longer starve units placed later in the list.
- **Offline progress uses the same fairness rule.** Exhausted miners are skipped instead of terminating the entire offline pass, all active units receive the elapsed duration before the CPU operation cap is applied, and capped work is distributed round-robin. Remaining backlog stays in `WorkAccumulator` rather than silently losing elapsed time.
- **High-rate currency observers are batched.** Currency remains authoritative immediately and `BlockMined` remains exact per block, but `CurrencyChanged` fan-out is coalesced across one automation simulation batch or one manual footprint. A 10×10 manual footprint or dense automation frame no longer wakes every currency observer dozens of times with intermediate values.
- **Ambient stopped-miner hover performs one terrain DDA.** Terrain occlusion under the pointer is common to all candidate machines, so it is raycast once and reused while comparing candidates instead of raycasting again for each overlapping stopped miner.
- **Ordinary world-state reads skip unnecessary region math.** Exact worlds normally contain no exhausted aggregate regions. `WorldStateStore.IsMined` now avoids computing a `RegionCoord` until aggregate exhaustion actually exists, removing signed floor divisions from the hottest voxel-sampling path in the demo/full-surface worlds.
- **Known center samples propagate farther.** Placement, shovel/tree checks, automated mining and bomb exposure use `IsExposed(coordinate, sample)` / known-sample mining overloads so the mandatory six-neighbour test does not also re-read the center voxel.

This pass also fixed a correctness problem exposed by the performance review: offline progress previously `break`-ed when it encountered the first exhausted miner, which could prevent every later active miner from receiving any offline work.

## Remaining large-world ceiling

The most important remaining architectural cost is **exact modified-chunk reconstruction**. A dirty chunk currently replaces its renderer root and rebuilds its MultiMesh batches. The work is frame-budgeted and coalesced, so it is safe for the reviewed demo worlds, but sustained high-rate visible mining in a 100³ world can still make chunk reconstruction the dominant frame-time cost.

Before increasing the product world beyond the current 100³ target, profile F9 under dense visible automation. If chunk-build time is dominant, the next renderer phase should retain chunk batch objects and patch/rewrite reusable MultiMesh buffers rather than rebuilding Node3D/MultiMeshInstance3D trees. A lower-level RenderingServer buffer path is also a candidate once normal MultiMesh reuse is exhausted.

Additional review findings worth addressing only when profiling shows they matter:

- Save schema 3 still expands mined chunk bitsets into JSON integer arrays at snapshot time for compatibility. If million-block saves become a measurable hitch or disk-size problem, a future save schema should serialize compact words/ranges rather than one JSON number per mined local index.
- `ProceduralWorldSource` keeps deterministic face-column/raw-terrain dictionaries for the life of a world. This is reasonable for the current 100³ target, but a substantially larger future cube should either bound those caches or replace them with a fixed direct-mapped cache like `VirtualWorld` already uses.
- Exact full-surface chunk reconstruction remains the next change with enough architectural risk that it should be driven by an F9 trace rather than rewritten without local frame-time evidence.

## Validation

The performance pass must keep all deterministic generation and replay contracts unchanged. The automated checkpoint covers content/progression validation, Release compilation, deterministic-generation contracts and replay-codec contracts; the final large-world frame-time decision still requires the local F9 stress path because CI does not render Godot frames.
