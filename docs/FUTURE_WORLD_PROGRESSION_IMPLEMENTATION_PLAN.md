# Future World Progression — Detailed Implementation Plan

Status: **planning / agent handoff document only**

Branch: `agent/future-world-progression-plan`

Base: `agent/one-million-squared-plan`

This plan turns the earlier progression outline plus the 30 answered design questions into an implementation sequence. It deliberately does **not** implement the new progression on this branch. The current gameplay branch can continue receiving local fixes independently.

---

## 1. Product direction locked by this plan

The game should teach one or two ideas at a time on very small deterministic cube worlds, then transition into increasingly expressive authored procedural worlds.

Planned shipped progression:

1. **1 x 1 x 1** — absolute interaction tutorial.
2. **5 x 5 x 5** — dirt/manual-mining tutorial.
3. **10 x 10 x 10** — lake + stone-core / Powered Shovel tutorial.
4. **15 x 15 x 15** — water + stone + trees + first red gem / Drill tutorial.
5. **20 x 20 x 20** — first full generated world; first real post-tutorial economy; Forest Cutter + Rock Breaker.
6. **40 x 40 x 40** — advanced upgrades + active cloud/lightning and meteor gameplay.
7. **50 x 50 x 50** — Steam demo finale; player can reach the end of the complete demo skill tree; clearing every block ends the demo.
8. **100 x 100 x 100** — current full-release destination, approximately one million logical blocks. Extra worlds may later be inserted between 50 and 100.

Do not plan progression around a fixed playtime. The desired experience is at least several hours, but pacing should be tuned from playtests rather than reverse-engineered from an arbitrary time budget.

The 1/5/10/15 worlds are tutorial worlds. The 20 world is the first “main” world.

All shipped worlds are deterministic and preselected by the developer. Runtime must never silently reroll a shipped world. A player-facing infinite/random cube generator remains far-future scope.

---

## 2. Core persistence rules

### 2.1 Player-bound state

The following belongs to the player and survives world changes:

- skill unlocks/ranks;
- automation-class unlocks;
- permanent tool transformations;
- permanent manual-mining upgrades;
- special-resource inventory unless consumed by an upgrade;
- world-unlock/completion state;
- settings and tutorial acknowledgements.

A permanent unlock is not a permanent physical automation instance.

### 2.2 World-bound state

Every world maintains its own persistent run state:

- mined voxel state;
- automation instances placed in that world;
- automation position/route/stop state;
- world-specific authored event state where required;
- replay event stream for that world’s current run;
- tutorial-local currency for tutorial worlds;
- per-world statistics.

If the player bought 20 Powered Shovels in World A, World B starts with zero physical Shovels. The class stays unlocked, but every instance in World B must be bought again for its fixed unit price.

There is **no increasing price per duplicate automation**. Each automation class has a fixed per-instance ordinary-resource price.

### 2.3 Currency scopes

Use two explicit ordinary-currency scopes rather than hidden special cases:

`TutorialLocalWallet`

- Used in 1/5/10/15 tutorial worlds.
- Persisted inside that world’s save slot so revisiting resumes correctly.
- Does not transfer to the next tutorial.
- Does not transfer into the 20 world.

`PersistentMainWallet`

- Begins with the 20 x 20 x 20 world.
- Player-bound.
- Carries through 20 -> 40 -> 50 and later full-release worlds.
- Revisiting a main world uses the player’s current persistent wallet.

Do not overload the current single `GameSaveData.Currency` field to mean both scopes. Make the distinction explicit in save data and runtime services.

### 2.4 Special resources

The first special resource is an existing **red gem**.

Special resources are consumable currencies/tokens used for selective transformational upgrades. They are not a new progression screen separate from the skill tree.

Example:

- player discovers/mines one red gem in the 15 world;
- the Wide Bore transformation skill requires ordinary resources + one red gem;
- purchasing it consumes the red gem once;
- Wide Bore becomes permanently unlocked for the player;
- future Wide Bore Drill placements cost ordinary resources only.

The transformation replaces the old basic Drill class for the player rather than requiring a red gem for every future copy.

The special-cost framework must be generic even if the first use is only `gem_red`.

---

## 3. Revisit and replay are separate features

The main menu/world-select flow must distinguish:

### Continue

Resume the most recently active world/run.

### Revisit

Open any previously unlocked world using its **actual persistent saved state**.

Example: the player partially revisits World 2, leaves it again, plays World 4, then later revisits World 2. World 2 resumes from the partial state left during that revisit.

### Replay

Read-only timelapse of the recorded run. Replay does not alter the world save, currency, skills, statistics or replay file.

The viewer may freely orbit/pan/zoom the camera but cannot mine, buy skills, move machines, trigger events, or otherwise interfere.

For v1, require a completed run before exposing `Replay`. An optional “watch run so far” can be added later without changing the file format.

---

# 4. Replay architecture

## 4.1 Research conclusion

Do **not** store a video and do **not** store a full copy of world state every frame.

Deterministic strategy games traditionally make compact replays by storing initial deterministic state plus an ordered action/input stream. StarCraft II’s public protocol documentation states that the game simulation is deterministic and replays effectively contain user input, while Age of Empires’ recorded-game approach similarly relies on deterministic synchronous simulation. Photon Quantum’s replay documentation also stores deterministic configuration plus input history. These systems demonstrate why an event/command stream is dramatically smaller than frame-by-frame state or video.

However, this game has an even narrower replay requirement: the replay viewer only needs to reproduce **how the cube was progressively mined**. It does not need to reproduce the exact camera, menu actions, cursor motion, skill purchases or machine animations that caused each removal.

Therefore the best source of truth is not raw player input. Record the **authoritative successful world mutations emitted by `MiningService`**.

This has major advantages:

- replay survives changes to automation AI/pathfinding better than command-only replay;
- playback does not need to simulate the skill tree, economy or automation decisions;
- all mining sources already converge through authoritative mining accounting;
- manual, automation, explosion, lightning and meteor mining can use one recorder;
- storage remains small because only actual removals are recorded;
- camera history is completely omitted.

The replay is deterministic because the baseline world is frozen and the mutation sequence is ordered.

Research references:

