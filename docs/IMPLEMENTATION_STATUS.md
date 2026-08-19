# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by the real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally iterated**
- Phase 4 — Rendering + picking: **small-world path validated; large-world streaming performance validated; close-navigation/context LOD revised**
- Phase 5 — Manual mining: **complete and locally validated**
- Phase 6 — Automation framework: **implemented and locally iterated**
- Phase 7 — Skill-tree runtime: **implemented; tool-specific intelligence/speed and active drill-pattern effects added**
- Phase 8 — Skill-tree editor: **implemented; runtime graph now scrolls to larger authored trees**
- Phase 9 — World completion/progression: **implemented; transition polish added**
- Phase 10 — Save/load/offline foundation: **implemented; aggregate exhausted regions supported**
- Phase 11 — 1000-scale stress/optimization: **CPU/frame-time target passed; stress-world final art intentionally deferred**
- Phase 12 — Game-feel/reference polish: **in progress; compact HUD, progress bar, contextual close LOD and feedback implemented**
- Phase 13 — final-scale validation: **reframed to the clarified one-million-block total target and implemented as a sparse diagnostic profile**
- Phase 14 — tool-class automation/world events: **generic foundation + drill/shovel specialisation implemented; asset-dependent axe/pickaxe/gem/bomb work remains**

The user's locally added forest assets, terrain/presentation tuning, UID files, shovel asset and plan additions remain preserved on this branch.

---

## Clarified scale target

The intended late-game goal is **1,000,000 mineable blocks total**.

- `stress_1000` is deliberately pathological at 1000 x 1000 x 1000 logical address dimensions so the renderer/state architecture can be abused, but its authoritative mining target is exactly 1,000,000.
- `final_target_1m` is a separate non-progression validation profile with a 1,000,000-block target and a more plausible 128 x 128 x 128 logical address space.
- Neither diagnostic profile commits the game to a final visual/world dimension. The final million-block world remains an art/content decision and can be redesigned later without changing sparse storage/accounting.

---

## Phase 11 performance result

The first streamed renderer measured approximately 1 FPS and 1.38 seconds per chunk because it reused the exact small-world algorithm: scan 32^3 voxels and repeatedly resample neighbours through `IsExposed`.

That renderer was replaced with direct surface-column construction. The locally validated replacement benchmark was:

```text
duration_s=20.01
generator_probes=418176
generator_avg_us=8.699
probe_batch_max_ms=2.233
minimum_observed_fps=159.0
chunk_build_avg_ms=0.626
chunk_build_last_ms=0.813
stream_loads=5759
stream_unloads=1331
aggregate_blocks_mined=12348
sparse_voxel_overrides=0
exhausted_regions=9
managed_memory_mb=5.3
```

The synchronous chunk-throughput issue is therefore considered solved at the current workload. Remaining giant-world work is presentation/LOD/content quality rather than raw chunk generation cost.

---

## Large-world camera and contextual LOD

### Camera safety and zoom

Large worlds use a surface-focus camera instead of eventually pushing a centre-orbit camera through the cube.

- the actual cube support distance is calculated along the current viewing ray;
- an expanded cube is enforced as a hard final camera-position barrier;
- this remains active while orbiting and panning, including diagonal/corner views;
- `Near [3]` requests a genuine close surface inspection rather than a scaled centre distance;
- wheel zoom becomes progressively finer near the surface instead of multiplying a 500–1000-unit distance by one coarse factor;
- F9 exposes camera clearance so penetration regressions are measurable.

### Close view no longer deletes the rest of the world

The first close-detail implementation completely hid the macro proxy once local detailed chunks settled. That made the inspected blocks readable but removed the surrounding planet and weakened the sense that mined blocks belonged to one large object.

The macro proxy is now an **inset contextual shell**:

- it remains available at every zoom level;
- far/medium range uses full opacity;
- as surface focus approaches 1.0 it fades toward a low-opacity contextual ghost rather than disappearing;
- detailed supplied block meshes remain opaque in front of the inset shell;
- while the camera is being dragged or detail is catching up, macro opacity rises so navigation never occurs against an empty background;
- F9 now reports `macro context` opacity instead of only visible/hidden state.

The coarse stress-world macro geometry is still diagnostic art, not final million-block presentation.

---

## Phase 12 UI / feedback

### Compact HUD

The old fixed middle-left diagnostics panel was removed. The normal gameplay HUD now occupies a compact lower-left dock:

- world name, remaining blocks, resources and blocks-per-click;
- a thin overall mining-progress bar;
- active miner count and aggregate blocks/second;
- drill/shovel ready state;
- transient block/reward feedback;
- **H** reveals controls and engineering/tool details only on demand.

The reference camera harness is also reduced to one narrow top-left row. F9 remains an explicit engineering overlay.

### Existing game-feel feedback

- block-aware debris is capped for multi-block clicks;
- hover highlight breathes subtly and pulses on successful mining;
- drill presentation has a rotating cutting head and block-aware debris;
- skill-tree purchases have success/failure feedback and node pulses;
- completion overlay and Continue use short fade/scale transitions.

