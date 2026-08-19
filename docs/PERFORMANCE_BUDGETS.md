# Performance budgets and stress protocol

This file is the Phase 11 performance contract. The important rule is that costs must be bounded by
visible/active work, not by the logical dimensions of a world.

## Runtime budgets

| Area | Budget / invariant |
|---|---|
| Logical world construction | No per-voxel, per-chunk, or per-region allocation for untouched space |
| Detailed render chunks | Bounded camera-facing working set on streaming profiles |
| Streamed chunk construction | Surface-column work only; never scan the full chunk volume to discover an already-known exterior shell |
| Interactive camera drag | Do not construct streamed detail while RMB/MMB is held; macro representation keeps the world continuously visible |
| Streamed detail frame budget | Up to 4 cheap builds, stopping after ~2.5 ms of detail-build work in a frame |
| Whole-world far representation | `6 * macro_resolution^2` cells, independent of world dimensions, with no visible gaps between cells |
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
2. performs 128 deterministic near-shell generator queries each rendered frame;
3. every two wall-clock seconds exhausts one logical region through the hierarchical `MiningService` path;
4. records observed FPS, managed memory, generator query cost, chunk build cost, streaming load/unload
   counts, sparse voxel count and aggregate region count.

Benchmark duration is measured using `Time.GetTicksUsec()`, not `_Process(delta)`. Godot clamps very
large frame deltas; using `delta` made the original 20-second benchmark take several minutes when the
renderer was already running at ~1 FPS. Normal scene/window teardown also writes an `aborted` report.
A hard OS process kill cannot be intercepted from inside the game.

The latest text report is written to:

```text
user://stress_benchmark_latest.txt
```

## Phase 11 acceptance targets

These are initial engineering targets, not promises for final hardware requirements.

- Entering `stress_1000` must not create millions of Nodes, chunk objects, voxel objects, or dictionary
  entries.
- RMB/MMB camera input must remain responsive even while the desired detail working set changes.
- Average streamed chunk build should be **under 4 ms** on the current development machine, with no
  routine build above **12 ms**. The old 1.38-second build is treated as a hard regression.
- The macro proxy must show all six faces as a continuous shell with no deliberate cell gaps/ridges.
- Macro proxy instance count must remain fixed when logical dimensions grow.
- `SparseVoxelOverrideCount` must remain zero when the benchmark only uses region exhaustion.
- Exhausting a region must reduce the exact remaining counter by that region's deterministic quota in
  O(1) state space.
- A stress save containing exhausted regions should grow roughly with the number of exhausted region
  markers rather than the number of blocks represented by them.

## First measured baseline — before the surface-column fix

Local benchmark supplied on 2026-08-19:

| Metric | Baseline |
|---|---:|
| Benchmark wall-clock duration | 20.43 s |
| Minimum observed FPS | 1.0 |
| Managed memory | 8.2 MB |
| Average generator query | 8.430 µs |
| Maximum 128-query batch | 1.654 ms |
| Average detailed chunk build | **1380.179 ms** |
| Last detailed chunk build | 1341.115 ms |
| Streamed loads/unloads | 19 / 17 |
| Aggregate blocks represented | 56,000,000 |
| Sparse voxel overrides | 0 |
| Exhausted regions | 7 |

The baseline isolated the bottleneck: `RebuildChunk` scanned a 32^3 volume and called `IsExposed` on
candidate voxels. `IsExposed` performs neighbour sampling, multiplying the already expensive terrain
noise work. Generator probes themselves were only ~8.4 µs each; the pathological cost came from doing
hundreds of thousands of them per detail build.

## Current optimization

Streaming profiles now use a different path from the small-world exact renderer:

- `ProceduralWorldSource.TrySampleOutermostSurfaceVoxel` evaluates terrain once per tangential column.
- Streamed detail chunks emit only the visible outer block for each column; mining a modified column
  performs a short inward fallback to expose its next block.
- `stress_1000` uses 8-voxel chunks rather than 32-voxel chunks.
- Relief depth is covered by several cheap inward chunks instead of one expensive 32-voxel volume.
- No streamed detail builds run while the user is actively dragging the camera.
- The macro proxy uses the same direct surface sampler instead of a radial depth search.
- Macro cells overlap slightly and have inward skirts so height differences do not expose black gaps.

The next local F7 run should be compared directly against the baseline above.

## Scale semantics

The gameplay end goal is **1,000,000 mineable blocks total**. `stress_1000` remains intentionally huge
at 1000 x 1000 x 1000 logical address dimensions because it is an adversarial renderer/address-space
test, but its authoritative mining target is now also 1,000,000 blocks. It is not a proposed final
playable world size.

`final_target_1m` is a separate non-progression validation profile with an exact 1,000,000-block
authoritative counter and more plausible cube dimensions. Exact final dimensions/art direction remain
content decisions; the architecture no longer assumes a billion- or trillion-block gameplay target.