- Blizzard StarCraft II protocol, deterministic replay notes: https://github.com/Blizzard/s2client-proto/blob/master/docs/protocol.md
- Photon Quantum deterministic inputs/replay: https://doc.photonengine.com/quantum/v3/manual/input and https://doc.photonengine.com/quantum/v2/manual/custom-server-plugin/snippets
- Age of Empires deterministic recorded-game engineering discussion: https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond
- Microsoft Event Sourcing pattern and snapshot guidance: https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing
- Protocol Buffers varint/ZigZag wire-format concepts: https://protobuf.dev/programming-guides/encoding/
- Zstandard small-data/dictionary compression background: https://facebook.github.io/zstd/

## 4.2 Replay source hook

Create a dedicated `ReplayRecorder` subscribing below the gameplay decision layer, ideally at the same authoritative point used by save/accounting:

- `MiningService.BlockMined`
- `MiningService.BulkMined`
- future authoritative batch-removal event used by lightning/meteor/explosions.

Only append an event **after** the mutation is accepted.

Do not record failed clicks, hover changes, selection changes, camera actions, UI actions, skill purchases, automation motion, or particle effects.

## 4.3 Replay event semantics

Minimum logical event types:

`RemoveVoxel`

- time delta / simulation tick delta;
- voxel address;
- optional 2-bit source class (`manual`, `automation`, `world_event`, `other`) if useful for replay effects/statistics.

`RemoveVoxelBatch`

- time delta;
- batch ID/type;
- compact list/range of voxel addresses removed by the same authoritative operation.

`EndRun`

- total duration;
- final event count;
- checksum/hash.

Do not record block ID for ordinary removals if it can be deterministically reconstructed from the frozen world baseline. If an authored runtime event can transform a block before removing it in the future, add an explicit transform event then rather than bloating every v1 removal.

## 4.4 Compact coordinate encoding

Do not serialize three 32-bit coordinates per block.

For each frozen finite world, map every valid voxel address to a deterministic integer linear index. The index can be:

`index = localX + width * (localY + height * localZ)`

Use world-local non-negative coordinates after applying the world’s coordinate origin offset.

Encode successive index **deltas** with ZigZag + base-128 varints. Nearby removals then commonly cost one or two bytes rather than 12 bytes of raw XYZ data. Protobuf uses the same family of varint and ZigZag techniques for compact small/signed integers.

For batch removals, sort only when order is visually irrelevant. Preserve authoritative order when order matters to the visual progression.

## 4.5 Time encoding

The replay does not require millisecond-perfect input reproduction.

Use a fixed replay clock, initially **20 ticks/sec (50 ms)** or **10 ticks/sec (100 ms)**, selected after visual testing. Record tick deltas as unsigned varints.

Multiple mining operations in the same replay tick use `delta = 0` after the first event.

This is smaller and more deterministic than storing floating-point wall-clock timestamps.

## 4.6 Binary file format

Create a versioned binary `.cubeminedreplay` or `.cmbreplay` format rather than JSON.

Header should contain at minimum:

- magic bytes;
- replay schema version;
- world ID;
- frozen world version;
- generator version;
- canonical world-definition/content hash;
- replay tick rate;
- event-stream uncompressed length;
- event count;
- final mined count;
- optional completion timestamp;
- checksum of decompressed event stream.

Event stream follows the header and is compressed.

Do not put human-readable duplicated block names in the event stream.

## 4.7 Compression decision

Build the replay serializer behind `IReplayCompressionCodec` and benchmark real completed-world logs before locking the codec.

Candidates:

1. **Brotli** via .NET built-ins — no new native dependency, good general-purpose compression.
2. **Deflate** — simplest compatibility fallback.
3. **Zstandard** — excellent speed/ratio and especially good small-data dictionary support, but adds dependency/deployment work.

Zstandard’s official documentation specifically notes that trained dictionaries improve compression on small correlated records. If replay files remain larger than desired, train a dictionary from representative replay event streams and compare against Brotli.

Do not add a native compression dependency merely because a benchmark says it is 5–10% smaller. Measure shipping complexity against real file-size savings.

### Expected order of magnitude

A 50³ cube has at most 125,000 logical block addresses. A delta-varint event log should be in the hundreds-of-kilobytes range before general compression, not tens/hundreds of megabytes. The exact target must be set from generated benchmark logs rather than guessed.

Add a CI/performance test that generates representative synthetic mining paths and prints:

- raw event bytes;
- varint-packed bytes;
- compressed bytes;
- bytes/mined-block;
- encode/decode throughput.

## 4.8 Replay compatibility

StarCraft II’s replay documentation illustrates a core problem: deterministic replays can become version-sensitive when the simulation/data changes. Avoid that by freezing shipped world definitions.

Every shipped world gets:

- immutable `world_id`;
- incrementing `world_version`;
- immutable generator version reference;
- canonical hash of profile + override data.

A replay points to that exact version/hash.

Never modify a frozen world definition in place after release. Author a new version. Keep old frozen definitions available as long as old saves/replays are supported.

Because our replay records final mining mutations rather than raw automation commands, replay compatibility is substantially less fragile than a full deterministic-simulation replay.

## 4.9 Replay playback

`ReplayPlayer` loads:

1. frozen baseline world;
2. replay header;
3. decompressed event stream;
4. empty replay-only mined-state store.

Playback applies removals according to the replay clock and tells `WorldView` to update presentation.

Controls:

- play/pause;
- restart;
- 1x / 2x / 4x / 8x / 16x / 32x speed;
- free camera orbit/pan/zoom;
- optionally “fit whole cube”.

At high speed, batch multiple replay ticks into one render frame and coalesce chunk rebuilds. The replay must not force one mesh rebuild per historical block.

For v1, forward playback and restart are sufficient. Do **not** persist full world snapshots merely to support arbitrary reverse scrubbing.

If scrub/seek is added later, build periodic **in-memory** checkpoints while opening the replay or cache compressed mined-bitsets at coarse intervals. Event-sourcing guidance recommends snapshots as a performance optimization when long streams become expensive to replay; they should not replace the event stream as source of truth.

---

# 5. Save-model refactor required before new progression

The current save file has one global currency, global skill ranks, and per-world mined/miner state. Refactor before adding tutorial worlds.

Suggested logical model:

`PlayerSaveData`

- `schema_version`
- `player_progression_version`
- `persistent_main_currency`
- `skill_ranks`
- `permanent_unlocks` if not derivable from skills
- `special_resources: Dictionary<string,long>`
- `highest_unlocked_world_id`
- `completed_world_ids`
- `active_world_id`
- `worlds: Dictionary<string, WorldRunSaveData>`

