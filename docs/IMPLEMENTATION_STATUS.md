# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented; major terrain-generation revision pending local visual validation**
- Phase 4 — Scalable rendering + picking: **near/chunk path implemented and locally validated; medium/far streaming LOD remains**
- Phase 5 — Manual mining gameplay loop: **complete and locally validated**
- Phase 6 — Automation/miner framework: **implemented; drill presentation upgraded and pending local validation**
- Phase 7 — Skill tree runtime: **implemented; schema upgraded for repeatable/rank-gated skills**
- Phase 8 — Skill-tree editor: **implemented; interactive connection/routing upgrade pending local validation**
- Phase 9 — World progression + completion overview: **implemented**
- Phase 10 — Save/load + offline automation foundation: **implemented for current small-world architecture**
- Phase 11 — 1000-scale streaming/LOD stress milestone: **next after this checkpoint**

The current branch is now at a high-impact validation boundary. The latest changes touch terrain generation, render materials, camera input, automated-miner presentation, skill-tree schema/editor interaction, world transitions, and persistence. A local build/play pass is required before investing in the medium/far renderer and 1000-scale streaming work.

---

## Terrain-generation revision

The previous generator still looked too much like random blocks on a cube. The new pipeline is explicitly staged and informed by the architecture described in Mojang/Microsoft's Minecraft world-generation documentation. See `docs/TERRAIN_GENERATION_RESEARCH.md`.

Implemented:

- broad `continentalness`-style field for land/lowland organization
- erosion field controlling retained relief
- ridge field producing coherent mountain/cliff regions
- secondary macro/weirdness variation
- lower-amplitude detail field
- configurable plateau quantization for broad stepped voxel terrain
- separate humidity, temperature, basin and forest climate fields
- hydrology as an actual generated layer instead of randomly recoloring grass as water
- shared local radial water level
- large low-region oceans plus independent inland-lake basins
- water carves/occupies space above a generated lake or sea floor
- shore rules place sand around water and continue sand below shallow water edges
- separate shallow / normal / deep water visual tiers
- shallow/deep water reuse the supplied water model with lightweight material tint variants
- cliff rules expose stone in appropriate terrain regions
- feature pass for trees using forest/humidity/temperature suitability rather than uniform random placement
- tree density increased and supplied tree instances enlarged slightly for readability
- `reference_natural` startup self-test now requires meaningful water, shallow water, deep water, beaches and a non-trivial tree population
- `reference_lakes` added as a second, deliberately more water-forward authored test profile

The startup ecology test is intended to prevent a later seed/tuning change from silently producing another world with no visible water or no trees.

### Still art-direction tuning, not architecture

- exact approved seeds
- lake shapes and frequency
- beach widths
- biome proportions
- plateau scale
- tree density/placement
- future ruins/paths/other landmarks
- final water material appearance

These are now profile/rule tuning tasks rather than a generator rewrite.

---

## Cloud revision

Clouds remain coherent persistent voxel clumps orbiting slowly around the world, but orientation is now corrected as they travel:

- each clump moves around its own orbital pivot
- the clump retains its internal block formation
- no vertical wiggle/bobbing simulation
- the carrier is oriented every frame so its local underside faces the cube-world center
- its broad/flat face therefore hugs the atmosphere instead of remaining locked to the global horizontal plane
- stars remain stationary

---

## Input revision

Requested input split is now explicit:

- **LMB:** mining and UI only
- **RMB drag:** orbit
- **MMB drag:** pan
- **mouse wheel:** zoom

The camera no longer consumes LMB at all. The reference harness copy has been updated to match.

---

## Automated drill presentation

The basic automated miner is no longer represented as a generic box/rod.

Implemented:

- cylindrical motor housing
- spinning central drill shaft
- conical drill tip
- three rotating cutting fins
- status/accent light
- drill body advances through the world to its most recently mined voxel
- mining still goes through the same authoritative `MiningService`
- block-aware debris burst at the active drill face
- grass/dirt-grass produces mostly brown debris with occasional green turf fragments
- dirt, sand, water, stone and ore families have their own representative debris colors
- debris is presentation-only and capped/sampled so future high mining rates do not require one expensive particle system per logical block

Miner state now has a serializable snapshot including position/progress/exhaustion state.

---

## Skill-tree schema/runtime revision

Skill-tree data is now schema version 2.

New prerequisite model:

```text
Prerequisite
  NodeId
  RequiredRank
  Route[]
    GridX
    GridY
```

This supports both requested behaviors:

1. visual prerequisite lines can have persistent grid-routed bends without changing the identity of either node;
2. a dependent node can require a specific rank of a repeatable source skill.

Nodes now also have:

```text
PurchaseMode = once | repeatable
MaxRank
```

Runtime validation rejects:

