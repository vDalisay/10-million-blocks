# Million-block mining and automation scaling research

This note records the external performance patterns reviewed for the 100³ / 1,000,000-block destination and how they map to this project's Godot/C# implementation. The goal is not to imitate another engine literally; it is to keep CPU, memory, rendering and save costs proportional to changed/visible/active work rather than total logical block count.

## Sources reviewed

- Minecraft Creator documentation — Simulation Distance, Render Distance, and Ticking Areas:
  https://learn.microsoft.com/en-us/minecraft/creator/documents/simulationrenderdistanceguide
- Factorio Friday Facts #148 — Optimizations for 0.14:
  https://factorio.com/blog/post/fff-148
- Factorio Friday Facts #176 — Belts optimization for 0.15:
  https://factorio.com/blog/post/fff-176
- Factorio Friday Facts #324 — mining drills/entity activation optimizations:
  https://factorio.com/blog/post/fff-324
- Factorio Friday Facts #421 — shared registrations/update buckets and read-only parallel work:
  https://factorio.com/blog/post/fff-421
- tModLoader issue #1430 — Tile data-oriented/value-type memory discussion:
  https://github.com/tModLoader/tModLoader/issues/1430
- Quintillion Cubes to Remove Reddit discussion:
  https://www.reddit.com/r/IndieDev/comments/1v83pxd/quintillion_cubes_to_remove_is_it_enough_for_a/

## Patterns that transfer well to 10 Million Blocks

### 1. Separate simulation from presentation

Minecraft deliberately has a smaller simulation distance than render distance. A thing can remain part of the world without receiving expensive gameplay/presentation updates every frame.

Applied here:

- authoritative mining state always commits regardless of camera location;
- hidden/back-side automation does not rebuild visible geometry per removed block;
- hidden automation collapses renderer invalidation into de-duplicated chunk markers;
- when a hidden modified area becomes visible, its presentation is reconstructed lazily from compact mined state;
- particle/debris effects are bounded and rejected when they cannot contribute pixels.

### 2. Update the changed frontier, not the accumulated history

Voxel/tile mutation is local: removing one block can only directly change the exposure of its immediate neighbours. Rebuilding or rescanning all previous excavation in a chunk makes runtime grow with playtime.

Applied here:

- full-surface base chunks retain the cheap surface-column representation;
- each exact removal records at most the removed cell plus its six neighbouring exposure candidates;
- sparse tunnel/cavity overlays rebuild from that compact frontier;
- stale frontier cells are pruned after rebuild;
- restored or intentionally deferred chunks bootstrap the frontier once from the mined bitset, then return to incremental updates.

This is the most important live-mining change from this research pass.

### 3. Put hard CPU budgets around catch-up work

Minecraft bounds active ticking areas. Factorio repeatedly uses activation lists, sleeping entities, grouped work and update buckets rather than allowing entity count to turn into arbitrary work in one tick.

Applied here:

- automation already uses a fair round-robin scheduler and preserves `WorkAccumulator` backlog;
- 1,000,000-block worlds cap normal automation work units to 24 per rendered frame;
- 100,000+ logical/target-block worlds cap them to 48;
- smaller worlds keep the original 96-unit ceiling;
- a 3×3 wide-drill work unit can remove up to nine blocks, so the million-block cap still permits a theoretical ~12,960 exact removals/sec at 60 FPS while preventing one hitch from attempting an unbounded catch-up burst.

The cap changes scheduling under saturation, not authored steady-state machine rates: work that cannot fit this frame stays in the accumulator.

### 4. Sleep/index inactive entities and bucket recurring scans

Factorio's recurring theme is “do less”: entities that have nothing to do go to sleep, mining drills avoid unnecessary repeated checks, overlapping systems share registrations, and large update sets are bucketed.

Applied here:

- stopped/blocked automations are indexed in an attention set instead of rediscovered by scanning the full fleet;
- ambient stopped-machine hover inspects only that attention set;
- automation visibility/resume policy checks at most 256 machines per refresh and walks very large fleets round-robin;
- `MinerStopped` remains immediate so player feedback is not delayed;
- active simulation remains fair round-robin under its separate mutation budget.

### 5. Compact block state, never instantiate the logical world

Terraria/tModLoader's tile memory discussion is a useful warning against heavyweight per-tile objects. The Quintillion discussion similarly revolves around full/empty chunks and sparse changes rather than literal storage for every conceptual cube.

Already present here and retained:

- untouched logical space allocates no voxel objects;
- modified chunks store mined state as compact `ulong[]` bitsets;
- fully exhausted logical regions can collapse to one aggregate marker;
- deterministic terrain is regenerated from seed/content rather than serialized as a million block objects;
- generated voxel samples use a bounded direct-mapped cache;
- renderer geometry is MultiMesh-batched by chunk/material.

### 6. Collapse completed coarse regions where semantics allow it

The Quintillion thread specifically suggests treating untouched/full chunks as one unit and marking completed chunks empty. That is a strong idea, but it must not silently remove this game's material/blocker/gem semantics.

Applied/retained here:

- aggregate region exhaustion exists for giant-world/offline/debug paths;
- sparse per-voxel deviations inside a completed aggregate region can be discarded in favor of one region marker.

Not applied to ordinary live Drills/Shovels/Rock Breakers:

- those machines care about exact material capabilities, blockers, gems, tree/terrain rules, rewards and replay order;
- replacing their work with blind chunk deletion would be faster but would change gameplay correctness.

A future explicitly-authored late-game bulk excavator can legitimately use chunk/region aggregate semantics if its design says it ignores individual blockers/materials.

### 7. Group repeated events and avoid per-block presentation overhead

Factorio gets large wins by merging many simple pieces into longer logical groups and by sharing overlapping work. For this project, the equivalent is to keep exact authoritative block removals while batching their observers/presentation.

Already applied:

- currency notifications batch over automation/manual area-mining bursts;
- HUD/counter refreshes coalesce to one presentation update per frame;
- mining pop/debris are pooled and capped;
- repeated debris fragments use MultiMesh;
- replay event storage pre-reserves capacity for larger worlds;
- replay timestamp calculation is cached once per rendered process frame because all removals in that frame map to the same 20 Hz replay tick.

## Quintillion-thread-specific observations

The developer reports that 16³ removals are smooth on Steam Deck while 32³ removals visibly freeze, and that deep holes after roughly six million removed cubes still render acceptably. The thread also explicitly calls checkerboard deletion a performance-hostile pattern. These anecdotes support two engineering conclusions:

1. a destruction operation needs a hard per-frame budget even if total world representation is sparse;
2. exposed surface complexity matters more than the raw count of blocks already removed.

That is why the renderer now tracks the exposed frontier and why automation catch-up is bounded. A checkerboard excavation can legitimately be more expensive because it maximizes visible surface area; no representation can make arbitrary visible complexity free.

## Ideas deliberately deferred

### Greedy meshing / flat-face merging

Minecraft-style greedy/quadded voxel meshes are excellent when the world is built from simple cube faces. This project intentionally uses supplied block meshes, grass fringes, water variants, trees and material-specific models. Replacing all of that with generated flat quads would materially change the current art direction. Keep MultiMesh batching for supplied meshes unless profiling proves a dedicated far-LOD surface mesh is required.

### Compute-shader world representation

Recent Godot experiments demonstrate much larger conceptual instance counts with GPU compute techniques. That is a different renderer architecture and is not necessary for a one-million-block target. It would also complicate deterministic mining, supplied mixed meshes, picking, replay and platform compatibility. Reconsider only if the product target expands by several orders of magnitude.

### Full multithreaded authoritative mining

Factorio demonstrates that read-only independent workloads can parallelize well, but deterministic mutation is much harder. Current Godot scene/render mutation stays on the main thread, while expensive work is bounded/coalesced. A future worker path should prepare immutable chunk/mesh buffers and upload them on the main/render thread rather than mutating Godot Nodes from workers.

### Save-format RLE/ranges

Terraria-style run-length compression is relevant to long contiguous excavation. Save schema 3 still expands modified chunk bitsets into local-index JSON arrays when creating a snapshot. This is primarily a save/load/disk-size concern, not the current live mining frame bottleneck. A later schema can serialize bitset words or ranges directly after runtime performance is stable.

## Current profiling targets

Use F8 -> F9 -> F7 and dense automation/manual mining. The important large-world counters are:

- FPS and GC counts;
- chunk build last/average milliseconds;
- sparse exposure pending/frontier/build count and overlay last/average milliseconds;
- automation units and max work units/frame;
- automation presentation queued/suppressed and deferred chunk count;
- mining FX active/pool/dropped counts;
- modified chunk / sparse voxel / exhausted-region counts.

The next architectural rewrite should be selected from measured data rather than from total block count alone.