`WorldRunSaveData`

- `world_id`
- `world_version`
- `tutorial_local_currency`
- `manual_blocks_mined`
- `automated_blocks_mined`
- mined sparse state
- exhausted regions if relevant
- automation snapshots
- world-event state
- replay file reference / replay stream metadata
- completion flag
- first-started timestamp / completion timestamp

Do not serialize replay bytes inside the JSON save. Store replay files separately under a versioned replay directory and refer to them by ID/path.

Use atomic save replacement as the current save system already does.

Add migration from the existing schema rather than silently resetting development saves.

---

# 6. Automation ownership/economy refactor

The current implementation partially couples the first automation unlock with buy-and-place. The future progression needs a clean separation:

### Capability unlock

Player-bound and permanent.

Examples:

- Powered Shovel unlocked;
- Drill unlocked;
- Forest Cutter unlocked;
- Rock Breaker unlocked;
- Wide Bore transformation unlocked.

### Unit purchase

World-bound physical instance.

- costs ordinary resources;
- fixed price per automation class;
- same price for the first and twentieth copy;
- transactional placement remains: preview first, spend only after valid accepted placement;
- cancelling placement spends nothing;
- unit remains in that world’s save only.

After a permanent transformation, the shop entry should present the transformed class/model/behavior as the normal version available to the player.

Do not make the player rebuy an old basic Drill after Wide Bore has permanently replaced it unless a future design explicitly adds a downgrade selector.

---

# 7. Manual mining redesign

## 7.1 One click = one damage application

Manual mining remains immediate interaction.

One direct manual click applies one unit of manual damage to eligible blocks. This prepares the architecture for blocks requiring multiple hits without turning manual mining into a hold-to-charge system.

Block hardness/damage state should be generalized so future three-hit rocks or special blocks can coexist with one-hit dirt.

## 7.2 Hover mining

Unlock in the 5³ world.

Behavior:

- player has a visible UI toggle: `Hover Mining ON/OFF`;
- when ON, simply hovering a mineable block repeatedly executes manual mining actions;
- no mouse button is required;
- action frequency comes from the player’s manual mining cadence;
- moving the cursor immediately moves the mining footprint; it does not finish the previous target first;
- normal click mining remains available.

Hover mining must be rate-limited by a deterministic/manual timer. Never bind one mine action to every rendered frame.

## 7.3 Faster manual mining

Because direct click mining remains one immediate damage action, “speed” primarily increases the cadence of automatic/hover manual actions and any future repeated-manual mode.

Represent this explicitly as `ManualMiningActionsPerSecond` or `ManualActionInterval`, not as an ambiguous animation speed.

Direct clicks may still use a very small anti-double-fire guard, but should feel immediate.

## 7.4 Mining footprint progression

Planned stages:

1. single block;
2. 3x3 **plus** footprint (center + four cardinal cells);
3. full 3x3 square;
4. later 10x10 square.

Represent footprints as data/strategy objects rather than `if rank == X` branches.

Suggested IDs:

- `single`
- `plus_3`
- `square_3`
- `square_10`

The 10x10 even-sized footprint must use a documented deterministic anchor convention. Suggested: tangent offsets `[-4,+5]` along each local tangent axis, with a translucent preview showing the exact cells before the action occurs.

## 7.5 Surface-layer priority algorithm

This is a critical rule.

The footprint does **not** blindly damage every exposed block around the cursor.

For the hovered cube face:

1. derive the face outward normal and two tangent axes;
2. project the selected footprint into tangent-plane columns;
3. for every footprint column, find the first present mineable voxel along the outward-to-inward direction;
4. compute that candidate voxel’s outward layer/depth coordinate;
5. find the **highest/front-most layer** represented anywhere in the footprint;
6. only candidates on that highest layer receive one damage application this manual tick;
7. lower layers remain untouched until every surviving obstruction above them is removed;
8. on the next tick, recompute the layer.

Example:

- flat grass plane in a 3x3 footprint;
- one rock sits one voxel above the center grass and needs three hits;
- only the rock is on the highest layer;
- three manual ticks damage/break that rock;
- surrounding grass is untouched during those three ticks;
- after the rock disappears, all nine grass cells become the highest equal layer and can receive the next area hit together.

This same rule applies to click mining and hover mining.

Clip a footprint to the current dominant cube face; do not wrap a single area-mining action around a cube corner unless a later upgrade explicitly allows it.

## 7.6 Footprint preview

When an area-mining upgrade is active, hovering should communicate the effective footprint and priority layer before mining:

- subtle outline on candidate cells;
- highest active layer stronger/brighter;
- cells blocked behind a higher layer de-emphasized;
- do not flood the screen with opaque fills.

This will make the priority rule understandable without tutorial text alone.

---

# 8. Skill-tree changes

The existing data-driven skill system should remain the foundation, but add capabilities needed by progression.

New/changed effect concepts:

- `unlock_hover_mining`
- `multiply_manual_mining_rate`
- `set_manual_footprint`
- generic permanent automation transformation effect if `set_drill_pattern` is too Drill-specific long-term
- special-resource costs on skill nodes
- optional world/milestone availability gates distinct from prerequisites

Extend skill costs from one ordinary `cost` to a structured cost model while keeping backward compatibility, e.g.:

```json
"cost": {
  "ordinary": 350,
  "special": {
    "gem_red": 1
  }
}
```

or retain `cost` and add `special_costs` if that minimizes migration.

The standalone skill-tree editor must expose special costs and world/milestone availability if those are data-driven.

## 8.1 5³ choice design

Hover Mining becomes available/unlocked in this world.

Both Faster Manual Mining and the first Larger Mining upgrade are offered. The player should only have enough tutorial-world money to buy **one of the two at that moment**. This is economic choice, not a permanently exclusive branch.

Later income/worlds allow buying the other.

Do not encode mutual exclusion in skill prerequisites.

## 8.2 Demo completion

By the end of the 50³ demo finale, the player must be able to unlock **everything visible in the demo skill tree**. Do not display intentionally unreachable “full game only” nodes in the demo tree unless that product decision changes later.

---

# 9. Deterministic world authoring system

A stronger authoring workflow is required. Seed search alone is not enough for tutorial/world-design requirements such as “exactly one lake”, “stone begins three blocks deep”, or “one red gem near the core”.

