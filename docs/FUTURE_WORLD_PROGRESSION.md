# Future world progression plan

This document records the intended product progression after the current Phase 12-14 implementation pass. It is **planning only** for now: the current authored runtime progression should not be replaced until the tutorial/pacing pass is started deliberately.

The guiding principle is to teach one or two concepts at a time on very small deterministic cubes before asking the player to manage a fully generated world. Every shipped world should be generated deterministically from an authored profile/seed, reviewed by hand, and then committed as a predetermined demo/full-release world. A player-facing infinite/random cube generator is a possible very-late future feature, not part of the current demo scope.

## Tutorial world 0 — 1 x 1 x 1

Purpose: teach the absolute base interaction with no distraction.

- One single mineable cube.
- Teach orbit/zoom only as needed and LMB manual mining.
- Completion is immediate and establishes the world-clear -> Continue loop.
- No automation and no complex UI required here.

## Tutorial world 1 — 5 x 5 x 5 dirt cube

Purpose: teach that manual mining itself can be upgraded.

- Mostly/all dirt-family terrain.
- Introduce the manual-mining branch of the skill tree.
- Planned manual upgrade lessons:
  - faster mining cadence;
  - bigger/multi-block manual mining;
  - hover mining, where an unlocked mode repeatedly mines the block currently hovered instead of requiring a fresh click every time.
- Keep geometry deliberately simple so the player notices the effect of each manual upgrade immediately.

## Tutorial world 2 — 10 x 10 x 10 lake/core cube

Purpose: teach material/terrain restrictions and why specialised tools exist.

- One authored lake.
- A stone core begins roughly three blocks below the outer surface.
- Dirt/sand-family surface terrain remains shovel-friendly.
- Water interrupts shovel routes.
- Stone also stops/prevents the shovel, making the need for another tool obvious.
- This is the first world where tool choice should matter more than raw clicking speed.

## Tutorial world 3 — 15 x 15 x 15 trees + special resource

Purpose: combine surface obstructions with the first transformational tool upgrade.

- Water remains present.
- Stone core remains present.
- Trees are introduced as real surface blockers: ground under a tree must be cleared manually or by the Forest Cutter before the Shovel can consume that support tile.
- Place one authored special resource/gem near the core.
- The special resource acts as a distinct progression currency/token for a major skill-tree transformation rather than only ordinary resources.
- Example transformation: upgrade the basic Drill into the 3 x 3 Wide Bore Drill.
- The special-resource framework should be generic enough for later tools to have their own enhanced forms.

## World 4 — 20 x 20 x 20 first full generated world

Purpose: transition from tutorials into the normal game.

- First world using the full Verdant-style deterministic terrain language.
- Trees, sand/dirt, stone, water, cliffs/plateaus and normal resources all participate together.
- Multiple gems/special resources may be scattered below the surface.
- The player should now be expected to combine manual mining, Shovel, Drill and surface-clearing tools without tutorial-style isolation.

## World 5 — 40 x 40 x 40 active-gameplay world

Purpose: expand the skill tree and add optional active world interactions while automation continues.

- Larger deterministic world with rarer gems used for more advanced tool upgrades.
- Introduce optional weather/world-event mechanics so the player has something active to do while automations work.

Planned weather interaction:

- Clouds remain physical/presentational objects orbiting the cube.
- Repeatedly clicking an eligible cloud can charge/trigger a lightning strike on the world.
- A lightning strike creates a bounded crater and removes blocks through the authoritative MiningService accounting path.
- Later upgrades may reduce required clicks, increase strike strength, improve targeting, or automate cloud/lightning usage.

Planned meteor interaction:

- Occasionally a meteor enters orbit around the cube for a limited time.
- The player can catch/grab the orbiting meteor and throw/direct it into the world.
- Impact removes a large but bounded group of blocks and creates a visible crater/effect.
- Missing the opportunity lets the meteor leave orbit without reward.
- Later upgrades may improve meteor frequency, capture time, impact strength, targeting, or partially automate the interaction.

Both systems are optional active accelerators, not mandatory chores. Automation should continue while the player ignores them.

## World 6 — 50 x 50 x 50 Steam demo finale

Purpose: final mastery test for the demo.

- Uses the complete deterministic world-generation language established by the earlier real worlds.
- Player has access to nearly the full demo skill tree.
- Multiple automation classes and enhanced tool forms should be useful simultaneously.
- Weather/lightning and meteor interactions can appear frequently enough to matter.
- Late demo skills may automate or partially automate thunder/meteor mechanics so the player can see the incremental-game transition from active interaction to automation.
- Clearing/completing this world is the intended end state of the Steam demo.

## Full release target — 100 x 100 x 100

Purpose: current planned full-release end state.

- Approximately one million logical block addresses at 100 cubed.
- Additional worlds may be inserted between 50 and 100 depending on pacing and feature count.
- The exact final sequence is intentionally not locked yet.
- The existing one-million/full-surface architecture remains useful for this destination, but early-game quality takes priority over further tuning of it right now.

## Deterministic authoring rules

All demo/full-release worlds should follow these rules:

1. World generation remains deterministic from versioned profile data + seed.
2. Candidate seeds may be generated offline/editor-side until a visually/gameplay-appropriate world is found.
3. Selected profiles/seeds are then committed and shipped as predetermined authored worlds.
4. Runtime should not silently reroll a shipped world.
5. Save data stores modifications/progression, not a complete materialized copy of untouched terrain.
6. Generation/version changes that would alter a shipped world require explicit migration/versioning rather than changing the world underneath existing saves.

## Manual mining upgrades to add during the tutorial-world implementation

These are planned, not implemented by this document:

- mining-speed/cadence upgrade;
- larger manual mining footprint / additional blocks per action;
- hover-mining mode with controlled repeated mining while the cursor remains over a valid block;
- appropriate rate limits so hover mining feels like an earned quality-of-life upgrade rather than an uncontrolled per-frame mining exploit.

## Special-resource/tool-transformation framework

The 15 x 15 x 15 world introduces the concept. The implementation should eventually support:

- ordinary resource currency for normal skills;
- rare/special resource tokens for transformational tool upgrades;
- skill nodes capable of requiring both ordinary resources and one or more special-resource types;
- tool transformations such as basic Drill -> 3 x 3 Wide Bore;
- later equivalent transformations for Shovel, Forest Cutter, Rock Breaker and future automations without hard-coding every transformation into UI code.

## Far-future infinite/random cube generator

Possible post-release/future feature only:

- player-selectable/random seeds;
- potentially unbounded sequence of generated cubes rather than one literally infinite materialized cube;
- generation rules derived from the same deterministic profile system;
- must not compromise stability or authored progression saves;
- no implementation work should be scheduled for this until the authored demo/full-release progression is complete and proven.
