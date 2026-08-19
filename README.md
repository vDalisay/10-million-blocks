# 10 Million Blocks

Godot 4.6.1 C# project for the block-mining incremental game currently being designed under the working names **1 Million Blocks / 1 Million Squared**.

## Current development slice

This branch implements Phase 1 and Phase 2 of `docs/IMPLEMENTATION_PLAN.md`:

- validated, data-driven supplied block asset catalog
- centered floating cube-planet reference scene
- supplied grass/dirt/stone/water/ore/tree/brick meshes
- smooth orbit, pan, and zoom camera
- fixed far/medium/near reference camera presets
- layered block clouds, dark space, and lighting pass
- visual validation harness

The temporary planet is a presentation test. It is intentionally separate from the scalable procedural generator planned for Phase 3.

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

## Reference-slice controls

- Left mouse drag: orbit
- Right or middle mouse drag: pan
- Mouse wheel: zoom
- `1`: far reference preset
- `2`: medium reference preset
- `3`: near reference preset
- `F`: recenter after panning

## Content

Block definitions are in `data/blocks/blocks.json`. Runtime model paths use `Assets/gltf`; see `docs/ASSET_CATALOG.md` for asset scale and batching rules.