Build a standalone **World Authoring Tool** similar in spirit to the skill-tree editor.

Suggested location:

`tools/world_authoring/`

Launch helper:

`world_authoring.bat`

## 9.1 Generation modes

Support three authored baseline modes:

### Blueprint

Explicit deterministic voxel/feature layout. Best for 1³, 5³, and likely 10³ tutorial worlds.

### Procedural

Profile + seed only. Suitable for candidate generation and worlds that need no hand-authored override.

### Hybrid

Profile + seed + sparse authored overrides/features. Preferred for 15³ and larger shipped worlds.

Runtime code should expose one `IWorldSource` interface regardless of mode.

## 9.2 Frozen world manifest

Every shipped world definition should include:

- `world_id`
- `world_version`
- display name / intro text
- dimensions
- generation mode
- generator version
- seed
- terrain/climate parameters
- material palette
- feature parameters
- sparse voxel overrides
- sparse feature overrides
- authored landmarks/special resources
- tutorial/milestone metadata
- canonical content hash

Do not rely on a mutable global generator implementation without versioning.

## 9.3 Sparse authored overrides

Do not bake an entire 50³ world into JSON just because one lake or gem was adjusted manually.

Provide sparse override layers:

- force block type at coordinate;
- force empty at coordinate;
- add/remove feature at support coordinate;
- place special-resource pocket;
- optional authored volume/shape primitive that compiles to sparse overrides on freeze.

The baseline remains reproducible from generator version + seed; overrides encode developer intent.

## 9.4 Editor features

Minimum editor:

- dimension selection;
- generation mode;
- seed entry and randomize candidate seed;
- all terrain/profile parameters;
- orbit/pan/zoom preview using runtime meshes/materials;
- regenerate;
- material-count statistics;
- water coverage;
- surface soft-terrain coverage;
- exposed stone coverage;
- tree count/density;
- ore/gem counts;
- cross-section/slice view;
- toggle features/material categories;
- click voxel inspect showing coordinate/material/generator values;
- paint/replace voxel;
- fill box/sphere/plane;
- carve/force empty;
- add/remove tree;
- place special gem/resource;
- undo/redo;
- validate;
- save draft;
- **Freeze for Shipping**.

## 9.5 Candidate seed browser

For 20³/40³/50³, add an offline candidate workflow:

- generate N seeds;
- calculate cheap summary metrics without instantiating Godot nodes;
- filter against author constraints;
- optionally render fixed-angle thumbnails;
- browse candidate cards;
- open candidate in full editor;
- manually adjust with overrides;
- freeze selected result.

This should reuse the exact runtime procedural source code, never a visually similar editor-only generator.

## 9.6 Freeze operation

“Freeze for Shipping” must:

1. canonicalize profile/override data;
2. assign/increment `world_version`;
3. record generator version;
4. calculate SHA-256 content hash;
5. run structural validation;
6. run material/feature-count validation;
7. optionally produce a preview thumbnail and metrics report;
8. write versioned world manifest;
9. never overwrite an already shipped/frozen version unless explicitly creating a new version.

---

# 10. World-by-world implementation specification

## World 0 — 1 x 1 x 1

Generation: **Blueprint**.

Contents:

- exactly one mineable block;
- no hidden layers;
- no automation;
- no special resources.

Teaching goal:

- camera exists but does not need a special restricted mode;
- LMB mines;
- world completion -> completion panel -> Continue.

UI:

- normal UI may already be visible;
- avoid forcing a “minimal UI” architecture that then has to be dismantled in World 1.

Currency:

- tutorial-local;
- effectively irrelevant here.

Validation:

- exact authoritative mineable count = 1;
- replay recorder must already work so the very first completed world can produce a valid one-event replay.

---

## World 1 — 5 x 5 x 5 dirt/manual tutorial

Generation: **Blueprint** or extremely constrained hybrid.

Contents:

- mostly/all dirt-family blocks;
- no water;
- no trees;
- no stone blockers required.

Teaching goals:

- unlock Hover Mining;
- introduce manual mining cadence;
- present first choice between Faster Manual Mining and Larger Mining;
- available tutorial currency should allow only one choice at that moment.

Manual upgrade content:

- Hover Mining toggle becomes visible when unlocked;
- Faster Mining increases hover cadence;
- Larger Mining first stage should be `plus_3`.

Do not permanently lock the unchosen branch. The player can buy it later.

Currency:

- tutorial-local;
- saved for revisit;
- not transferred to World 2.

Validation:

- exact dimensions 5³;
- enough obtainable ordinary resources to buy Hover Mining if it is not milestone-granted plus exactly one of the offered early upgrades according to final tuning;
- replay records area/hover mining as final block removals, not cursor movement.

---

## World 2 — 10 x 10 x 10 lake + stone core

Generation: **Blueprint/Hybrid**.

Required authored structure:

- one clearly readable lake;
- dirt/sand-family surface;
- stone core begins about three blocks below the surface;
- enough contiguous soft terrain for Powered Shovel to demonstrate movement before it stops;
- water and stone interrupt shovel traversal.

Unlock:

- **Powered Shovel** in this world.

Teaching goal:

- automation class unlock vs per-world unit purchase;
- placement ghost;
- surface routing;
- Shovel stops on unsupported terrain;
- water/stone demonstrate that other tools will be needed.

The game should provide tutorial hook events such as `FirstShovelStoppedByWater` / `FirstShovelStoppedByStone`, but actual tutorial wording/content can be authored later. The machine itself should simply obey normal stop behavior.

Currency:

- tutorial-local;
- physical Shovels remain only in this world.

Validation:

- exactly one authored primary lake unless deliberately changed later;
- at least one route where base Shovel can travel several tiles before a meaningful blocker;
- stone exists ~3 depth layers in;
- no accidental generator feature creates a second tutorial-breaking lake.

---

## World 3 — 15 x 15 x 15 trees + first red gem + Drill

Generation: **Hybrid**.

Contents:

- water;
- stone core;
- trees as surface obstructions;
- one authored red gem near the core;
- enough ordinary stone to teach the Drill clearly.

Unlock:

- **Drill** in this world.

Trees:

- Shovel cannot mine the ground under a tree;
- Forest Cutter is not yet unlocked, so player must manually clear/route around tutorial tree obstacles where necessary;
- this sets up why Forest Cutter matters later.

Special-resource teaching:

- red gem is an actual existing gem resource;
- mining it increments player special inventory;
- Wide Bore skill requires ordinary resources + one red gem;
- purchase consumes the gem;
- Wide Bore becomes a permanent player transformation;
- current placed Drill(s) should update safely to the transformed behavior/model or require a clearly defined re-place if live transformation proves technically unsafe. Preferred behavior: update existing compatible instances, matching the current skill-change architecture.

Currency:

- ordinary currency remains tutorial-local and does not enter World 4;
- permanent skill/transformation unlock does carry.

Validation:

- red gem count/position is deterministic and guaranteed reachable;
- no other random red gem trivializes the teaching beat unless intentionally authored;
- Drill can reach enough normal stone before encountering unsupported material to demonstrate value;
- transformed Wide Bore behavior has room to be visibly useful.

---

## World 4 — 20 x 20 x 20 first real generated world

Generation: **Hybrid procedural**, first world expected to use the full approved terrain language.

This is the start of the main game/economy.

Contents:

- natural earth-like zones;
- dirt/grass/sand;
- coherent lakes/water depth;
- beaches around most shallow water;
- stone/dark stone;
- cliffs/plateaus;
- trees;
- ore;
- several gems/special resources below surface;
- authored adjustments as needed after seed selection.

Unlocks:

- **Forest Cutter**;
- **Rock Breaker**.

Economy:

- start `PersistentMainWallet` here;
- tutorial ordinary currency is discarded/not transferred;
- all main-world earnings now carry forward.

Teaching goal:

- first world where player combines all previously learned mechanics rather than solving one isolated lesson;
- trees make Forest Cutter immediately legible;
- stone/ore/gems make Rock Breaker useful;
- automations are purchased per world despite permanent class unlocks.

This world should become the first major art-direction benchmark against the supplied reference image/video.

---

## World 5 — 40 x 40 x 40 advanced + active world events

Generation: **Hybrid procedural** with stronger authored landmark/event validation.

Contents:

- broader terrain variety;
- rarer gems;
- more reasons to combine upgraded automation classes;
- cloud/lightning active mechanic;
- meteor active mechanic.

### Cloud/lightning v1

Clouds orbit as coherent clumps.

Eligible cloud has a charge meter/state. Repeated player clicks charge it. When fully charged:

- cloud automatically strikes the world point directly beneath it along the local radial/outward direction;
- lightning resolves a bounded crater/removal operation through authoritative `MiningService` batch mining;
- reward/accounting/save/replay all see ordinary authoritative removals;
- strike cannot mine outside world bounds;
- particle/light/audio presentation is separate from simulation.

Later upgrades:

- fewer clicks to charge;
- larger/stronger strike;
- better orbit frequency/coverage;
- **Cloud Generator** automation that creates clouds onto a configured orbit;
- **Cloud Charger** automation that periodically charges passing clouds.

A generator/charger should not require simulating every decorative cloud physics interaction. Use deterministic orbit parameters and logical charge state.

### Meteor v1

Meteor occasionally enters a deterministic temporary orbit/window around the world.

Interaction:

- player can grab it with mouse;
- drag/flick/throw toward the cube;
- release velocity is derived from recent pointer motion in world/screen space;
- a generous assisted trajectory/targeting layer is acceptable so the mechanic feels intentional rather than like unreliable raw physics;
- impact location resolves to cube surface;
- impact invokes bounded crater removal through authoritative mining;
- if not caught/used within its window, meteor exits and despawns.

Keep meteor spawn/event RNG deterministic from a world-event RNG seed/state so save/load cannot reroll an opportunity simply by restarting.

Future upgrades:

- spawn frequency;
- longer capture window;
- stronger impact;
- targeting assist;
- partial/complete automation.

Replay only needs to record resulting authoritative removed voxels. It does not need to reproduce the original mouse flick.

---

## World 6 — 50 x 50 x 50 Steam demo finale

Generation: **Hybrid procedural**, hand-approved/frozen.

Purpose:

- mastery test for everything in the demo;
- nearly/full skill tree available;
- multiple automation classes useful simultaneously;
- active cloud/lightning and meteor accelerators relevant;
- late upgrades can automate or partially automate active systems.

Completion condition:

- **every mineable block is removed**;
- no percentage shortcut;
- clearing the final block triggers Steam demo ending/completion flow.

By the end of this world the player can unlock everything visible in the demo skill tree.

Add an explicit demo-complete screen distinct from ordinary world transition. It may tease the 100³ full-release destination without exposing inaccessible nodes in the demo skill tree.

Replay for this world is the most important storage/performance target: 125,000-address full clear with accelerated automation/events must play back smoothly at high timelapse speeds.

---

## Full release — 100 x 100 x 100

Current end target:

- 1,000,000 logical block addresses;
- uses architecture already prototyped for the one-million world;
- additional 50–100 worlds may be inserted later;
- exact full-release progression is intentionally not locked now.

Do not let work on 100³ delay the demo progression once the 50³ world is stable.

---

# 11. Tutorial/event hook framework

The user will refine tutorial wording later, but systems need clean hooks now.

Create semantic events rather than hard-coded tutorial popups inside mechanics:

- `WorldStarted`
- `FirstManualMine`
- `HoverMiningUnlocked`
- `FirstAreaMine`
- `AutomationClassUnlocked`
- `AutomationPlaced`
- `AutomationStopped`
- `ShovelStoppedByWater`
- `ShovelStoppedByStone`
- `TreeBlockedShovel`
- `SpecialResourceFound`
- `TransformationPurchased`
- `LightningCharged`
- `LightningImpact`
- `MeteorSpawned`
- `MeteorGrabbed`
- `MeteorImpact`
- `WorldCompleted`

A `TutorialDirector` can subscribe and decide whether to display authored guidance based on world/tutorial state.

Mechanics must work correctly even when all tutorial prompts are disabled.

---

# 12. Main menu/world selection UX

Add a world-selection screen once multiple persistent/revisitable worlds exist.

Each unlocked world card should expose state such as:

- world name;
- dimension;
- completion status/percentage;
- last played;
- completed/not completed;
- `REVISIT` or `CONTINUE`;
- `REPLAY` when a completed replay exists.

Global `Continue` resumes active world.

Revisit must not reset the world.

A separate explicit reset/new-run feature, if ever added, must warn that it replaces the world’s persistent run/replay. Do not make “Replay” secretly create a fresh playable run.

