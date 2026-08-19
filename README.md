# 10 Million Blocks

Godot 4.6.1 C# project for the block-mining incremental game currently being designed under the working names **1 Million Blocks / 1 Million Squared**.

The intended late-game progression target is **1,000,000 mineable blocks total**. The separate `stress_1000` world deliberately uses a pathological 1000 x 1000 x 1000 logical address space to test streaming/state architecture; it is not the planned visual shape or block count of the final world.

## Current development slice

The implementation now includes the main architectural/gameplay systems from the implementation plan:

- deterministic cube-world generation with terrain, sand, shallow/deep water, ores, trees and multiple authored profiles
- supplied-model chunk rendering with large-world macro/detail streaming
- block picking, hover feedback and authoritative manual mining
- scalable sparse mined state and aggregate region exhaustion
- placeable animated drill automation and material-aware debris
- Powered Shovel automation with a deliberately primitive base behavior and skill-driven intelligence/speed upgrades
- data-driven skill tree with repeatable/rank-gated upgrades and selectable drill mining patterns
- standalone grid-based skill-tree editor with routed prerequisites
- world completion -> overview -> Continue progression flow
- save/load and bounded offline automation foundation
- large-address-space diagnostics, performance HUD and automated benchmark
- compact gameplay HUD and close-surface camera mode for very large worlds

See `docs/IMPLEMENTATION_STATUS.md` for the current checkpoint and deferred work.

## Run on Windows

Use the .NET build of Godot 4.6.1.

```bat
play_game.bat
```

The helper searches for Godot in common locations and on `PATH`. To force a specific editor executable:

```bat
set GODOT_PATH=C:\path\to\Godot_v4.6.1-stable_mono_win64.exe
play_game.bat
```

To compile without launching:

```bat
build_game.bat
```

To edit the skill tree visually:

```bat
skill_tree_editor.bat
```

## Gameplay controls

- `LMB`: mine highlighted block / interact with UI
- `RMB drag`: orbit camera
- `MMB drag`: pan camera
- Mouse wheel: zoom; large worlds automatically use finer steps near the surface
- `1` / `2` / `3`: far / medium / near camera presets
- `F`: recenter camera pan
- `K`: open/close skill tree
- `M`: place the unlocked drill on the highlighted block
- `N`: place the unlocked Powered Shovel on a highlighted sand block
- `H`: expand/collapse gameplay HUD details

Debug-build controls:

- `F8`: toggle the 1000-address-space stress profile
- `F9`: performance/streaming diagnostics
- `F7`: run/cancel the 20-second stress benchmark while in a streaming profile
- `F10`: preview the completion/Continue flow without actually clearing the current world

## Content

Primary editable runtime content lives under `data/`:

- `data/blocks/blocks.json` — block definitions, supplied model paths, values and material tags
- `data/worlds/worlds.json` — world-generation and streaming profiles
- `data/miners/miners.json` — automation definitions
- `data/skills/skill_tree.json` — skill graph, costs, ranks and effects
- `data/progression/world_progression.json` — authored world order

Supplied runtime model paths primarily use `Assets/gltf`, with the added forest models under `Assets/forest` and the Powered Shovel under `Assets/godeeper`. See `docs/ASSET_CATALOG.md` for asset scale/batching notes.
