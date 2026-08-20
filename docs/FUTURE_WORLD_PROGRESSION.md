# Future world progression plan

This document drives the progression pass that now starts with tutorial world 0. The existing Verdant sequence remains a temporary playable bridge after that first tutorial until the later authored tutorial worlds replace it.

The guiding principle is to teach one or two concepts at a time on very small deterministic cubes before asking the player to manage a fully generated world. Every shipped world should be generated deterministically from an authored profile, seed and generation version, reviewed by hand, and then committed as a predetermined demo/full-release world. A player-facing infinite/random cube generator is a possible very-late future feature, not part of the current demo scope.

## World scale and rounded block totals

World labels such as 5 x 5 x 5 or 40 x 40 x 40 describe the intended approximate visual scale. They do not require the generated terrain to be a perfect solid cube with exactly that mathematical volume.

The terrain generator should have freedom to create coherent hills, cliffs, lakes, shorelines and other authored terrain features. After a candidate profile/seed is reviewed offline:

1. Count its physical mineable blocks.
2. Manually choose a nearby clean-looking total by rounding **up**, never down. For example, a candidate containing 128 blocks may receive an authored target of 130.
3. Integrate the small difference into a coherent existing terrain feature, such as extending a hill or cliff. Never append isolated filler blocks merely to satisfy the count.
4. Commit the resulting profile, seed, generation version, rounded target and expected validation counts together.
5. Runtime generation must reproduce that exact physical block total deterministically.

The rounded target is selected by hand for each shipped world; there is no universal rounding formula. Tutorial world 0 remains the deliberate one-block exception, and the full-release 100-cubed destination already has the clean target of exactly 1,000,000 authoritative mineable blocks.

## Development-save cutover

The replacement of the current provisional progression may reset existing development saves. No migration machinery is required for those pre-release saves.

The first cutover writes `savegame_v2.json`; the previous `savegame.json` is left untouched.

After the new progression becomes the active baseline:

- saves identify the current world by stable world ID rather than only by its position in the progression list;
- each saved world records its generation version;
- changing a shipped generator/profile requires an explicit migration, retained older generator version or clearly communicated reset;
- runtime must never silently regenerate untouched terrain differently underneath saved modifications.

## Tutorial world 0 — approximately 1 x 1 x 1

Purpose: teach the absolute base interaction with no distraction.

- One single exposed mineable cube and no generated filler.
- Teach LMB manual mining and the world-clear -> Continue loop.
- Orbit/zoom can be introduced later; the player should not be required to perform camera movement before clearing this world.
- Skill-tree and automation controls remain unavailable here.
- No automation and no complex UI required here.

## Tutorial world 1 — approximately 5 x 5 x 5 dirt cube

Purpose: teach that manual mining itself can be upgraded.

- Mostly/all dirt-family terrain.
- Introduce only the manual-mining branch of the skill tree; automation branches remain unavailable.
- Planned manual upgrade lessons:
  - bigger/multi-block manual mining;
  - toggleable hover mining;
  - faster hover-mining cadence where later tuning proves useful.
- Keep geometry deliberately simple so the player notices the effect of each manual upgrade immediately.

Hover mining behavior:

- unlocking hover mining exposes an explicit on/off toggle;
- it starts disabled so the player does not mine accidentally;
- while enabled, resting the cursor on a valid exposed block repeatedly mines without holding a mouse button;
- it operates at a controlled authored interval and never once per rendered frame;
- it pauses while a menu is open, automation is being placed/moved, the camera is being manipulated, input is otherwise captured, or the world-completion view is active;
- the selected on/off state is stored in the save.

## Tutorial world 2 — approximately 10 x 10 x 10 lake/core cube

Purpose: teach material/terrain restrictions and why specialised tools exist.

- One authored connected lake.
- A stone core begins roughly three blocks below the outer surface.
- Dirt/sand-family surface terrain remains shovel-friendly.
- Water interrupts shovel routes.
- Stone also stops/prevents the shovel, making the need for another tool obvious.
- Relevant tool branches become available deliberately during this world rather than through affordability alone.
- This is the first world where tool choice should matter more than raw clicking speed.