---

## Phase 7/14 — automation progression

### Drill patterns are now real runtime upgrades

The early skill data could unlock `wide_line` and `disc`, but the placed `line_miner` still read its original catalog pattern and therefore stayed a one-block tunnel. The derived skill state now contains the active primary-drill pattern.

- base drill: `line`;
- **Wide Bore** selects `wide_line` and width 3;
- **Radial Excavation** selects `disc` and width 5;
- changing drill pattern resets active primary-drill pattern enumeration to the origin, where already-mined cells are skipped naturally, so existing drills fill around their previous tunnel instead of jumping arbitrarily ahead;
- selected pattern IDs are cross-validated against the mining-pattern registry.

### Powered Shovel starts deliberately dumb

The shovel is now a proper progression path rather than starting with the intelligence of its later upgrades.

Base **Powered Shovel**:

- about **1 sand block/second**;
- sand-only;
- digs the placement tile first;
- after that, only considers the four cardinal neighboring surface tiles;
- only considers tiles at the same local surface height;
- no diagonal search, no slope following and no gap jumping;
- stops when those adjacent candidates are exhausted;
- general drill `Faster Motors` does not speed it up.

### Shovel speed branch

- **Shovel Gearbox**: repeatable, four ranks, +25% shovel speed per rank;
- **High-Torque Drive**: requires Shovel Gearbox rank 3 and adds another +50% shovel speed;
- shovel speed is an independent derived stat and affects live and bounded offline simulation consistently.

### Shovel intelligence branch

- **Slope Sensor**: allows the next neighboring sand tile to be one local block above or below the current surface height;
- **Terrain Scout**: after all nearer valid tiles fail, expands the tangential search radius to 5 and can jump to a disconnected sand patch;
- search is still nearest-shell-first, so Terrain Scout does not skip connected adjacent terrain merely because a farther candidate exists;
- an intelligence upgrade wakes already exhausted shovels so they can retry from the position where they stopped;
- saved exhausted shovels likewise revive on load when the save owns an intelligence upgrade that can make a new route possible.

### Runtime skill tree scale

The runtime tree used to have a fixed ~590px-tall canvas. The expanded shovel branches now extend deeper than that, so the runtime graph is wrapped in a scroll container and computes its required extent from node and routed-edge grid coordinates. The standalone editor remains the authoritative layout tool.

---

## Deferred Phase 14 content

The branch currently has the supplied shovel model but does not yet have dedicated final axe, pickaxe or gem models wired as runtime content. The following remain intentionally deferred rather than baking in arbitrary placeholder art:

- persistent tree-feature clearing + axe automation;
- stone/ore-specialized pickaxe automation and final model;
- deterministic multi-hit bomb blocks + blast presentation;
- clustered high-value gem pockets and final gem models.

The miner tag/pattern/stat architecture is already structured to accept these without another generic automation rewrite.

---

## Next local checkpoint

The remaining uncertainty is now genuinely runtime/visual. The repository changes touch camera-context blending, transparent macro materials, crawler topology, active pattern upgrades and runtime skill-tree scrolling; further tuning without an actual Godot run would risk optimizing the wrong behavior.

### 1. Normal-world regression

Run `play_game.bat` and confirm Verdant still launches without compile/runtime errors. LMB should mine only; RMB orbit and MMB pan should remain unchanged.

### 2. Powered Shovel progression

Use a sand patch:

1. Unlock **Powered Shovel** and place it with **N**.
2. Base shovel should remove about one sand block per second.
3. It should only move to a directly cardinal-adjacent sand tile at the same local height and should stop at a one-block slope/gap.
4. Buy ranks of **Shovel Gearbox**; mining cadence should visibly increase.
5. Buy **Slope Sensor**; a stopped shovel should wake and be able to follow a neighboring sand tile one block higher/lower.
6. Buy **Terrain Scout**; when local terrain runs out, a stopped shovel should wake and bridge to a valid sand patch within five tiles if one exists.

### 3. Drill pattern progression

With an active drill:

1. base Automation should drill a single line;
2. buying **Wide Bore** should make that existing drill start filling a 3-wide bore around its previous tunnel;
3. buying **Radial Excavation** should switch the same drill to the disc pattern.

### 4. Large-world close context

Press **F8**, then **F9**, then **3**.

- camera must remain outside the cube;
- close detail should still use real block meshes;
- the rest of the world must remain visible as a faint contextual shell rather than disappearing;
- F9 `macro context` should fall to a low non-zero opacity once detail settles and rise while moving/catching up;
- RMB/MMB navigation should remain smooth.

### 5. UI / skill-tree regression

- the compact HUD should not materially cover the playfield;
- **H** should expand/collapse details;
- **K** should open the skill tree;
- the skill-tree viewport should scroll far enough to reach Slope Sensor, Terrain Scout and High-Torque Drive.

If this checkpoint is broadly correct, the next implementation work can move away from camera/shovel iteration and into the remaining Phase 12 polish plus asset/content-dependent Phase 14 systems.
