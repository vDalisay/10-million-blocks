# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **functionally validated; visual shortcomings are now generator tuning work**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally validated**
- Phase 4 — Scalable rendering + picking: **near-world/chunk path implemented and locally validated; later mesh/LOD tiers remain**
- Phase 5 — Manual mining gameplay loop: **complete and locally validated**
- Phase 6 — Automation/miner framework: **implemented, awaiting local interaction validation**
- Phase 7 — Skill tree runtime: **implemented, awaiting local interaction validation**
- Phase 8 — Skill-tree editor: **implemented, awaiting editor-ergonomics validation**
- Phase 9 — World progression + completion overview: **next after this checkpoint**

The user confirmed the Phase 5 build has no runtime errors and that mining works. The current procedural terrain is an improvement but is not considered final art direction; visible water frequency/placement and multiple themed world profiles remain future tuning/content work.

## Latest reference-feedback change — cloud motion

The previous cloud implementation technically moved but looked like independent blocks/layers wiggling in place. It has been replaced with coherent voxel cloud **clumps**:

- each cloud is built as one persistent flattened multi-block formation
- each clump has its own orbital pivot around the world
- orbital planes have restrained fixed inclination rather than vertical bobbing
- clumps keep their internal shape while travelling
- orbital periods are intentionally slow (roughly 1.5–3 minutes per revolution)
- a small minority travel the opposite direction to avoid a perfectly mechanical ring
- stars remain stationary

This is the motion model to validate at the current checkpoint.

## Phase 3 — virtual world + deterministic generation

Implemented:

- versioned `data/worlds/worlds.json`
- data-driven `WorldProfile`/`WorldCatalog`
- separate logical dimensions, procedural radius, render spacing, and chunk size
- exact negative-safe voxel -> chunk -> region coordinate math
- pure deterministic fractal/value-noise utility
- side-effect-free `ProceduralWorldSource.SampleVoxel`
- coherent cube-planet surface field
- depth-relative surface / soil / stone material layering on every face
- coherent water/shore/cliff masks
- deterministic ore fields
- deterministic tree/grove sampling
- sparse `WorldStateStore` containing only mined deviations
- `VirtualWorld` combining generated untouched state with sparse modifications
- exact small-world mineable count without floating-point counters
- `stress_1000` profile that can sample arbitrary coordinates without allocating its address space
- startup self-tests covering negative chunk boundaries, determinism, sparse modifications, and 1000-address-space probes

### Known visual tuning debt

- the current approved test seed does not expose enough obvious water from common camera angles
- biome proportions, lake placement, terrain silhouettes, tree density, and authored seed selection are content-tuning tasks
- several distinct themed world profiles still need to be authored after the systems stabilize

## Phase 4 — rendering and picking foundation

Implemented:

- per-chunk supplied-mesh MultiMesh batches instead of a Node3D/collider per block
- only exposed logical blocks are put into visible chunk batches
- local-face orientation for grass and decorative trees
- dirty-chunk queue with fixed rebuild budget per frame
- removal invalidates only the touched chunk plus neighboring boundaries
- no per-block physics bodies
- custom screen-ray -> voxel DDA picking against logical queries
- one reusable selection highlight

Still deferred within Phase 4 until the near representation is profiled against larger worlds:

- greedy `ArrayMesh` medium-distance terrain path
- far macro proxy/LOD path
- camera-driven chunk streaming for worlds too large to display all surface chunks simultaneously

## Phase 5 — manual mining

Locally validated:

- authoritative `MiningService`
- exposed block hover/picking
- one-block-per-click base behavior
- LMB click versus drag arbitration
- dirty chunk rebuild after removal
- deeper blocks become mineable after exposure
- exact 64-bit mined/remaining/resource counters
- HUD diagnostics

Phase 7 now extends manual mining to multiple neighboring blocks per click when the relevant skill is purchased.

## Phase 6 — automation/miner framework

Implemented:

- versioned `data/miners/miners.json`
- validated `MinerCatalog` and reusable miner definitions
- `MinerInstance` state with stable instance ID, origin, direction, progress, work accumulator, and mined counter
- `MiningPatternRegistry`
- reusable `line`, `wide_line`, and `disc` strategy implementations
- straight-line miner placement on a hovered exposed surface voxel
- miner automatically derives inward direction from the cube face it was placed on
- automated mining goes through the same `MiningService`/reward/counter path as manual mining
- automated miners can continue through the solid interior instead of requiring every target to already be externally exposed
- fixed per-frame operation budget
- dirty rendering is coalesced through the existing chunk rebuild queue
- compact miner body/direction visual
- HUD automation rate and placed-miner count
- `[M]` placement input after the Line Miner is unlocked

The wider/disc strategies are present as extensible runtime strategies, but their final placement UX and balancing are deliberately deferred until the skill/editor loop is approved.

## Phase 7 — data-driven skill tree runtime

Implemented:

- versioned `data/skills/skill_tree.json`
- stable skill IDs and content version
- node positions stored as grid presentation data
- prerequisite graph validation
- duplicate/missing ID validation
- circular prerequisite rejection
- generic effect registry rather than node-specific gameplay conditionals
- rank and cost handling
- `SkillTreeService` purchase API
- derived stats rebuilt from purchased skills
- effects currently exercised by gameplay:
  - additional manual blocks per click
  - line miner unlock
  - miner speed multiplier
- foundation effects represented for:
  - alternate mining-pattern unlocks
  - mining pattern width
  - resource-filter capability
- full-screen runtime skill-tree overlay on `[K]`
- prerequisite connections and node state display
- purchasing directly spends mined resources
- opening the tree blocks manual world input

The placeholder tree is intentionally cheap enough to test during a short local session and is not game balance.

## Phase 8 — standalone skill-tree authoring tool

Launch with:

```bat
skill_tree_editor.bat
```

Implemented:

- separate Godot tool scene; the shipping game only consumes exported JSON
- pan/zoom grid canvas
- draggable skill cards
- snap-to-grid on release
- prerequisite connection rendering
- node inspector
- stable ID, display name, category, description and cost editing
- comma-separated prerequisite editing
- raw structured effect-list editing for full effect flexibility
- create node
- duplicate node
- delete node (also removes dangling prerequisite references)
- JSON reload/import from the canonical game data
- Save + Validate back to the canonical `data/skills/skill_tree.json`
- validation uses the same runtime `SkillTreeCatalog` parser/validator before replacing game data
- output is versioned JSON read directly by the main game on its next launch

The first editor version deliberately exposes effects as JSON in the inspector rather than hard-coding an editor widget for every effect type. Once the graph ergonomics are approved, typed effect rows/dropdowns can be layered on without changing the file format.

## Local validation requested at this checkpoint

### Main game

Run `play_game.bat`.

1. Clouds should now look like persistent clumps that **slowly travel around the planet**, not blocks wiggling in place.
2. Press `K`; the skill tree should open and prevent accidental mining through the overlay.
3. Mine at least 30 resources and buy **Automation**.
4. Close the tree, hover an exposed block and press `M`.
5. A small miner should appear outside that surface and mine a straight tunnel inward over time.
6. Its mining must update the same block/resource counters as manual mining.
7. Buy **Two-Handed Mining** and confirm a normal click now removes two connected/exposed blocks rather than one.
8. If enough resources are available, buy **Faster Motors** and confirm the displayed automation rate increases.

### Skill-tree tool

Run `skill_tree_editor.bat`.

1. Pan with MMB and zoom with the wheel.
2. Drag a node; it should snap to a grid cell when released.
3. Select nodes and verify the inspector reflects their data.
4. Add/duplicate/delete should update the canvas without editing source code.
5. `Save + Validate` should reject invalid prerequisite/effect/cycle data rather than silently writing it.
6. A valid saved layout should be reflected by the runtime skill tree the next time `play_game.bat` launches.

This checkpoint is intentionally required before Phase 9 because the **miner interaction and skill-editor ergonomics are player/author-facing behavior that cannot be meaningfully approved from code alone**.