## Tutorial world 3 — approximately 15 x 15 x 15 trees + central special resource

Purpose: combine surface obstructions with the first transformational tool upgrade.

- Water remains present.
- Stone core remains present.
- Trees are introduced as real surface blockers: ground under a tree must be cleared manually or by the Forest Cutter before the Shovel can consume that support tile.
- The tree/Forest Cutter lesson should happen on the surface before the central gem is reached, naturally staging the two new concepts.
- The exact center voxel of the world is replaced by one authored special gem. The replacement does not increase the rounded world block total.
- Mining the central gem by any authoritative mining source awards exactly one special token.
- The token acts as a distinct progression currency for the Wide Bore transformation rather than only awarding ordinary resources.
- Purchasing Wide Bore consumes the token and upgrades every existing and future basic Drill into the 3 x 3 Wide Bore Drill.
- The special-resource cost model should support later tools without requiring a separate tool-transformation framework.

## World 4 — approximately 20 x 20 x 20 first full generated world

Purpose: transition from tutorials into the normal game.

- First world using the full Verdant-style deterministic terrain language.
- Trees, sand/dirt, stone, water, cliffs/plateaus and normal resources all participate together.
- Multiple gems/special resources may be authored below the surface, with their final deterministic count recorded during world review.
- The player should now be expected to combine manual mining, Shovel, Drill and surface-clearing tools without tutorial-style isolation.

## World 5 — approximately 40 x 40 x 40 active-gameplay world

Purpose: expand the skill tree and add optional active world interactions while automation continues.

- Larger deterministic world with rarer gems used for more advanced tool upgrades.
- Introduce optional weather/world-event mechanics so the player has something active to do while automations work.

Planned weather interaction:

- Clouds remain physical/presentational objects orbiting the cube.
- Repeatedly interacting with an eligible cloud can charge/trigger a lightning strike on the world.
- A lightning strike creates a bounded crater and removes blocks through the authoritative MiningService accounting path.
- Later upgrades may reduce required interaction, increase strike strength, improve targeting, or automate cloud/lightning usage.

Planned meteor interaction:

- Meteors are rare, optional, playful events rather than progression-critical accelerators.
- Occasionally a meteor enters orbit around the cube for a limited time.
- The player can catch/grab the orbiting meteor and throw/direct it into the world.
- Impact creates a visible but small, capped crater through the authoritative MiningService accounting path.
- Destroyed blocks grant their normal resources, and destroyed special-resource blocks still grant their tokens exactly once.
- Missing the opportunity lets the meteor leave orbit without penalty.
- Progression balance and expected world completion must never assume that the player uses meteors.
- Any later meteor upgrades should focus on handling, capture time, targeting or presentation unless playtesting deliberately changes its role.

Both systems are optional active interactions, not mandatory chores. Automation continues normally while the player ignores them.

## World 6 — approximately 50 x 50 x 50 Steam demo finale

Purpose: final mastery test for the demo.

- Uses the complete deterministic world-generation language established by the earlier real worlds.
- Player has access to nearly the full demo skill tree, with the exact available nodes recorded before implementation.
- Multiple automation classes and enhanced tool forms should be useful simultaneously.
- Weather/lightning can appear often enough to participate in the active loop; meteors remain rare optional extras.
- Late demo skills may automate or partially automate thunder mechanics so the player can see the incremental-game transition from active interaction to automation.
- Clearing/completing this world is the intended end state of the Steam demo.

## Full release target — approximately 100 x 100 x 100

Purpose: current planned full-release end state.

- Exactly 1,000,000 authoritative mineable blocks at the 100-cubed destination.
- Additional worlds may be inserted between 50 and 100 depending on pacing and feature count.
- The exact final sequence is intentionally not locked yet.
- The existing one-million/full-surface architecture remains useful for this destination, but early-game quality takes priority over further tuning of it right now.

## Progression availability rules

