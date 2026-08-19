# 1 Million Squared / 10 Million Blocks — Implementation Plan

> Working-title note: the repository remains `10-million-blocks`. The game title (`1 Million Blocks`, `1 Million Squared`, or another final name) is deliberately not coupled to the code architecture.

## 1. Product vision

Build a Godot 4 C# incremental mining game centered on a floating, block-built 3D world. The player begins by removing one visible block per click, can freely orbit/pan/zoom around the world, unlocks upgrades through a predetermined skill tree, and eventually delegates mining to configurable automated miners.

The short-term test target is worlds up to a nominal **1000 × 1000 footprint/address space**. The architecture must not need to be rewritten when progression later reaches a **1,000,000 × 1,000,000 address space** or similarly enormous logical worlds.

The key design invariant is:

> **The number of mineable blocks is gameplay data, not an equal number of Godot nodes, collision bodies, or always-resident voxel records.**

The world may be enormous logically while only a small part is generated, simulated in detail, and rendered at any one time.

---

## 2. Reference visual target

The supplied screenshot and video establish the visual target and should be treated as reference material, not loose inspiration.

Required presentation characteristics:

- A compact floating voxel world remains the visual focus in the center of the screen.
- Overall silhouette reads as a cube/rounded cube or "cube planet," while local terrain is irregular and organic.
- Terrain wraps around visible faces instead of looking like a normal flat Minecraft map.
- Bright green grass, layered dirt/stone, recessed blue water, cliffs, terraces, trees/ruins, and occasional constructed features create readable visual landmarks.
- Large empty dark-space margins keep the world isolated and toy-like.
- White blocky clouds float around the world at different depths.
- Strong directional lighting, dark ambient space, depth shading, and restrained atmospheric/post-processing effects provide the high-contrast look seen in the reference.
- Camera movement is smooth and orbital, with enough zoom range to move from a distant complete-world view to a much closer terrain inspection view.
- The game UI must avoid consuming the center of the screen or obscuring the world.

### Visual-validation requirement

Create a dedicated `ReferenceVisualHarness` during the first implementation slice. It will expose fixed camera presets approximating the supplied far, medium, and near reference shots. Every visual-system change can be checked against the same poses rather than relying on memory.

---

## 3. Current repository assessment

The current `main` branch is the correct base for future work because it contains the newest asset import work and is otherwise still close to a clean Godot project.

The repository already contains the supplied block asset family in several formats (`fbx(unity)`, `fbx`, `gltf`, and `obj`) and includes terrain/resource/decorative variants such as:

- grass / grass-with-snow
- dirt / dirt-with-grass / dirt-with-snow
- stone / dark stone
- copper, silver, and gold stone
- sand variants
- gravel variants
- water and lava
- wood, metal, glass, brick
- colored / striped / decorative blocks
- trees and snow trees

### Canonical runtime asset path

Use `Assets/gltf` as the initial canonical source for runtime assets because it maps cleanly into Godot's normal import pipeline. Keep the other supplied exports untouched as source/reference alternatives.

Do not duplicate these source models into gameplay scenes. Build a `BlockAssetRegistry` that maps stable block IDs to their imported resources.

### Existing prototype branch

`agent/initial-playable-demo` is useful as a disposable technical prototype, but it is **not** the foundation for this implementation. It diverged before the newest asset commit and its world model uses a dictionary entry per voxel and a generator that enumerates and sorts candidate voxels. That is acceptable for a tiny prototype but is the wrong baseline for the requested scale.

Concepts worth selectively reusing after review:

- central mining service
- voxel DDA picking
- chunk-oriented rendering
- separation between game state and rendering

Do not merge that branch wholesale.

---

## 4. Scope of the first implementation program

The first program should establish systems, not final balancing or final content.

### In scope

- deterministic procedural world generation
- editable world profiles and thematic palettes
- scalable sparse/chunked world state
- block rendering using the supplied asset set
- orbit/pan/zoom camera
- hover/highlight/select visible blocks
- manual mining, initially one block per click
- block rewards/resources
- automated-miner framework
- one initial straight-line miner behavior
- extensible mining-pattern system
- comprehensive data-driven skill-tree runtime
- separate visual skill-tree editing tool with grid layout
- world completion detection
- completion/overview screen
- Continue -> next world flow
- save/load foundation
- stress-test profiles up to 1000 × 1000 logical footprint
- profiling and debug instrumentation

### Explicitly deferred

- final world progression sequence and balancing
- final skill-tree values/text/icons
- final economy tuning
- final sounds/music
- final Steam integration
- achievements
- final title/name migration
- complete million-scale content generation
- final biome roster

These systems must nevertheless be designed so the deferred content is configuration rather than a rewrite.

---

## 5. Core architecture

Use a data-oriented hierarchy with rendering as a cache of logical state.

