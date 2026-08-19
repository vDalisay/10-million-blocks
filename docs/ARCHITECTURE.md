# Architecture

## Goals

The demo establishes the core loop for **10 Million Blocks**:

1. Generate an exact-count procedural voxel world.
2. Let the player manually mine any visible block.
3. Convert mined blocks into currency.
4. Spend currency on multiplicative manual power, speed, and automation.
5. Progress from 1 → 100 → 1,000 → 10,000 blocks.
6. Regenerate the final stage with a new seed for endless demo play.

The visual target is a small floating voxel planet: a rounded cube silhouette, chunky green terrain, exposed stone, pockets of blue water, simple trees/ruins, white block clouds, and dramatic dark-space presentation.

## Runtime structure

### Domain / progression

`GameState` owns currency, stage progress, total blocks mined, and generation seed.

`UpgradeSystem` owns upgrade levels and balancing formulas. Gameplay controllers read its derived values; they do not duplicate balancing rules.

`MiningService` is the single path through which manual and automatic mining award currency and emit feedback.

### World representation

`VoxelWorld` keeps logical voxels in:

- `Dictionary<Vector3I, BlockType>` for block occupancy/type.
- `Dictionary<Vector3I, float>` for partial damage.
- `HashSet<Vector3I>` for currently exposed surface blocks.

A block is data, **not a Node3D**.

### Rendering

`VoxelMesher` builds one mesh per 8×8×8 chunk. Only exposed faces are emitted. Face colors are baked into vertex colors and shaded by face direction for a readable voxel look.

Destroying a voxel marks only its own chunk and neighboring chunk boundaries dirty. Dirty chunk rebuilds are budgeted across frames.

This follows Godot's general performance guidance: reduce scene-tree object count and batch large quantities of simple geometry instead of creating thousands of independent nodes.

### Picking

`VoxelRaycaster` uses 3D DDA traversal through the voxel grid, so manual selection does not require a `StaticBody3D`/collision shape for every block.

### Automation

`AutoMiningController` samples from the surface-block cache. Its batch size grows exponentially with the automation upgrade but is capped per tick to keep frame work bounded.

## Scaling toward 1,000,000–10,000,000 blocks

The current 10k stage is intentionally kept simple enough for an initial demo. The architecture already separates simulation from rendering, but the following changes should happen before shipping million-scale worlds:

1. Replace the global voxel dictionary with fixed-size chunk storage using packed arrays/bitsets.
2. Generate chunk data asynchronously on worker threads; only create/replace Godot meshes on the main thread.
3. Persist mined state per chunk as compressed bitsets rather than serializing individual coordinates.
4. Add greedy meshing to merge coplanar faces and reduce vertex count further.
5. Stream distant chunks and use chunk-level visibility ranges/frustum decisions.
6. Batch large auto-mining operations and rebuild each affected chunk once per frame.
7. Consider `RenderingServer`/`MultiMesh` for decorative repeated elements while keeping terrain as chunk meshes.
8. Add profiling counters for generated faces, dirty rebuild time, visible chunks, auto-mining operations, and memory per logical voxel.

The key invariant should remain: **block count is gameplay data; rendered geometry is a derived cache.**
