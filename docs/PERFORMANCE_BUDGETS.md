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
| Close inspection | Transition orbit pivot toward the viewed surface; do not solve close zoom by placing a centre-orbit camera inside the cube |
| Whole-world far representation | Fixed `6 * macro_resolution^2` cells, independent of world dimensions; diagnostic LOD must remain visually contiguous |
| Sparse save state | Individual mined voxels only in modified chunks; fully exhausted regions collapse to one marker |
| Aggregate mining | One event/state mutation per exhausted region, not one event per logical block |
| Generator stress probes | Deterministic arbitrary-coordinate queries; no cache population required for correctness |
| Offline work | Small worlds may replay a bounded number of exact operations; large worlds use region aggregates |
| Counter width | Signed 64-bit throughout mined/remaining/resource aggregate paths |

## Debug controls

- **F8** — toggle the non-persistent `stress_1000` profile.
- **F9** — performance/LOD HUD.
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
  routine build above **12 ms**.
- The far proxy must show all six faces without deliberate gaps; close inspection must switch to real
  supplied block meshes instead of magnifying a macro tile.
- Macro proxy instance count must remain fixed for a given authored resolution when logical dimensions grow.
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

## Phase 11 validation — surface-column renderer

Second local benchmark supplied on 2026-08-19 after the renderer rewrite:

| Metric | Validated result |
|---|---:|
| Benchmark wall-clock duration | 20.01 s |
| Generator probes | 418,176 |
| Average generator query | 8.699 µs |
| Maximum 128-query batch | 2.233 ms |
| Minimum observed FPS | **159.0** |
| Average detailed chunk build | **0.626 ms** |
| Last detailed chunk build | **0.813 ms** |
| Streamed loads/unloads | 5,759 / 1,331 |
| Aggregate blocks represented | 12,348 |
| Sparse voxel overrides | 0 |
| Exhausted regions | 9 |
| Managed memory | 5.3 MB |

This passes the core Phase 11 CPU/frame-time budget by a large margin. The immediate renderer rewrite
is therefore considered successful. The remaining large-world work is now primarily **LOD quality,
camera inspection UX, and unnecessary stream churn**, rather than raw chunk-build cost.

The high load/unload counters came from changing camera focus faster than the old pending load queue
could drain. The streaming set now rebuilds that tiny queue from the current desired set whenever focus
changes, discarding stale requests rather than carrying them forward.

## Current large-world LOD/inspection strategy

The 1000-wide diagnostic world exposed a second, visual problem after performance was fixed: a
centre-orbit camera and coarse far cells cannot also provide useful close block inspection.

The renderer/camera now use three continuous regimes:

1. **far/whole-world:** fixed macro shell, now using a denser authored macro resolution;
2. **transition:** orbit pivot moves from world centre toward the currently viewed surface and the
   streamed detailed working set expands;
3. **close inspection:** camera stand-off is measured from the surface pivot, real supplied block
   meshes and nearby tree features fill the detailed patch, and the coarse macro shell is hidden once
   that patch has caught up.

RMB/MMB manipulation still temporarily keeps the macro shell visible and suspends detail construction,
so the user never pays chunk-build latency directly in pointer movement. F9 now exposes `surface focus`,
current detail radius, and whether the macro shell is visible so this transition can be diagnosed locally.

`stress_1000` uses a deliberately higher macro resolution after the successful CPU benchmark. It is
still a diagnostic stress world and is not expected to become final art; final large-world appearance
should be authored around the actual progression world's scale rather than a pathological 1000-wide cube.

## Scale semantics

The gameplay end goal is **1,000,000 mineable blocks total**. `stress_1000` remains intentionally huge
at 1000 x 1000 x 1000 logical address dimensions because it is an adversarial renderer/address-space
test, but its authoritative mining target is also 1,000,000 blocks. It is not a proposed final playable
world size.

`final_target_1m` is a separate non-progression validation profile with an exact 1,000,000-block
authoritative counter and more plausible cube dimensions. Exact final dimensions/art direction remain
content decisions; the architecture no longer assumes a billion- or trillion-block gameplay target.