```text
GameSession
  ├── PlayerProgress
  ├── SkillTreeService
  ├── WorldProgressionService
  └── ActiveWorldSession
        ├── WorldProfile
        ├── ProceduralWorldSource
        ├── WorldStateStore
        │     ├── Region/Macro state
        │     └── Materialized Chunk state
        ├── MiningService
        ├── MinerSimulationService
        └── WorldView
              ├── ChunkRenderScheduler
              ├── TerrainChunkRenderer
              ├── DecorativeMultiMeshes
              ├── SelectionHighlight
              └── OrbitCamera
```

Strict ownership rules:

1. `ProceduralWorldSource` answers what an untouched coordinate *would be* from profile + seed.
2. `WorldStateStore` records only deviations from that untouched state and aggregate exhausted regions.
3. `MiningService` is the only authority that changes mined state and awards mining output.
4. Automation uses the same `MiningService` as manual clicks.
5. Renderers never own authoritative gameplay state.
6. UI never directly mutates world state.

---

## 6. Data model

### 6.1 BlockDefinition

Each block/resource type is data-driven.

Suggested fields:

```text
Id
DisplayName
AssetPath
Material/visual tags
Hardness
BaseValue
ResourceDrops[]
Tags[]               // stone, ore, surface, liquid, etc.
TransparencyMode
RenderClass          // terrain, liquid, decorative, special
MiningRules
```

Stable string IDs must be used in saved data and skill/world definitions; enum ordinal values must not become save-format contracts.

### 6.2 WorldProfile

A world must be fully selectable through data rather than hard-coded stage numbers.

Suggested fields:

```text
Id
DisplayName
SeedPolicy / authored seed
LogicalWidth
LogicalHeight
LogicalDepth / depth policy
TargetMineableBlockCount (optional, depending on shape mode)
WorldShape
GeneratorPresetId
BiomePresetId
BlockPaletteId
FeatureSetId
ResourceDistributionId
StartingCameraDistance
CompletionRule
UnlockRule
CompletionRewards
NextWorldIds[]
```

This deliberately keeps **dimensions** separate from **total mineable block count**. A 1000 × 1000 footprint, 1000³ volume, and "exactly 1,000,000 blocks" are different concepts and must not be conflated in code.

### 6.3 Numeric policy

- Local voxel coordinates can remain 32-bit integers for the intended coordinate range.
- All block totals, mined totals, and progression counters use checked 64-bit integers at minimum.
- If the incremental economy is expected to exceed 64-bit currency values, introduce a dedicated scientific-notation/BigNumber economy type before balancing instead of leaking `double` into authoritative counters.
- Never use floating-point numbers as exact block counters.

---

## 7. Procedural world generation

## 7.1 Do not use marching squares/cubes for the primary terrain

The reference is intentionally made of discrete square blocks. Marching Squares is 2D and Marching Cubes/Surface Nets would generate a smoothed continuous surface, which would work against the required silhouette.

Instead use procedural scalar fields/noise to decide **which block occupies an integer voxel coordinate**, then render exposed voxel faces/asset instances.

### 7.2 Generation pipeline

Use a deterministic layered generator:

1. **World-space shape field**
   - rounded cube / cube-planet signed-distance style base
   - configurable face thickness and corner rounding
   - optional terraces/face warping

2. **Macro terrain field**
   - low-frequency FastNoiseLite layers
   - domain warp where useful
   - large plateaus, basins, cliffs, valleys

3. **Surface detail**
   - higher-frequency noise
   - stepped/quantized heights to retain clear voxel terraces

4. **Biome/theme sampling**
   - temperature/moisture or authored masks
   - world profile decides available palettes

5. **Subsurface materials**
   - dirt/stone layering
   - ore veins/pockets using deterministic 3D noise and hashed seeds

6. **Water/liquid placement**
   - bounded lakes/basins rather than full dynamic fluid simulation for the first version

7. **Features**
   - deterministic hashed placement of trees, ruins, towers, decorations, unusual landmarks
   - placement rules query terrain slope, height, biome, and neighborhood

8. **Authored cleanup pass**
   - world-generation tooling must allow generating candidate seeds and previewing them
   - approved seeds/settings become committed `WorldProfile` content

### 7.3 One-time authored procedural worlds

The player does not need a random new landscape every playthrough. The generator is primarily a **content-authoring system**:

- we generate/inspect worlds during development
- desirable seeds and parameter sets are committed
- the shipping game reproduces exactly those worlds deterministically

This gives handcrafted-quality control without manually placing every block.

### 7.4 Generator contract

The most important API is conceptually:

```text
BlockSample SampleVoxel(WorldProfile profile, int x, int y, int z)
```

It must be deterministic and side-effect-free. Untouched chunks can therefore be regenerated instead of serialized.

---

## 8. World storage and million-scale strategy

A logical world must not be allocated as one enormous 2D/3D array.

### 8.1 Chunk layer

Start with a configurable logical chunk size (initial candidate: 32³; benchmark 16³/32³/64³).

A materialized chunk contains tightly packed data rather than dictionaries of `Vector3I -> object`:

- compact block-type palette indices
- mined/present bitset
- optional damage data only for partially damaged blocks
- optional per-chunk resource/feature metadata
- dirty flags
- cached exposed-face information

Only chunks close enough to matter are materialized.

### 8.2 Sparse untouched state

Untouched chunks have **no save record**. Their contents come from deterministic generation.

A partially mined chunk stores only the compact state needed to reproduce its modifications.

A fully mined chunk can collapse to a tiny `Exhausted` marker plus statistics.

### 8.3 Macro/region hierarchy

Huge late-game automation cannot iterate millions or billions of voxels individually. Add a second hierarchy above chunks.

Example:

```text
World
  -> Region / MacroChunk
       -> Chunk
            -> Voxel
```

A region can be represented as:

- untouched/generated
- partially materialized
- fully exhausted
- aggregate resource summary when safe to precompute/cache

High-level automation is allowed to mine an entire eligible chunk/region as one **batched logical operation**, while still producing exact mined-block/resource counts.

This is what makes a 1,000,000 × 1,000,000 address space feasible: the game reasons hierarchically instead of visiting every coordinate every frame.

### 8.4 Cache policy

Maintain separate caches for:

- simulation-active chunks
- rendering-visible chunks
- recently used chunks

Use explicit memory budgets and LRU-like eviction for generated data. Rendering visibility must not imply permanent simulation residency.

---

## 9. Rendering strategy

The supplied 3D models are mandatory visual source assets, but instantiating one Godot node per block is prohibited.

Use a hybrid system.

### 9.1 Small/near worlds

For small block counts and close camera distances:

- use the actual supplied model meshes
- batch identical models using `MultiMeshInstance3D` per block type and spatial chunk
- keep one `MultiMesh` per manageable spatial area so culling remains useful

This preserves the authored uneven/block-detail look visible in the asset pack.

### 9.2 Medium/large terrain

For dense terrain:

- generate an `ArrayMesh` per visible chunk
- emit only exposed faces
- merge compatible coplanar faces with greedy meshing where it does not visibly damage the supplied style
- retain material/UV appearance derived from the canonical block assets
- keep liquids/special surfaces in separate surfaces/material groups

### 9.3 Sparse decorative geometry

Trees, ruins, special crystals, structures, and other visually distinctive objects can continue using the actual imported meshes, batched with MultiMesh where repeated.

### 9.4 LOD tiers

The reference video spans a large zoom range. Implement rendering LOD explicitly:

- **Near:** individual/block-detail representation + high-quality decoration
- **Medium:** chunk meshes with full silhouette and major features
- **Far:** macro surface/proxy meshes, simplified decoration, cheap clouds

LOD changes must preserve world topology; they change presentation only.

### 9.5 Chunk rebuild scheduling

Mining invalidates only affected chunks and neighbors touching a modified boundary. Rebuilds are queued and applied under a strict per-frame budget rather than synchronously rebuilding everything touched by a large mining batch.

---

## 10. Camera, navigation, picking, and highlighting

### Orbit camera

Required controls:

- drag to orbit around the world's center/pivot
- vertical pitch clamping to avoid awkward inversion
- scroll to zoom
- optional pan with a secondary gesture, bounded so the world can easily recenter
- smooth damping/inertia modeled after the reference video
- automatic camera-distance limits derived from current world bounds
- `Focus/Recenter` action for recovery

### Input arbitration

A click intended for mining must not accidentally rotate the camera. Use a drag threshold:

- press + short movement + release = mine/select
- movement beyond threshold = camera orbit

UI input consumes events before world interaction.

### Picking

Do not create a collider for every block.

Use screen ray -> custom voxel DDA traversal through the logical world/chunk query. The first present/visible mineable voxel becomes the target.

### Highlight

Use one reusable selection visualization:

- slightly enlarged wireframe/outline cube or shader overlay
- snaps to target voxel
- color/status can communicate mineability, hardness, filtered miner target, etc.

Do not spawn/destroy highlight nodes per hover.

---

## 11. Manual mining system

All mining flows through one `MiningService`.

Initial player behavior:

- one click requests one block of mining work
- base mining power removes one normal starter block per successful click
- hard blocks may later require accumulated damage or increased power depending on balancing
- successful mining removes/marks the logical block, awards drops, updates exact counters, emits presentation events, and queues affected render updates

Suggested command abstraction:

```text
MiningCommand
  Source                // manual, miner, offline, debug
  Origin
  TargetSelector
  Pattern
  Power
  MaxBlocks
  ResourceFilter
  SimulationBudget
```

This prevents later "mine 1,000,000 blocks/sec" upgrades from needing a separate parallel gameplay implementation.

### Feedback events

MiningService should emit semantic events such as:

- BlockDamaged
- BlockMined
- MiningBatchCompleted
- ResourceAwarded
- ChunkExhausted
- WorldCompleted