- unknown purchase modes
- one-time nodes with more than one rank
- repeatable nodes with fewer than two ranks
- missing prerequisites
- duplicate prerequisites
- required ranks outside the source node's valid rank range
- circular prerequisite graphs
- unknown effects

`Faster Motors` is now the placeholder repeatable example with five ranks. `Wide Bore` demonstrates a rank-gated connection by requiring Faster Motors rank 3.

---

## Skill-tree editor revision

The editor now supports graph authoring rather than only node placement.

Launch:

```bat
skill_tree_editor.bat
```

Implemented:

- LMB node drag + grid snap
- MMB canvas pan
- wheel zoom
- **Connect** mode: click prerequisite source node, then dependent target node
- prerequisite line hit testing: lines themselves can be clicked/selected
- selected line is highlighted
- selected line exposes its required source rank in the line inspector
- click an empty grid location while a line is selected to insert a snapped route bend
- multiple bends create grid-based repathing
- RMB a bend to remove that waypoint
- Clear Route returns the edge to a direct connection
- Delete Line removes the prerequisite edge
- repeatable/one-time node type selector
- editable max rank for repeatable nodes
- duplicate/delete/rename continue to preserve or clean up graph references
- Save + Validate uses the same runtime schema validator before replacing canonical game data

Effects remain raw structured JSON in this iteration so the effect system stays generic. Typed effect widgets remain a later editor polish task.

---

## Phase 9 — world progression and completion

Implemented:

- `WorldProgressionService`
- versioned `data/progression/world_progression.json`
- provisional authored sequence:
  1. Verdant Cube
  2. Lakebound Cube
- completion is detected from the exact authoritative remaining-block counter
- completion is guarded so it opens once
- manual mining and miner placement stop during completion
- automated miner simulation pauses during the overview
- overview displays total blocks, manual contribution, automation contribution and resources
- next-world preview
- Continue tears down the old session and builds the next configured profile while retaining global skills/resources
- F10 in debug builds opens the completion flow without requiring thousands of manual blocks, solely for transition testing

The progression sequence is test content, not final balancing.

---

## Phase 10 — persistence and offline foundation

Implemented:

- versioned `user://savegame.json`
- atomic-ish temp-file then replace write path
- global currency persistence
- skill ranks persisted by stable skill ID
- progression index persistence
- per-world sparse modified-chunk snapshots only
- per-world manual/automated mining contribution counters
- automated miner placement/progress snapshots
- deterministic untouched terrain remains absent from save data
- world state is restored before render chunks are rebuilt
- 10-second dirty-state autosave cadence
- saves on completed-world transition
- current small-world offline miner catch-up

Offline catch-up is exact logical mining for the current test worlds and deliberately suppresses drill debris. It is capped at 50,000 operations and seven days because the million-scale version must use the region/chunk aggregate path planned for Phase 11 rather than replaying unbounded individual mining operations.

---

## Required local checkpoint

This is the point where local validation is now necessary before Phase 11 because several new behaviors depend on actual Godot rendering/input and subjective visual comparison.

### Main game — `play_game.bat`

Validate:

1. Project compiles and launches without startup validation errors.
2. LMB only mines/clicks UI; dragging LMB no longer moves the camera.
3. RMB drag orbits and MMB drag pans.
4. Clouds remain clumped, orbit slowly, and keep their flat underside facing the world throughout the orbit.
5. Verdant Cube visibly contains coherent water bodies and sand shoreline rather than isolated blue replacement blocks.
6. Water shows readable shallow/normal/deep variation.
7. Trees are visible again and occur in coherent land regions.
8. Existing mining/highlighting still works.
9. Automated miner is visibly drill-shaped, rotates, advances into its tunnel and emits block-colored debris.
10. Close/relaunch after mining and/or placing a miner; sparse modifications, resources, skill ranks and miner state should restore.
11. Leave the game closed briefly with an active miner and relaunch; a small amount of offline work should be applied.
12. In a debug build, press **F10** to exercise the completion overview and Continue flow without mining the entire test world. Continue should load `Lakebound Cube`.
13. Lakebound Cube should visually demonstrate that the same generator can produce a more water-forward second world.

### Skill-tree editor — `skill_tree_editor.bat`

Validate:

1. Existing node drag/pan/zoom still works.
2. Connect -> source node -> dependent node creates an edge.
3. Clicking the line selects/highlights it.
4. Clicking empty grid locations while the line is selected adds snapped route bends.
5. RMB on a bend removes it.
6. Required source rank can be edited on the selected line.
7. Repeatable node type + max rank can be authored and saved.
8. Invalid required rank/cycle/missing-node data is rejected by Save + Validate.
9. A saved routed graph appears with the same routing and rank requirements in the runtime tree.

If this checkpoint is sound, implementation can proceed into Phase 11: actual camera-driven chunk streaming, medium/far terrain representation, 1000-scale stress profiling and the region-aggregate automation path.
