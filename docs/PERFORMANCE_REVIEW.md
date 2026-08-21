# Large-world performance review

This review targets the 40³/50³ demo worlds and the 100³ / 1,000,000-block destination. It is based on the usual high-impact real-time game optimization categories and the current Godot/C# architecture rather than generic micro-optimizations.

## Ten high-impact areas and project action

1. **Profile before guessing** — retained the F9 runtime HUD and expanded it with pooled mining-FX active/pool/drop counters. Existing chunk-build, GC, cache, resident/presented chunk, automation-presentation, and feedback metrics remain the primary regression signal.
2. **Batch repeated geometry / reduce draw calls** — terrain already uses per-chunk MultiMeshes. Mining debris now uses one MultiMesh per burst rather than 7–9 MeshInstance3D nodes; the entire mining-selection footprint is also one MultiMesh.
3. **Pool short-lived effects** — manual/replay mine-pop meshes and debris bursts are pooled with hard active caps. Existing incremental pickup flights were already pooled. Automation debris still uses a standalone burst container, but now shares geometry/materials and contains a single MultiMesh instead of per-particle nodes.
4. **Cull work that cannot contribute pixels** — mining pop/debris is rejected before acquisition when behind the camera or outside a padded viewport. Full-surface chunk visibility remains camera-dependent and now skips redundant rescans when the camera and resident set did not change.
5. **Reduce managed allocations and GC pressure** — replay dirty-voxel lists and renderer dirty-chunk sets are reused. Per-pop Tweens were removed. Large save JSON is streamed directly to/from files instead of materializing a complete JSON string first.
6. **Use compact/data-oriented state** — per-chunk mined state now uses a fixed bitset instead of HashSet<int>. Save snapshot compatibility is preserved by converting the bitset to the existing sorted-index representation only when a snapshot is requested.
7. **Time-slice expensive world work** — existing chunk loading/rebuild queues already stage expensive initial and modified chunk work across frames with explicit build budgets. This remains preferable to creating Godot rendering nodes from worker threads.
8. **Spatial partitioning / streaming** — existing chunk + region addressing, shell rendering, streamed detail, macro context, and deferred off-screen automation are retained. These are the correct foundations for larger-than-demo worlds.
9. **Reuse immutable resources** — debris fragments share one BoxMesh and one material globally; block assets remain registry-cached/preloaded. Avoid per-effect materials/meshes in future particle-like systems.
10. **Bound cosmetic work independently from simulation** — authoritative mining is never dropped, but cosmetic pop/debris has active caps and off-screen suppression. This prevents high automation/replay speeds from turning presentation into the simulation bottleneck.

## Remaining large-world ceiling

The most important remaining architectural cost is **exact modified-chunk reconstruction**. A dirty chunk currently replaces its renderer root and rebuilds its MultiMesh batches. The work is frame-budgeted and coalesced, so it is safe for the reviewed demo worlds, but sustained high-rate visible mining in a 100³ world can still make chunk reconstruction the dominant frame-time cost.

Before increasing the product world beyond the current 100³ target, profile F9 under dense visible automation. If chunk-build time is dominant, the next renderer phase should retain chunk batch objects and patch/rewrite reusable MultiMesh buffers rather than rebuilding Node3D/MultiMeshInstance3D trees. A lower-level RenderingServer buffer path is also a candidate once normal MultiMesh reuse is exhausted.

## Validation

The performance pass must keep all deterministic generation and replay contracts unchanged. The CI checkpoint after these changes passes content validation, Release compilation, deterministic-generation contracts, and replay-codec contracts.