Particles, sounds, text popups, camera shake, and UI counters subscribe to these events rather than being embedded in mining logic.

---

## 12. Automated miner system

Automated miners are **simulation agents**, not physics-heavy objects.

A miner definition contains:

```text
Id
DisplayName
BaseRate
PatternId
PatternParameters
Power
AllowedBlockTags
ResourceFilter capability
Range
VisualPrefab/asset
Upgrade hooks
```

### 12.1 Initial miner

The first automated miner should:

- be placeable on/near a valid world surface
- have an orientation/direction
- mine in a straight line from its starting point
- stop, skip, or retarget according to data-configured behavior when blocked/exhausted
- visibly indicate its path/working direction

### 12.2 Pattern strategy system

Mining shape is a strategy/data object, not switch statements throughout the miner code.

Planned pattern families:

- Line
- WiderLine / Tunnel
- Plane / Strip
- Circle / Disc
- Sphere
- Cone
- Spiral
- Branching/Bore
- SurfaceSweep
- NearestMatchingResource

Patterns return candidate ranges/batches. The MiningService decides what is actually mineable and applies budget/power/filter rules.

### 12.3 Scaling automation

At low rates, a miner may show block-by-block visual work.

At very high rates:

- sample/animate only representative mining events
- calculate bulk work at chunk/region level
- coalesce render invalidations
- never run one Godot callback per mined block

The player must receive exact progression/resources even when presentation is deliberately sampled.

---

## 13. Skill-tree runtime

The skill tree must be completely data-driven and predetermined by shipped content.

### 13.1 Skill node schema

Suggested node fields:

```text
Id
DisplayName
Description
GridX
GridY
Category
Icon
PrerequisiteNodeIds[]
UnlockRequirements[]
CostDefinition
MaxRank
Effects[]
VisibilityRule
```

### 13.2 Generic effect system

Do not encode one C# class per individual skill node. Define reusable effects, for example:

- MultiplyManualMiningPower
- AddManualBlocksPerClick
- IncreaseMiningRadius
- UnlockMiner
- AddMinerRate
- MultiplyMinerRate
- UnlockPattern
- UnlockResourceFilter
- IncreaseMinerRange
- UnlockBlockTypeYield
- MultiplyResourceYield
- ReduceBlockHardnessByTag
- UnlockWorldFeature
- IncreaseOfflineEfficiency

Effects are validated against a registry and compiled/evaluated into derived player stats.

### 13.3 Runtime rules

`SkillTreeService` owns:

- prerequisite evaluation
- purchase validation
- costs
- ranks
- unlock events
- derived modifier aggregation
- serialization

Gameplay systems query derived capabilities/stats; they do not ask UI nodes whether a skill is owned.

---

## 14. Separate skill-tree editor tool

Build a standalone Godot tool under `tools/skill_tree_editor/`. It may live in the same repository/project or a small sibling project if separation later proves cleaner, but the shipping game must consume exported data rather than depend on editor nodes.

### Required editor UX

- large pan/zoom canvas
- visible configurable grid
- create node
- drag node
- snap node to grid
- connect/disconnect prerequisites
- multi-select
- inspector panel for all node fields
- copy/duplicate/delete
- category/filter controls
- search by ID/name
- preview locked/unlocked/purchased states
- preview total cost/path cost
- auto-layout assistance optional, never mandatory

### Validation before export

Block export when there are hard errors:

- duplicate IDs
- missing prerequisite IDs
- circular prerequisite graph
- invalid effect IDs
- missing required effect parameters
- invalid rank/cost definitions
- invalid/unparseable resource IDs

Warn, but allow export, for:

- unreachable optional nodes
- overlapping grid positions
- disconnected branches
- suspiciously large costs

### Output format

Use a versioned canonical JSON format, e.g.:

```text
skill_tree.schema_version
skill_tree.content_version
nodes[]
```

The game loads/validates this data into immutable runtime definitions. Later we can add an import/compile step into `.tres` resources if profiling shows a reason; JSON remains the editable interchange format.

Grid coordinates are presentation data. Rearranging nodes in the editor must never change their persistent identity or purchased save state.

---

## 15. World progression and game flow

World progression must be data-driven because the exact sequence is intentionally undecided.

Initial test sequence can contain deliberately small profiles solely to validate flow, for example:

```text
1-block proof world
10-ish block proof world
100-ish block world
10 × 10 test profile
100 × 100 test profile
1000 × 1000 virtualized stress profile
```

These are test content, not a promise of final progression.

### Completion

Maintain an exact authoritative `remaining mineable blocks` counter. Do **not** scan the world to discover completion.

When remaining count reaches zero:

1. freeze new mining requests
2. let queued presentation settle briefly
3. open world-completion overview
4. show statistics/rewards/unlocks
5. save progress
6. enable Continue
7. asynchronously prepare the next world's initial visible chunks
8. transition camera/presentation
9. activate next world

### Overview content foundation

Support fields for:

- blocks mined
- completion time
- manual vs automated contribution
- resources discovered
- rare blocks found
- miners used
- milestone/skill unlocks
- next-world preview

Final layout/content can come later.

---

## 16. UI architecture

Keep the central world visually dominant.

Initial HUD should be minimal:

- total/current-world blocks mined
- remaining blocks or progress indicator
- primary resource/currency
- current manual mining power / blocks per click
- automation rate
- Skill Tree button
- Miner/Automation button
- settings/pause

### Skill tree

Open as a dedicated full-screen or large overlay view. Simulation pause behavior should be a setting/policy rather than implicitly tied to UI visibility.

### Mining UI

Hovering a block can show compact contextual information without requiring a large tooltip at all times:

- block/resource name
- hardness/progress when relevant
- expected reward

### Debug UI

Development builds need a collapsible diagnostics panel showing:

- FPS/frame time
- active/visible/materialized chunks
- mesh rebuild queue
- chunk generation queue
- generated face count
- draw-call approximation
- logical blocks represented
- resident voxel bytes
- mining operations/sec
- batched mining operations/sec
- current LOD distribution
- save-delta size

---

## 17. Save/load and offline progress

### Save principles

Never serialize the full generated world.

Persist:

- save schema version
- player progression/resources
- skill purchases/ranks
- unlocked worlds/features
- active world profile/seed
- exact world counters
- automated miner definitions/placements/state
- modified chunk records only
- exhausted region/chunk markers
- partial damage records only where necessary

Untouched terrain is regenerated from seed + content version.

### Compatibility

World and skill definitions require stable IDs and content versions. Add migration hooks before public saves exist so we do not need to retrofit them later.

### Offline mining

Offline progression must use the same miner capability/rate model but operate in aggregate time slices. It must not replay every simulated mining tick that occurred while the game was closed.

---

## 18. Threading and work scheduling

Keep all active SceneTree/resource mutation on the main thread.

Worker tasks may perform pure/data work:

- deterministic chunk generation
- block/material arrays
- exposure calculation
- greedy-mesh data generation where API usage allows pure buffers
- region summaries
- save compression

Then submit compact results to a main-thread apply queue.

### Main-thread budgets

Do not apply unlimited completed work in one frame. Use tunable budgets such as:

- maximum chunk mesh applies per frame
- maximum milliseconds of render-cache maintenance per frame
- maximum presentation mining effects per frame

Large mining batches should remain responsive even if visual updates trail the authoritative simulation by a few frames.

---

## 19. Performance targets and constraints

Initial development target:

- 60 FPS at 1920 × 1080 on a normal desktop target machine
- no per-block Node3D/collider architecture
- no full-world iteration during normal frames
- no synchronous generation of the complete 1000 × 1000 test space
- no frame-dependent mining outcomes
- deterministic generation from committed world profile + seed

Suggested frame budgets after baseline profiling:

- routine world simulation: < 1 ms CPU average
- chunk generation/mesh application on main thread: budgeted to roughly 1–2 ms/frame under streaming load
- UI: < 1 ms typical
- avoid managed allocations in tight mining/render loops

The numbers are starting targets, not acceptance criteria until measured on representative hardware.

### Stress scenarios

Automated benchmark scenes/tests should cover:

1. orbiting a fully intact dense world
2. rapidly mining one block every frame
3. mining a line crossing chunk boundaries
4. mining a circular/large batch
5. thousands/millions of logical mining operations aggregated into chunk batches
6. zooming from near to far LOD quickly
7. switching worlds while generation jobs are queued
8. saving a heavily modified world
9. loading a heavily modified world
10. opening/closing skill tree while automation continues

---

## 20. Testing strategy

### Pure unit tests

High priority because much of the architecture is deterministic data logic:

- coordinate -> chunk/region mapping, including negatives and boundaries
- procedural generation determinism
- exact target block counts where profile requires them
- block palette resolution
- mining power and hardness
- resource drops
- batch mining exactness
- world remaining-count invariants
- skill prerequisites/cycles/costs/effects
- miner pattern coordinate generation
- region exhaustion aggregation
- save round trips
- schema migration

### Integration tests

- click a highlighted block -> exactly expected logical state change
- mining on a chunk edge rebuilds correct neighboring chunks
- miner line crosses chunks without duplication/skips
- completion event fires once
- Continue loads correct profile
- purchasing a skill changes only intended derived stats
- editor export loads successfully in runtime

### Visual/manual QA

A checklist using the `ReferenceVisualHarness` should verify:

- cube-world silhouette
- material fidelity to supplied assets
- lighting contrast
- cloud scale/depth
- water readability
- camera orbit feel
- far/medium/near framing
- block highlight clarity
- destruction feedback without losing reference aesthetic

---

## 21. Proposed repository structure

