# Supplied Block Asset Catalog

## Canonical runtime source

The first implementation uses `Assets/gltf` as the canonical runtime source. The FBX/OBJ variants remain untouched as source/reference exports.

The supplied block meshes share a common Blender-authored coordinate convention. Representative terrain and tree models have bounds close to:

- X: `-1.083 .. +1.083`
- Y: `-1.0 .. +1.0`
- Z: `-1.083 .. +1.083`

The reference slice therefore treats one logical block step as **2.0 Godot units**. The slight authored overshoot on X/Z is intentional and helps hide hairline gaps between neighboring irregular block meshes.

## Runtime rules

- Stable block IDs come from `data/blocks/blocks.json`.
- Gameplay code must request meshes through `BlockAssetRegistry`; it must not hard-code file paths throughout the project.
- Imported glTF resources are validated at startup during the current development slice. Missing IDs/assets fail visibly instead of silently substituting geometry.
- Dense block populations are rendered with `MultiMesh` or chunk meshes. A logical block is not represented by a persistent `Node3D`.
- Imported model pivots are centered at the logical block center for the registered assets. Terrain batching therefore places transforms directly on the logical grid.
- Materials and UVs stay attached to the imported meshes for near/reference rendering. Later chunk meshing must preserve the same visual vocabulary rather than replace it with unrelated flat colors.

## Registered Phase 1 assets

The initial catalog covers:

- grass / grass edge
- dirt
- stone / dark stone
- copper / silver / gold ore
- sand
- water
- wood
- brick
- tree

Additional supplied assets can be registered without changing runtime code.

## Reference-slice usage

Phase 2 deliberately renders a compact authored/procedural test planet instead of the final virtual-world generator. It exists to settle:

1. asset scale and spacing,
2. lighting/material readability,
3. camera framing and motion,
4. cloud/background treatment,
5. near/medium/far visual expectations.

Once those are approved, Phase 3 replaces the temporary planet builder with the scalable deterministic world source while retaining these presentation choices.