---

# 13. Reference visual / post-processing pass

This is a required demo-quality phase, not optional cleanup.

The current game is mechanically much closer than visually. The supplied reference still/video show a much more cohesive image than raw Godot rendering.

## 13.1 Reference characteristics observed

From the supplied still and video frames, the target presentation has:

- deep navy/blue-black background rather than neutral pure black;
- strong readable separation between lit and shadowed cube faces;
- dark contact/crevice shading that gives small voxel forms depth;
- saturated but controlled greens and blues;
- less raw/specular “plastic” response than default materials;
- bright clouds/water highlights that separate from the background without turning the whole image into bloom;
- subtly softened/finished edges compared with raw nearest-render output;
- cohesive cool shadow palette;
- natural terrain composition remains the largest contributor to the image — post-processing must not be used to hide poor generation;
- depth and silhouette remain readable even at whole-world camera distance.

## 13.2 Godot capabilities researched

Godot’s Environment provides tonemapping, glow, fog, SSAO and adjustments. Godot 4.6 Compatibility now has a simplified SSAO implementation, while Forward+ supports fuller SSAO plus SSIL, TAA/FSR2 and more advanced compositor options. Godot documentation recommends MSAA as a strong fit for cartoon/stylized art where avoiding temporal blur is important.

References:

- Environment/post-processing: https://docs.godotengine.org/en/latest/tutorials/3d/environment_and_post_processing.html
- Renderer feature matrix: https://docs.godotengine.org/en/4.6/tutorials/rendering/renderers.html
- 3D antialiasing: https://docs.godotengine.org/en/4.5/tutorials/3d/3d_antialiasing.html
- Custom post-processing: https://docs.godotengine.org/en/4.5/tutorials/shaders/custom_postprocessing.html
- Advanced depth-aware post-processing: https://docs.godotengine.org/en/4.6/tutorials/shaders/advanced_postprocessing.html

## 13.3 Do an A/B renderer evaluation, not an immediate blind switch

Current project lineage uses Compatibility-oriented rendering. Before migrating the entire project, create a controlled reference harness and compare:

### Compatibility candidate

- Godot 4.6 simplified SSAO;
- 4x MSAA candidate;
- Filmic/AgX tone-map comparison;
- subtle glow;
- environment brightness/contrast/saturation;
- custom fullscreen color-grade/vignette/dither if necessary.

### Forward+ candidate

- full SSAO;
- optional subtle SSIL;
- MSAA or TAA comparison;
- same tone-map/grade;
- optional compositor effect only if it materially improves the target look.

Choose based on measured visual improvement and demo hardware cost. Do not switch merely because Forward+ exposes more checkboxes.

## 13.4 Visual reference harness

Create a dedicated debug/art harness with locked camera viewpoints for 20³ and later worlds.

Controls/presets should toggle components independently:

- raw lighting;
- AO only;
- tone map/grade;
- glow;
- final combined;
- shadows quality;
- anti-aliasing mode where runtime-switchable;
- post-process custom pass.

Provide screenshot capture using stable filename metadata:

`worldId_worldVersion_cameraPreset_visualPreset_timestamp.png`

This makes iterations compare like-for-like instead of relying on memory.

## 13.5 Proposed staged look pass

### Stage A — material response

- keep terrain matte;
- eliminate accidental metallic/specular shine;
- tune water separately rather than applying terrain matte rules blindly;
- ensure grass/dirt mesh/material orientation remains correct.

### Stage B — lighting

- one primary directional/key light defining cube faces;
- restrained cool ambient/fill;
- contact/soft shadow tuning;
- avoid multiple strong omni lights flattening the scene.

### Stage C — ambient occlusion

AO is likely one of the biggest gains for the reference because the target has dark voxel creases and vegetation/terrain contact depth.

Tune radius relative to block spacing. Avoid excessive haloing.

### Stage D — tone/color

Compare Filmic, AgX and existing tone mapping using fixed screenshots.

Target:

- preserve saturated green vegetation;
- deepen cool shadows without crushing all inner detail;
- prevent clouds from clipping to featureless white;
- preserve distinct shallow/deep water blues.

### Stage E — glow

Use subtle glow mainly for legitimately bright clouds/water/event effects. Avoid generic bloom over green terrain.

### Stage F — custom finishing pass only if needed

Potential small full-screen operations:

- restrained vignette;
- subtle shadow tint/grade;
- very subtle dither/grain to reduce sterile digital gradients;
- optional sharpening/softening only after A/B comparison.

Do **not** add chromatic aberration, heavy film grain, large bloom or strong depth-of-field unless direct reference comparison demonstrates it.

### Stage G — final generation/material iteration

Post-processing cannot create the reference’s beaches, lakes, forests, plateaus and natural terrain zoning. Keep a separate art-generation checklist and fix geometry/material distribution where the difference is structural.

## 13.6 Visual quality settings

Expose at least:

- Post-processing Low/High or individual toggles;
- AO quality;
- glow toggle/quality if needed;
- MSAA setting;
- 3D resolution scale.

UI should remain full resolution while 3D scaling changes.

Lock a demo default only after profiling the 50³ world on representative hardware.

---

# 14. Weather and meteor simulation architecture

Do not build these as unconstrained physics toys.

## 14.1 Deterministic world-event service

Create `WorldEventService` with:

- per-world deterministic RNG state;
- logical event schedule;
- active cloud charge states;
- meteor spawn/orbit state;
- save snapshot;
- semantic event emissions.

Visual nodes observe logical state.

Save/load must restore an active meteor/cloud opportunity without rerolling it.

## 14.2 Mining integration

Lightning/meteor crater operations should use a shared bulk-removal primitive that:

- determines affected voxel set;
- checks bounds/protection rules;
- removes through `MiningService` authority;
- awards resources according to explicit event policy;
- sends one grouped replay batch;
- marks rendering dirty in coalesced chunks;
- creates presentation separately.

Avoid calling full expensive chunk rebuild once per crater voxel.

---

# 15. World-generation art rules to preserve/extend

The full worlds should continue moving toward Minecraft-esque coherent terrain rules rather than per-block noise.

Keep generation layered:

1. macro landform/continentalness;
2. erosion/ridge/plateau shaping;
3. climate/humidity/forest fields;
4. surface material rules;
5. water basin/sea-level logic;
6. beach/sand adjacency logic;
7. subsurface stone/dark stone;
8. ore/gem pockets;
9. deterministic feature pass (trees/rocks/landmarks);
10. authored overrides.