```text
Assets/
  gltf/                         # supplied canonical runtime source assets

scenes/
  Main.tscn
  gameplay/
  ui/
  debug/

src/
  App/
    GameRoot.cs
    GameSession.cs
  Core/
    Events/
    Math/
    Serialization/
  Content/
    BlockDefinition.cs
    BlockAssetRegistry.cs
    WorldProfile.cs
    ContentDatabase.cs
  World/
    Generation/
      ProceduralWorldSource.cs
      WorldGeneratorConfig.cs
      Biomes/
      Features/
    Storage/
      ChunkCoord.cs
      RegionCoord.cs
      ChunkState.cs
      RegionState.cs
      WorldStateStore.cs
      ChunkCache.cs
    Rendering/
      ChunkRenderScheduler.cs
      TerrainMesher.cs
      DetailMultiMeshRenderer.cs
      WorldLodController.cs
    Interaction/
      VoxelRaycaster.cs
      SelectionHighlight.cs
  Mining/
    MiningService.cs
    MiningCommand.cs
    MiningResult.cs
    MiningPatterns/
  Automation/
    MinerDefinition.cs
    MinerInstance.cs
    MinerSimulationService.cs
  Skills/
    SkillNodeDefinition.cs
    SkillEffectDefinition.cs
    SkillTreeService.cs
  Progression/
    PlayerProgress.cs
    WorldProgressionService.cs
  Presentation/
    OrbitCameraController.cs
    CloudField.cs
    ReferenceVisualHarness.cs
  UI/
    HudController.cs
    SkillTreeView.cs
    WorldCompleteView.cs
  Save/
    SaveService.cs
    SaveSchema.cs

data/
  blocks/
  worlds/
  biomes/
  skills/
  miners/

tools/
  skill_tree_editor/
  world_preview/

tests/
  Unit/
  Integration/

docs/
  IMPLEMENTATION_PLAN.md
  PERFORMANCE_BUDGETS.md
  CONTENT_SCHEMAS.md
```

Exact names can evolve, but the separation between content, logical world state, mining, automation, and rendering should remain.

---

## 22. Implementation phases

## Phase 0 — Plan and baseline

**Goal:** establish the agreed architecture before code changes.

Tasks:

- keep this document as the implementation contract
- base work on current `main`
- do not merge `agent/initial-playable-demo` wholesale
- record reference screenshot/video as external QA references
- identify canonical glTF scale/orientation/material behavior

Exit criteria:

- plan reviewed/accepted
- no gameplay implementation committed as part of this phase

---

## Phase 1 — Project foundation + asset catalog

**Goal:** clean runnable C# Godot project that can display supplied assets reliably.

Tasks:

- add `.csproj`/solution configuration compatible with current Godot version
- create `Main.tscn` / `GameRoot`
- create content loading/validation foundation
- build `BlockAssetRegistry`
- register representative grass, dirt, stone, water, ore, wood, and tree models
- normalize/document model scale, pivot, orientation, materials
- add build/play helper scripts only if useful and robust
- add CI compile check

Exit criteria:

- project launches cleanly
- representative supplied assets render correctly
- missing/invalid asset IDs fail with clear diagnostics

---

## Phase 2 — Reference visual slice

**Goal:** match the screenshot/video presentation before building deeper gameplay.

Tasks:

- create a small temporary cube-planet scene
- implement orbit/zoom camera
- implement reference camera presets
- dark-space/star background
- block clouds at layered depths
- lighting/environment pass
- water/terrain/trees from supplied assets
- establish far/medium/near LOD expectations

Exit criteria:

- central composition and camera behavior visibly resemble supplied reference
- user can orbit smoothly without UI/gameplay interference
- no need for final procedural generator yet

**Local user verification checkpoint recommended here.**

---

## Phase 3 — Virtual world data + deterministic generator

**Goal:** generate/query large logical worlds without materializing them all.

Tasks:

- `WorldProfile`
- chunk/region coordinate types
- `ProceduralWorldSource`
- rounded-cube world field
- noise/biome layers
- thematic palettes
- deterministic feature placement
- chunk cache
- region/chunk state representation
- generator tests
- temporary world-preview/debug controls for seed/profile switching

Exit criteria:

- same seed/profile reproduces identical samples
- arbitrary chunk can generate independently
- 1000 × 1000 logical profile can be addressed without proportional startup allocation
- approved small world resembles reference terrain language

---

## Phase 4 — Scalable rendering + picking

**Goal:** turn virtual world state into responsive visual chunks.

Tasks:

- exposed-face terrain mesher
- benchmark greedy-mesh variants
- MultiMesh detail path using actual supplied meshes
- chunk render scheduler
- near/medium/far LOD system
- DDA voxel picking
- hover highlight
- chunk dirty-boundary propagation
- render diagnostics

Exit criteria:

- only nearby/visible chunks consume meaningful render resources
- hover target remains accurate while orbiting
- removing test voxels updates only necessary chunks
- no per-block physics bodies

---

## Phase 5 — Manual mining gameplay loop

**Goal:** first genuinely playable incremental loop.

Tasks:

- MiningService/MiningCommand
- one-block-per-click starter behavior
- hardness/damage foundation
- block/resource rewards
- progression counters
- HUD
- destruction events and basic visual feedback
- exact remaining-block count

Exit criteria:

- player can orbit, select, click, and remove any exposed reachable block
- each successful starter click removes exactly one appropriate block
- counters/rewards are exact
- interior blocks become targetable as surrounding blocks disappear

**Local user verification checkpoint recommended here.**

---

## Phase 6 — Automation/miner framework

**Goal:** automation is a first-class scalable mining source.

Tasks:

- miner definition/content schema
- placement/orientation interaction
- miner simulation scheduler
- `MiningPattern` interface
- straight-line miner
- path visualization
- batch mining API
- representative effect sampling at high rates
- skeleton patterns for circle/strip/resource filters

Exit criteria:

- placed miner reliably advances in a straight line
- manual and automated mining produce identical authoritative rules/rewards
- high simulated rates do not require one node/event/render rebuild per mined block

---

## Phase 7 — Skill tree runtime

**Goal:** upgrades can unlock/modify every important mining dimension through data.

Tasks:

- schema + validator
- prerequisite graph
- cost/rank handling
- generic effect registry
- derived player/miner stats
- purchase API/events
- initial placeholder tree covering manual power, automation, miner rate, line width, alternate patterns, resource filtering

Exit criteria:

- tree can be replaced by data without recompilation
- purchases persist by stable skill ID
- effects alter intended systems without skill-specific conditionals spread through gameplay code

---

## Phase 8 — Skill-tree editor

**Goal:** user can rearrange and edit the predetermined tree without touching code.

Tasks:

- editor canvas/grid
- drag/snap nodes
- prerequisite connections
- node inspector
- add/duplicate/delete
- validation
- JSON import/export
- runtime preview/load button
- schema/version display

Exit criteria:

- user can move a skill to another grid cell and export it
- game reads changed layout directly
- invalid graph cannot silently ship

**Local user verification checkpoint recommended here because editor ergonomics are subjective.**

---

## Phase 9 — World progression + completion overview

**Goal:** complete world -> summary -> continue -> next world.

Tasks:

- `WorldProgressionService`
- placeholder multi-world sequence
- completion lock/state transition
- overview screen
- reward/unlock application
- asynchronous next-world warmup
- camera transition

Exit criteria:

- all blocks mined triggers completion exactly once
- Continue consistently activates configured next world
- skill/global progress survives world transition

---

## Phase 10 — Save/load + offline automation

**Goal:** persistent incremental-game session without giant saves.

Tasks:

- save schema/versioning
- player/skills/miners state
- sparse modified-chunk serialization
- exhausted region markers
- compression
- autosave triggers
- deterministic load/regeneration
- aggregate offline-mining calculation

Exit criteria:

- untouched terrain is absent from save data
- large mostly-mined world loads correctly
- save size scales with modifications/aggregate markers, not logical world address-space size

---

## Phase 11 — 1000 × 1000 stress/optimization milestone

**Goal:** prove the architecture rather than just asserting scalability.

Tasks:

- dedicated stress profile(s)
- automated orbit/mining benchmark
- memory counters
- generator queue profiling
- chunk/region cache tuning
- LOD tuning
- mesh rebuild coalescing
- GC/allocation profiling
- bulk miner benchmarks
- save/load benchmark

Exit criteria:

- starting a 1000 × 1000 logical profile does not allocate an object/voxel/node for the whole footprint
- camera remains responsive during streaming
- large mining batches are processed hierarchically/batched
- measured performance data is written to `docs/PERFORMANCE_BUDGETS.md`

---

## Phase 12 — Game-feel and reference polish

**Goal:** turn technically correct mining into a satisfying incremental-game presentation while protecting the reference look.

Tasks:

- destruction animation/particles
- representative block fragments
- click/mining feedback
- camera easing
- miner effects
- UI animation
- cloud/environment polish
- skill-tree visual polish
- completion transition polish
- sound hooks

Exit criteria:

- visual comparison against supplied screenshot/video still passes
- repeated mining feels immediate and readable
- high automation remains visually understandable without effect spam

---

## Phase 13 — Final-scale architecture validation

**Goal:** prove that later content can target a 1,000,000 × 1,000,000 logical address space before creating final worlds.

This is a simulation validation, not a request to render/traverse every block.

Tasks:

- create enormous logical test profile
- query arbitrary far coordinates/chunks
- navigate region hierarchy
- bulk-exhaust regions
- exact large block-count accounting
- save/load aggregate exhausted state
- benchmark very high automation rates
- validate numeric limits

Exit criteria:

- profile creation is effectively constant/small in memory
- arbitrary areas generate deterministically on demand
- entire distant regions can transition to exhausted state without visiting every voxel
- exact remaining/mined counts remain valid
- save state remains practical

---

## Phase 14 — Tool-class automations and world events (proposed)

