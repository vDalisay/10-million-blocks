# 10 Million Blocks

Godot 4.6.1 C# project for the block-mining incremental game currently being designed under the working names **1 Million Blocks / 1 Million Squared**.

The intended late-game progression target is **1,000,000 mineable blocks total**. The one-million world is now rendered as actual block-scale terrain: the visible surface is made from the same supplied block meshes and voxel addresses the player can mine. The earlier coarse macro-cell world is no longer the product direction.

## Current implementation

The branch now includes the main architectural/gameplay systems from `docs/IMPLEMENTATION_PLAN.md`:

- deterministic cube-world generation with plateaus, beaches, shallow/deep water, ores and trees
- small-world eager rendering plus a real-block full-surface renderer for the one-million end world
- block picking, hover feedback and authoritative manual mining
- sparse mined state, exact 64-bit target accounting and aggregate region state
- base drill that advances exactly one depth block/second
- Wide Bore upgrade that physically scales the drill to a 3x3 cutter and clears one 3x3 depth slice/second
- square Wide Bore excavation; disc-shaped excavation is not used
- Powered Shovel with sand-only base traversal, speed upgrades, slope sensing and Terrain Scout
- Rock Breaker/pickaxe automation specialized for stone, ore and gem blocks
- Forest Cutter/axe automation that seeks deterministic tree-bearing surface blocks
- deterministic deep gem pockets with high-value rewards
- deterministic unstable blocks that require repeated manual hits and then clear a bounded blast radius
- data-driven skill tree, repeatable/rank-gated upgrades and standalone visual skill-tree editor
- world completion -> overview -> Continue progression, ending in the one-million world
- save/load and bounded offline automation foundation
- compact gameplay HUD, mining feedback, camera safety/zoom smoothing and performance diagnostics

See `docs/IMPLEMENTATION_STATUS.md` for current verification status and `docs/ONE_MILLION_WORLD_RENDERING.md` for the large-world rendering decision.

## Run on Windows

Use the .NET build of Godot 4.6.1.

```bat
play_game.bat
```

The helper searches common locations and `PATH`. To force a specific editor executable:

```bat
set GODOT_PATH=C:\path\to\Godot_v4.6.1-stable_mono_win64.exe
play_game.bat
```

Compile without launching:

```bat
build_game.bat
```

Edit the skill tree visually:

```bat
skill_tree_editor.bat
```

## Gameplay controls

- `LMB`: mine highlighted block / select a placement target
- `RMB drag`: orbit camera
- `MMB drag`: pan camera
- Mouse wheel: zoom; large worlds automatically use finer steps near the surface
- `1` / `2` / `3`: far / medium / near camera presets
- `F`: recenter camera pan
- `A`: open/close the right-side automation menu
- `K`: open/close skill tree
- `M`: open the automation menu focused on the drill
- `N`: open the automation menu focused on the Powered Shovel
- `H`: expand/collapse gameplay HUD details
- `P`: open the automation menu focused on the Rock Breaker
- `C`: open the automation menu focused on the Forest Cutter

Debug-build controls:

- `F8`: toggle the real-block one-million debug world
- `F9`: performance/render diagnostics
- `F7`: run/cancel the 20-second stress benchmark while in a large profile
- `F10`: preview completion/Continue without clearing the current world

## Content

Primary editable runtime content lives under `data/`:

- `data/blocks/blocks.json` — block definitions, model paths, values and material tags
- `data/worlds/worlds.json` — world-generation/render profiles
- `data/miners/miners.json` — automation definitions and material affinities
- `data/skills/skill_tree.json` — skill graph, costs, ranks and effects
- `data/progression/world_progression.json` — authored world order

Runtime model paths primarily use `Assets/gltf`, with forest models under `Assets/forest` and the Powered Shovel under `Assets/godeeper`. The current Rock Breaker/Forest Cutter are procedural placeholder presentations so their mechanics are testable without blocking on final art. Gem/unstable blocks currently reuse supplied colored block meshes. See `docs/ASSET_CATALOG.md` for batching and asset-scale notes.