- Purchased skills, ordinary resources and special-resource balances carry forward between worlds.
- World/stage availability, not cost alone, decides when a skill branch becomes visible and purchasable.
- Tutorial world 0 exposes no skill or automation branches.
- Tutorial world 1 exposes the manual-mining branch only.
- Later worlds reveal the relevant tool branches when their lesson begins.
- Unavailable future branches should not distract the player with the complete late-game tree during early tutorials.

## Deterministic authoring rules

All demo/full-release worlds should follow these rules:

1. World generation remains deterministic from versioned profile data + seed.
2. Candidate seeds may be generated offline/editor-side until a visually/gameplay-appropriate world is found.
3. The candidate's physical block count is reviewed, rounded upward to an authored clean target and adjusted through coherent terrain rather than filler blocks.
4. Selected profiles, seeds, generation versions, rounded targets and expected validation counts are committed and shipped as predetermined authored worlds.
5. Runtime should not silently reroll or reshape a shipped world.
6. Save data stores modifications/progression, not a complete materialized copy of untouched terrain.
7. Save data records stable world IDs and generation versions.
8. Generation/version changes that would alter a shipped world require explicit migration/versioning rather than changing the world underneath existing saves.

## Manual mining upgrades to add during the tutorial-world implementation

These are planned, not implemented by this document:

- larger manual mining footprint / additional blocks per action;
- toggleable no-button hover-mining mode;
- controlled repeat cadence and later cadence upgrades where playtesting supports them;
- explicit pause/cancellation rules so hover mining cannot operate through menus, placement, camera manipulation or world completion;
- appropriate rate limits so hover mining feels like an earned quality-of-life upgrade rather than an uncontrolled per-frame mining exploit.

## Special-resource/tool-transformation rules

The approximately 15 x 15 x 15 world introduces the concept. The implementation should support:

- ordinary resource currency for normal skills;
- persisted balances keyed by special-resource type;
- skill nodes capable of consuming both ordinary resources and one or more special-resource types;
- exactly-once special-resource credit regardless of whether the source block is removed manually, by automation, by a blast, by lightning or by a meteor;
- class-wide tool transformations expressed through the existing skill-effect model, starting with basic Drill -> 3 x 3 Wide Bore;
- UI that renders authored resource costs from data rather than hard-coding each transformation.

Do not create a separate transformation subsystem until the existing skill-effect model measurably cannot express a required upgrade.

## Pacing policy

The plan does not prescribe target completion times for individual worlds or for the Steam demo. World sizes, costs and mining speeds should be built and then adjusted through playtesting.

Clear time may be observed as diagnostic information, but it is not an implementation gate. Tuning should prioritize whether progression feels understandable, satisfying and free of mandatory dead periods rather than forcing the game into a preselected number of minutes or hours.

## Future implementation acceptance rules

- WHEN a fresh save enters tutorial world 0 THEN the world contains exactly one exposed mineable block and unavailable systems remain hidden.
- WHEN a shipped world is validated THEN its physical mineable count equals its committed rounded target and its authored feature counts match expectations.
- WHEN hover mining is disabled THEN cursor hover alone never removes a block.
- WHEN hover mining is enabled over a valid block THEN mining repeats only at the controlled authored interval and obeys every pause condition.
- WHEN the central gem is removed by any authoritative source THEN exactly one special token is credited and persisted.
- WHEN Wide Bore is purchased THEN one token is consumed and every existing/future basic Drill uses the Wide Bore form.
- WHEN lightning or a meteor removes blocks THEN normal counters, rewards, special-resource credit, save state and completion accounting remain authoritative.
- WHEN a meteor is ignored or missed THEN the player loses nothing and automation continues normally.
- WHEN a save's world generation version differs from the available profile THEN the game migrates or explicitly resets/rejects it rather than silently generating different untouched terrain.

## Far-future infinite/random cube generator

Possible post-release/future feature only:

- player-selectable/random seeds;
- potentially unbounded sequence of generated cubes rather than one literally infinite materialized cube;
- generation rules derived from the same deterministic profile system;
- must not compromise stability or authored progression saves;
- no implementation work should be scheduled for this until the authored demo/full-release progression is complete and proven.