**Goal:** move automation from one generic drill toward a small roster of specialised tools, and give
the late-game cube two reasons to keep exploring it after the surface is gone.

None of this is required for the first playable milestone; it is recorded here so the systems built
in Phases 6/7 stay general enough to accept it.

### 14a. Tool-class miners

The KayKit "Go Deeper" pack already supplies axe, pickaxe and shovel meshes, so the visual identity
of each tool exists before the mechanics do. Each becomes a placeable, skill-tree-unlocked
automation with a block-affinity bonus rather than a flat rate:

- **Shovel** — fast on dirt/sand/soil, and mines *horizontally* along the surface rather than boring
  straight inward. This is a new mining pattern, not just a rate multiplier.
- **Axe** — fast on blocks carrying a tree feature, and clears the tree with the block.
- **Pickaxe** — fast on stone/dark stone/ore.

Design constraints this implies:

- `MinerDefinition` needs a per-tag rate multiplier, not only `allowed_block_tags`. The existing
  allow-list already keys off block tags, so the affinity table can reuse the same vocabulary.
- `MiningPatternRegistry` needs a surface-tangential pattern for the shovel. Every pattern so far
  works along the outward normal; a tangential walk has to decide what happens when it reaches a
  drop or a water body.
- Tree features are currently render-only decorations resolved by `ProceduralWorldSource.TrySampleTree`
  and are not part of the voxel grid. The axe bonus needs trees to be *queryable* per voxel, which
  the deterministic sampler already supports, but "clearing" one has to be recorded in world state.

### 14b. Block bombs

Unstable blocks spawned deterministically in and around the cube's core. Mining one a few times
detonates it and clears a large radius in one operation.

Design constraints:

- detonation must clear a region through `MiningService` in bulk, using the same exact 64-bit
  accounting as normal mining — it cannot bypass the counters or the remaining-block total
- placement must be deterministic from the world seed so a bomb survives save/load and offline
  progress without being stored per-instance
- the cleared radius interacts with `WorldView.MarkDirtyAround`, which currently dirties a single
  voxel's neighbourhood; a blast needs a region-level dirty mark

### 14c. Gem mines

Deterministically placed high-value pockets inside the core that pay out extra resources. The
exported `Gem1`/`Gem2`/`Gem3` meshes from the same pack cover the visual.

Design constraints:

- these are ordinary blocks with a high `base_value`, so the existing content pipeline covers them;
  what is new is a *placement rule* that clusters them rather than the per-voxel ore noise used today
- they should be rare enough that finding one is an event, which makes them a natural reward for the
  bomb and deep-drill paths above

---

## 23. Acceptance criteria for the first playable milestone

The first playable milestone should not be considered complete until all of the following are true:

1. Game launches from a clean checkout with documented steps.
2. Center world visually resembles the supplied cube-planet reference.
3. Player can smoothly orbit and zoom around it.
4. Hovering visibly highlights the correct exposed block.
5. Base player mining removes one block per click.
6. Mining exposes deeper blocks and updates only affected render chunks.
7. Resources/block counters increment exactly.
8. One automated straight-line miner can be placed and operates through MiningService.
9. Skill tree can be opened at any time.
10. Placeholder upgrades demonstrably modify manual mining and automation.
11. Skill tree is loaded from exported editable data.
12. Completing a test world displays the overview screen.
13. Continue loads the next configured world.
14. Save/load restores player skills, world modifications, and miners.
15. 1000 × 1000 logical test profile does not require pre-creating all blocks.
16. Debug metrics make generation/render/mining bottlenecks observable.

---

## 24. Important configurable decisions that are intentionally not blockers

These can be tuned later without changing architecture:

- final game title
- exact world progression sequence
- whether a progression label such as 10 × 10 describes surface footprint, cube dimensions, or a curated target-count stage
- final chunk size
- exact biome list
- final resource rarity
- final mining hardness rules
- final skill-tree graph and balance
- final miner costs/rates
- whether skill-tree overlay pauses simulation
- exact input buttons for orbit/pan
- renderer/post-processing choice after visual/performance tests

The architecture should expose these decisions as content/settings wherever practical.

---

## 25. Decisions that should *not* be postponed

These are structural and should be enforced from Phase 1 onward:

- no Node3D per logical block
- no collider per logical block
- no full-world voxel dictionary as authoritative storage
- deterministic untouched terrain
- chunk + region hierarchy
- 64-bit exact block counters
- one authoritative MiningService
- data-driven skill definitions/effects
- stable content IDs in saves
- supplied block assets routed through a registry rather than hard-coded scene paths
- worker/main-thread separation for scalable generation
- render work budgeted across frames

---

## 26. Recommended next action after plan approval

Start **Phase 1 and Phase 2 together as the first implementation branch** from the newest `main`: establish the clean C# project/asset registry, then immediately build the reference visual slice before investing in the large procedural/mining backend.

This order reduces the largest product risk early: building technically scalable systems around a world presentation that does not match the intended game.