Water art rules:

- most ordinary shore water should transition through sand/beach where appropriate;
- shallow water uses lighter material/tint;
- deeper water becomes darker;
- lake geometry should read as actual basin depth, not blue blocks painted onto a random surface.

Tree/feature rules:

- features are support-owned;
- mining support removes/invalidates feature consistently;
- Shovel cannot remove a support tile under a blocking feature;
- future rocks/props join the same feature-ownership policy.

---

# 16. Implementation phases

The implementation should be staged so each milestone is independently testable.

## Phase A — save/economy/player-state refactor

Deliverables:

- explicit player-bound vs world-bound state;
- tutorial-local wallet vs main persistent wallet;
- special-resource inventory;
- per-world physical automation inventory/state;
- save migration;
- revisit-safe world slots.

Do this first because every later tutorial/world depends on correct ownership semantics.

Local checkpoint:

- switching between two existing worlds preserves each world’s mined/miner state;
- skill unlocks persist globally;
- physical automations do not appear in another world;
- tutorial-local currency does not leak;
- main currency does carry.

## Phase B — replay recorder + file format

Deliverables:

- authoritative mutation recorder;
- binary versioned event codec;
- delta/varint packing;
- compression abstraction;
- replay file lifecycle;
- replay checksum/version validation;
- synthetic compression benchmark.

Do not build fancy replay UI yet.

## Phase C — world authoring foundation

Deliverables:

- versioned world manifest;
- blueprint/procedural/hybrid sources;
- sparse overrides;
- generator version registry;
- content hash/freeze rules;
- runtime loads frozen versions.

## Phase D — world authoring tool MVP

Deliverables:

- preview;
- seed/profile controls;
- voxel inspect/paint/carve;
- feature/gem placement;
- metrics;
- undo/redo;
- validate/freeze.

This tool becomes the path for authoring all remaining worlds, rather than hand-editing JSON whenever possible.

## Phase E — manual mining architecture

Deliverables:

- generalized damage application;
- hover mining toggle/cadence;
- footprint strategy/data;
- `single`, `plus_3`, `square_3`;
- highest-layer priority solver;
- footprint preview;
- skill effects/editor support.

Do not implement 10x10 until the smaller footprints are validated.

## Phase F — author Worlds 0 and 1

Deliverables:

- frozen 1³ world;
- frozen 5³ world;
- progression entries;
- Hover Mining introduction;
- economic choice between speed/plus footprint;
- replay recording from the first minute of the game.

Local checkpoint strongly recommended here because this defines the basic manual feel.

## Phase G — automation ownership/shop refactor

Deliverables:

- capability unlock separate from unit purchase;
- fixed per-unit world-local prices;
- transactional ghost placement retained;
- transformed class replacement semantics;
- save migration for existing miners.

## Phase H — author World 2 (10³)

Deliverables:

- exact lake/core world;
- Powered Shovel progression hook;
- tutorial semantic stop events;
- water/stone blocking validation.

## Phase I — special-resource framework + author World 3 (15³)

Deliverables:

- generic special resource inventory/costs;
- red gem acquisition;
- Wide Bore transformation cost;
- Drill unlock;
- tree blocking/tutorial hooks;
- frozen 15³ world.

## Phase J — author World 4 (20³) + first main economy

Deliverables:

- first full hybrid-generated frozen world;
- persistent main currency begins;
- Forest Cutter and Rock Breaker progression;
- candidate seed workflow exercised for real;
- multiple special resources;
- full-system progression checkpoint.

## Phase K — reference visual/post-processing pass

Do this **before** building 40³/50³ final demo content so new worlds are authored against the actual shipping look.

Deliverables:

- reference harness;
- renderer A/B evaluation;
- lighting/AO/tonemap/glow/material pass;
- optional custom finishing shader;
- quality settings;
- fixed-camera screenshot comparison workflow.

Do not postpone this to the day before Steam screenshots.

## Phase L — replay viewer + main menu Revisit/Replay

Deliverables:

- world-selection screen;
- Continue/Revisit/Replay separation;
- read-only replay mode;
- free camera;
- accelerated playback;
- rebuild batching/coalescing;
- completed-run replay availability.

## Phase M — WorldEventService + lightning

Deliverables:

- deterministic event RNG/save state;
- clickable/chargeable clouds;
- strike-under-cloud behavior;
- authoritative crater batch;
- replay integration;
- presentation.

## Phase N — meteor interaction

Deliverables:

- deterministic spawn/orbit window;
- grab/flick interaction;
- assisted impact solution;
- authoritative crater;
- replay integration;
- save/resume active event.

Local checkpoint required: this is physical input/game-feel work.

## Phase O — author World 5 (40³)

Deliverables:

- frozen advanced world;
- rare gems;
- lightning/meteor introduction;
- advanced skill availability;
- first cloud generator/charger progression if in demo tree.

## Phase P — author World 6 (50³) Steam demo finale

Deliverables:

- frozen 50³ world;
- complete visible demo skill tree obtainable;
- all automation/event systems useful;
- full-clear demo ending;
- replay stress/performance pass on 125k-block clear;
- save/revisit/replay full regression.

## Phase Q — demo pacing/art/polish

Deliverables:

- resource costs tuned from playtests;
- automation prices tuned;
- special-resource placement tuned;
- tutorial wording/flow integrated;
- visual reference pass revisited with final 50³ content;
- accessibility/quality settings;
- Steam demo end-state polish.

Do not balance by making worlds arbitrarily larger. Increase decision density and system interplay.

## Phase R — full-release continuation later

- additional intermediate worlds if desired;
- 100³ final target;
- further transformations/systems;
- revisit one-million performance only when product progression reaches it.

---

# 17. Proposed file/class map

Names are recommendations, not hard requirements.

### Progression/save

- `src/Save/PlayerSaveData.cs`
- `src/Save/WorldRunSaveData.cs`
- `src/Save/SaveMigration.cs`
- `src/Progression/WorldUnlockService.cs`
- `src/Economy/CurrencyService.cs`
- `src/Economy/SpecialResourceInventory.cs`

### Replay

