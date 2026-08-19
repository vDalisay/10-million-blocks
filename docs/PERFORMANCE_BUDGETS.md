# Performance budgets and stress protocol

This file is the Phase 11 performance contract. The important rule is that costs must be bounded by
visible/active work, not by the logical dimensions of a world.

## Runtime budgets

| Area | Budget / invariant |
|---|---|
| Logical world construction | No per-voxel, per-chunk, or per-region allocation for untouched space |
| Detailed render chunks | Bounded camera-facing working set on streaming profiles |
| Detailed chunk builds | At most 1 streamed chunk build per frame; 2 dirty builds/frame on small worlds |
| Whole-world far representation | `6 * macro_resolution^2` cells, independent of world dimensions |
| Sparse save state | Individual mined voxels only in modified chunks; fully exhausted regions collapse to one marker |
| Aggregate mining | One event/state mutation per exhausted region, not one event per logical block |
| Generator stress probes | Deterministic arbitrary-coordinate queries; no cache population required for correctness |
| Offline work | Small worlds may replay a bounded number of exact operations; large worlds use region aggregates |
| Counter width | Signed 64-bit throughout mined/remaining/resource aggregate paths |

## Debug controls

- **F8** — toggle the non-persistent `stress_1000` profile.
- **F9** — performance HUD.
- **F7** — while in a streaming profile, run the 20-second automated stress benchmark.
- **F10** — existing small-world completion-flow preview.

The F7 benchmark performs all of the following simultaneously:

1. slowly rotates the camera so the detailed chunk working set has to stream;
2. performs 128 deterministic near-shell generator queries each frame;
3. every two seconds exhausts one logical region through the hierarchical `MiningService` path;
4. records observed FPS, managed memory, generator query cost, chunk build cost, streaming load/unload
   counts, sparse voxel count and aggregate region count.

The latest text report is also written to:

```text
user://stress_benchmark_latest.txt
```

## Phase 11 acceptance targets

These are initial engineering targets, not promises for final hardware requirements. They should be
revised after several local measurements.

- Entering `stress_1000` must not create millions of Nodes, chunk objects, voxel objects, or dictionary
  entries. The intended steady-state detailed working set is roughly `(2r+1)^2 * depth` chunks, where
  `r` comes from the world profile; the default stress profile uses `r=1`, `depth=1`.
- Camera input must remain responsive while the streaming queue is non-empty.
- Macro proxy instance count must remain fixed when logical dimensions grow.
- `SparseVoxelOverrideCount` must remain zero when the benchmark only uses region exhaustion.
- Exhausting a region must reduce the exact remaining counter by that region's deterministic quota in
  O(1) state space.
- The million-scale self-test must create and bulk-exhaust a distant region without enumerating the
  world, and without creating sparse per-voxel overrides.
- A stress save containing exhausted regions should grow roughly with the number of exhausted region
  markers rather than the number of blocks represented by them.

## Measurement table

The instrumentation is now committed, but actual GPU/frame timings require a local Godot run. Fill
this table from the F7 report rather than estimating values from code review.

| Metric | `stress_1000` baseline | Notes |
|---|---:|---|
| Minimum observed FPS | pending local run | F7 report |
| Managed memory MB | pending local run | F7 report |
| Average generator query µs | pending local run | F7 report |
| Maximum 128-query batch ms | pending local run | F7 report |
| Average detailed chunk build ms | pending local run | F7 report |
| Macro proxy build ms | pending local run | F9 HUD |
| Streamed loads/unloads over 20s | pending local run | F7 report |
| Aggregate blocks represented by region markers | pending local run | F7 report |

## Final-scale validation

`final_scale_1m` is intentionally not part of normal progression. It represents a one-million-wide
logical address space and an authored **1,000,000,000,000** block target. Startup self-tests query far
coordinates, compute region quotas and exhaust a distant region using constant/small state. Rendering
that entire address space in block detail is explicitly not a requirement; the renderer remains a
macro proxy plus a bounded detailed patch.
