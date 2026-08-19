# One-million world rendering decision

## Product direction

The late-game target is **1,000,000 mineable blocks total**. The final world must read as one continuous block world made from the same block-scale terrain the player mines.

The previous large-world experiment used a coarse macro shell whose cells visually represented many smaller logical blocks. That architecture was useful for proving sparse state and streaming performance, but it is **not the desired product presentation** and must not be reintroduced as the normal renderer for the one-million world.

## Current renderer

`rendererMode: "full_surface"` selects `WorldView`'s real-block full-surface path.

- No `MacroWorldProxy` is created for the one-million profiles.
- Every visible terrain cell is an actual voxel address rendered with the supplied block mesh for that block ID.
- Initial work is limited to surface-shell chunks. Interior voxels remain deterministic and unallocated until they become relevant.
- Surface chunks use the fast surface-column sampler so loading cost scales primarily with visible area, not full volume.
- When mining changes a chunk, that chunk switches to the exact exposed-voxel rebuild path. This allows tunnels, holes and newly exposed interior walls to remain composed of real blocks.
- Mining progressively adds interior chunks to the render working set as the player moves inward.
- MultiMesh batching remains mandatory; there is still no Node3D or collider per logical block.

This preserves the scalability rules from the original plan without sacrificing the player's ability to see and mine the actual blocks making up the world.

## One-million accounting

The authored target remains exactly `1_000_000` blocks and uses 64-bit counters. The current 100-axis profiles deliberately include physical terrain headroom around that target; region quotas cap authoritative progression at exactly one million mined blocks while untouched terrain remains deterministic.

The physical generator shape and biome parameters can be tuned later without returning to coarse macro cells. If future tuning changes block density, the invariant to protect is: **the player-facing world is real blocks; aggregation belongs to state/accounting, not to visible geometry.**

## Diagnostic macro renderer

`MacroWorldProxy` remains in the codebase only as a diagnostic/experimental renderer for profiles using the default large-world `auto` mode. It is not used by `stress_1000` or `final_target_1m` after this decision.

## Verification targets

For a local review of the one-million world:

1. Press `F8` to enter the one-million debug profile.
2. F9 must report `real-block full surface` and `macro: disabled (real blocks only)`.
3. Far and medium views must show recognizable individual terrain blocks rather than large proxy cells.
4. Near view must remain outside the cube and expose the same block meshes seen from farther away.
5. Mining must expose real deeper blocks; no proxy face should be revealed behind the hole.
6. Frame pacing during initial surface population and subsequent mining is the remaining local performance checkpoint.