- `src/Replay/ReplayRecorder.cs`
- `src/Replay/ReplayHeader.cs`
- `src/Replay/ReplayEvent.cs`
- `src/Replay/ReplayBinaryWriter.cs`
- `src/Replay/ReplayBinaryReader.cs`
- `src/Replay/ReplayCompression.cs`
- `src/Replay/ReplayPlayer.cs`
- `src/UI/ReplayControlsView.cs`

### Manual mining

- `src/Mining/ManualMiningCadence.cs`
- `src/Mining/ManualMiningFootprint.cs`
- `src/Mining/ManualSurfaceLayerResolver.cs`
- `src/UI/HoverMiningToggle.cs`

### World authoring

- `src/World/Authoring/FrozenWorldManifest.cs`
- `src/World/Authoring/WorldOverrideSet.cs`
- `src/World/Generation/GeneratorVersionRegistry.cs`
- `tools/world_authoring/...`

### World events

- `src/WorldEvents/WorldEventService.cs`
- `src/WorldEvents/LightningEvent.cs`
- `src/WorldEvents/MeteorEvent.cs`
- `src/WorldEvents/WorldEventSnapshot.cs`

### Visual pass

- `src/Presentation/ReferenceLookController.cs`
- `src/Presentation/ReferenceLookHarness.cs`
- `shaders/reference_finish.gdshader` only if the built-in Environment pass is insufficient.

---

# 18. CI/content validation additions

Extend `tools/validate_content.py` or split into focused validators.

Validate:

- progression world IDs exist and are ordered;
- world dimensions exactly match authored progression contract;
- frozen world version/hash fields exist;
- no shipped world silently references an unregistered generator version;
- tutorial wallet scope vs main wallet scope is explicit;
- required tutorial features exist (lake/core/tree/red gem) from manifest metrics/overrides;
- every automation class has fixed instance price;
- permanent transformations have valid special costs;
- all demo skill nodes are reachable by/before 50³;
- replay schema fixtures round-trip;
- replay decoder rejects bad checksum/version/hash;
- replay codec benchmark remains below a chosen bytes/block threshold after empirical baseline is established;
- 50³ full clear is exactly 125,000 logical addresses if using a literal full 50³ cube with no excluded non-mineable cells; if water/non-mineable representation changes this, distinguish logical address count from authoritative mineable count explicitly;
- 100³ target remains exactly 1,000,000 logical addresses.

Add deterministic generator tests: same manifest/version/hash must return same sample/material/feature results.

---

# 19. Performance rules for the demo worlds

The 20/40/50 worlds should not inherit giant-world complexity unnecessarily.

For 50³, 125,000 logical addresses is small enough to use much simpler exact data structures than 100³ if they are faster and easier to reason about.

Rules:

- simulation state and rendering representation remain separate;
- automation off-screen should remain computational where practical;
- dirty chunk updates are coalesced;
- lightning/meteor batches mark chunks, not one rebuild per voxel;
- replay high-speed playback must batch historical mutations;
- particles are capped during high-rate automation/replay;
- world authoring tool can precompute metrics offline; runtime should not recompute authoring-only analysis.

Do not prematurely optimize the demo around the million-block renderer if a simpler path is better for 50³.

---

# 20. Migration strategy from the current branch

When implementation begins, first merge/rebase the latest locally fixed gameplay branch into the new progression implementation branch. Do **not** assume this planning branch contains future local fixes made after it was forked.

Migration steps:

1. bring in final Phase 12–14 local fixes;
2. freeze a known-good baseline commit;
3. add save schema v2 with migration from current schema v1;
4. preserve existing skills/miners where possible;
5. only then replace provisional Verdant/Lakebound/Copper progression with tutorial sequence;
6. retain old worlds as development/reference fixtures until new progression is verified;
7. remove/retire obsolete provisional progression only after demo worlds supersede it.

---

# 21. Explicit non-goals for this implementation plan

Not part of the demo progression pass unless separately approved:

- player-generated infinite/random cube mode;
- multiplayer;
- online replay sharing/workshop integration;
- reverse-time replay simulation;
- exact camera/input reproduction in replays;
- every automation having a rare transformed form;
- forcing a fixed three-hour timing target;
- final full-release world sequence between 50 and 100;
- deep re-optimization of the current million-world manual mining path before the demo progression needs it.

---

# 22. Local verification checkpoints

Keep local checks sparse but meaningful.

### Checkpoint 1 — save/economy ownership

Verify global unlocks, world-local machines, tutorial wallet isolation, main wallet carry, revisit.

### Checkpoint 2 — manual mining/tutorial feel

Verify Hover Mining toggle, cadence, plus footprint, layer priority, 1³/5³ experience.

### Checkpoint 3 — world authoring tool

Verify paint/carve/feature placement, deterministic regenerate, freeze/hash, runtime exact match.

### Checkpoint 4 — 10³/15³ progression

Verify Shovel blockers, Drill unlock, red gem, consumed transformation, permanent Wide Bore.

### Checkpoint 5 — 20³ visual benchmark

Verify first main-world generation and complete reference-look/post-processing A/B pass.

### Checkpoint 6 — replay

Complete a world, revisit it separately, then replay the completed run with free camera and accelerated playback. Check file size.

### Checkpoint 7 — lightning/meteor

Physical/active gameplay must be checked locally for feel.

### Checkpoint 8 — 50³ full demo regression

Full progression/save/revisit/replay/end-screen/performance/art check before demo release.

---

# 23. Definition of done for the future progression pass

The Steam-demo progression pass is complete when:

- all 1/5/10/15/20/40/50 worlds are frozen deterministic authored versions;
- player-bound unlocks survive world transitions;
- physical automations are repurchased per world at fixed unit prices;
- tutorial currency stays local and main currency carries from 20 onward;
- revisit resumes actual per-world save state;
- completed worlds have compact deterministic mining timelapses;
- manual hover mining and footprint/layer-priority upgrades work as specified;
- 10³ teaches Shovel limitations;
- 15³ introduces Drill/red gem/Wide Bore transformation;
- 20³ introduces the complete normal terrain/tool ecosystem;
- 40³ introduces lightning and meteor active systems;
- 50³ can unlock every visible demo skill and full clear ends the demo;
- world authoring tool can generate, inspect, edit, validate and freeze shipped worlds;
- visual/post-processing pass has been compared against the supplied reference still/video using fixed-camera captures;
- save/replay schemas are versioned and validated;
- CI catches progression/content/hash/replay regressions;
- no work in this pass depends on a player-facing infinite generator.
