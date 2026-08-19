# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by the real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally iterated**
- Phase 4 — Rendering + picking: **small-world path validated; large-world streaming performance validated, LOD appearance in active polish**
- Phase 5 — Manual mining: **complete and locally validated**
- Phase 6 — Automation framework: **implemented and locally iterated**
- Phase 7 — Skill-tree runtime: **implemented**
- Phase 8 — Skill-tree editor: **implemented**
- Phase 9 — World completion/progression: **implemented; transition polish added**
- Phase 10 — Save/load/offline foundation: **implemented; aggregate exhausted regions supported**
- Phase 11 — 1000-scale stress/optimization: **CPU/frame-time target passed; close/far LOD UX awaiting visual validation**
- Phase 12 — Game-feel/reference polish: **in progress; mining, skill-tree and completion feedback improved**
- Phase 13 — final-scale validation: **reframed to the clarified one-million-block total target**
- Phase 14 — tool-class automation/world events: **generic foundation implemented; powered shovel now playable; asset-dependent tool/event work remains**

The user's locally added forest assets, terrain/presentation tuning, UID files, shovel asset and plan
additions remain preserved on this branch.

---

## Clarified scale target

The intended gameplay end goal is **1,000,000 mineable blocks total**.

Current diagnostic meanings:

- `stress_1000` is deliberately pathological at 1000 x 1000 x 1000 logical address dimensions so the
  renderer/state architecture can be abused, but its authoritative mining target is exactly 1,000,000.
- `final_target_1m` is a separate non-progression validation profile with a 1,000,000-block target and
  more plausible 128 x 128 x 128 logical dimensions.
- Neither profile commits the game to a final visual/world dimension. Final progression-world scale is
  still an art/content decision.

---

## Phase 11 performance result

The first local streamed renderer measured approximately 1 FPS and 1.38 seconds per chunk because it
reused the exact small-world algorithm: scan 32^3 voxels, sample each, then repeatedly resample
neighbours through `IsExposed`.

That renderer was replaced with direct surface-column construction. The second local benchmark passed:

```text
duration_s=20.01
generator_probes=418176
generator_avg_us=8.699
probe_batch_max_ms=2.233
minimum_observed_fps=159.0
chunk_build_avg_ms=0.626
chunk_build_last_ms=0.813
stream_loads=5759
stream_unloads=1331
aggregate_blocks_mined=12348
sparse_voxel_overrides=0
exhausted_regions=9
managed_memory_mb=5.3
```

The Phase 11 performance problem is therefore considered solved at the current detail workload. The
remaining large-world issue is presentation/LOD quality rather than raw synchronous chunk cost.

### Stream churn cleanup

The successful benchmark still recorded thousands of stream load/unload requests. Camera focus could
change faster than the old queue drained, so stale queued chunks were repeatedly checked later.

`WorldView` now clears and rebuilds the small pending queue from the **current** desired working set on a
focus transition instead of accumulating obsolete work.

---

## Large-world camera and LOD revision

The 1000-wide stress profile revealed why simply scaling the normal centre-orbit camera does not work:
zooming closer than the world radius puts the camera inside the terrain, while staying outside makes
individual blocks visually tiny.

### Surface-focus camera

`OrbitCameraController` now treats large worlds differently:

- far/medium views continue to orbit the world centre;
- as the user zooms toward the surface, the orbit pivot smoothly moves toward the currently viewed face;
- at close range, camera distance becomes a **surface stand-off** instead of centre distance;
- the camera can therefore inspect blocks from a few block widths away without entering the cube;
- wheel zoom is deliberately finer through the centre-to-surface transition;
- MMB panning becomes finer in surface inspection mode;
- the large-world Near preset enters a surface inspection distance rather than scaling the ordinary
  centre-orbit Near distance.

The normal Verdant/Lakebound camera behavior remains on the existing small-world path.

### Detail working set follows zoom level

Large-world streamed detail is now LOD-aware:

- far view: base detailed radius around the active surface focus;
- transition: radius expands by one chunk;
- close inspection: radius expands by two chunks, bounded by a hard cap;
- depth is still calculated from the authored relief band so valleys/water do not disappear;
- close detail restores deterministic supplied tree models;
- chunk building remains suspended while RMB/MMB is actively held.

### Macro proxy behavior

The coarse macro shell is now treated strictly as a far/movement LOD:

- `stress_1000` macro resolution increased from 24 to 48 cells per face after performance validation;
- `final_target_1m` uses a 40-cell-per-face diagnostic macro resolution;
- while zooming/orbiting the proxy remains immediately available;
- once surface focus is close, the detailed queue is settled and real block chunks are present, the
  coarse macro shell is hidden so a single giant green macro tile cannot fill the close view;
- F9 exposes surface-focus blend, detail radius and macro visible/hidden state.

This makes the pathological stress world useful for inspecting the LOD transition. It does **not** mean
its far proxy is intended as final art.

---

## Phase 12 game-feel work

### Manual mining

- block-aware debris remains capped for multi-block clicks;
- hover highlight now has a subtle breathing pulse;
- successful manual mining adds a short hit pulse to the highlight;
- LMB remains mining/UI only, RMB orbit and MMB pan.

### Runtime skill tree

- open/close has a short fade;
- purchases provide success/failure feedback;
- purchased nodes pulse briefly;
- owned/maxed, prereq-locked and unaffordable states are visually differentiated;
- routed prerequisite lines remain data-driven and now sit on a faint grid matching the editor model.

### Completion overview

- completion overlay fades/scales in;
- Continue fades/scales out before changing world;
- Continue disables immediately to prevent double activation.

These are lightweight feedback additions; sound and broader UI art direction remain later polish.

---

## Phase 14 — specialised automation progress

The generic tool architecture already supports:

- `toolClass`
- optional allowed block tags
- per-tag rate multipliers
- affinity-aware work credits in live and bounded offline simulation
- swappable pure mining patterns including inward line, wide bore, disc and tangential `surface_strip`

### Drill presentation restored

Drill-class miners now use their own procedural drill presentation instead of sharing the locally added
shovel model:

- cylindrical motor housing
- shaft + pointed bit
- rotating three-fin cutting head
- material-aware debris at the working face
- visual advances to the most recently mined block

The supplied KayKit shovel remains reserved for the shovel tool class.

### Powered Shovel is now playable

The previously provisional shovel is wired into runtime progression:

- new skill-tree node `Powered Shovel`
- requires `Resource Sensors`
- unlocks `shovel_miner` and the `surface_strip` pattern
- **N** places an unlocked Powered Shovel on the hovered surface block
- it follows the surface rather than boring inward
- allowed tags keep it focused on surface/soil/sand
- dirt/sand/surface affinity multipliers make it materially faster on its intended block families
- HUD shows drill/shovel lock state and both placement controls

### Asset-dependent Phase 14 work still deferred

The current branch does not contain dedicated axe, pickaxe or gem models. Rather than silently baking
placeholder visuals into final content, these remain pending assets/content direction:

- persistent tree-feature clearing and axe automation
- pickaxe final model/content and stone/ore-specific automation
- deterministic multi-hit bomb blocks and blast presentation
- gem block models and rare procedural pockets

The underlying tool/tag/pattern/state architecture is intended to support them without another miner
framework rewrite.

---

## Next local checkpoint

The performance numbers no longer require another benchmark before ordinary development continues. The
next required local check is now genuinely visual/input-specific because it changes how a giant world is
navigated and switches between two render representations.

### Quick normal-world regression

Run `play_game.bat` and confirm Verdant still launches, RMB orbit/MMB pan work and one LMB mining click
still behaves normally.

### Large-world inspection check

1. Press **F8**, then **F9**.
2. At far range the denser macro shell should still show the whole stress world.
3. Scroll inward slowly. The camera should approach the viewed **surface** rather than eventually pass
   through the cube.
4. Press **3** as a shortcut to the large-world Near inspection distance.
5. F9 should show `surface focus` approaching 1.00 and detail radius increasing as the view gets closer.
6. After the detail queue settles, F9 should report the macro as `hidden`; the visible close patch should
   consist of actual supplied block meshes rather than one giant flat green macro tile.
7. Some trees should return in sufficiently close detailed land patches.
8. RMB drag should remain smooth; macro may temporarily reappear while moving and detail should catch up
   after release.

### New progression/tool check

On the ordinary world, unlock the Powered Shovel path in the skill tree. Verify:

- **M** places the drill and it visibly looks/rotates like a drill;
- **N** places the Powered Shovel and uses the supplied shovel model;
- the shovel travels tangentially across suitable surface terrain rather than following the drill inward;
- the skill tree and completion overview transitions do not throw UI errors.

This is the next point where a local result is actually needed. If it passes, implementation can continue
into deeper Phase 12 polish and the asset-independent parts of Phase 14 without revisiting the streaming
architecture.
