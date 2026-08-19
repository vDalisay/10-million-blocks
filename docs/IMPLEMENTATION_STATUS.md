# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **superseded by the real generator; reference remains the art target**
- Phase 3 — Virtual world data + deterministic generator: **implemented and locally iterated**
- Phase 4 — Rendering + picking: **small-world path validated; large-world streaming performance validated, close-navigation fix awaiting local validation**
- Phase 5 — Manual mining: **complete and locally validated**
- Phase 6 — Automation framework: **implemented and locally iterated**
- Phase 7 — Skill-tree runtime: **implemented; shovel-specific derived search stat added**
- Phase 8 — Skill-tree editor: **implemented**
- Phase 9 — World completion/progression: **implemented; transition polish added**
- Phase 10 — Save/load/offline foundation: **implemented; aggregate exhausted regions supported**
- Phase 11 — 1000-scale stress/optimization: **CPU/frame-time target passed; pathological final visual proxy intentionally deferred**
- Phase 12 — Game-feel/reference polish: **in progress; HUD obstruction reduced and camera controls refined**
- Phase 13 — final-scale validation: **reframed to the clarified one-million-block total target**
- Phase 14 — tool-class automation/world events: **generic foundation implemented; powered shovel crawler + search upgrade implemented; asset-dependent work remains**

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

The current request explicitly allows the final one-million-world visual composition to be revisited
later. Work below therefore fixes navigation correctness without treating the stress macro proxy as
finished art.

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

The performance issue is considered solved at the current detail workload. Remaining giant-world work
is presentation/LOD/navigation quality, not synchronous chunk throughput.

### Stream churn cleanup

The successful benchmark still recorded thousands of stream load/unload requests. Camera focus could
change faster than the old queue drained, so stale queued chunks were repeatedly checked later.

`WorldView` now clears and rebuilds the small pending queue from the **current** desired working set on a
focus transition instead of accumulating obsolete work.

---

## Large-world camera and LOD revision

### What failed in the first surface-focus attempt

The initial surface-focus camera used the world's axis half-extent as though the cube were a sphere.
That is geometrically unsafe: viewed along a diagonal, the cube surface is farther from its centre than
that axis extent. As a result:

- pressing **3** could place the camera inside the stress cube;
- a single wheel notch could jump from an outside overview to an unusable/interior view;
- the nominal MinDistance clamp was not a real collision barrier because it only clamped the control
  distance, not the camera's final position relative to the cube.

### Hard camera barrier now implemented

`OrbitCameraController` now treats penetration prevention as an invariant rather than a tuning value:

- it calculates the actual cube support distance along the current camera ray;
- during the centre-to-surface pivot blend it computes the final camera ray from the panned pivot;
- an expanded axis-aligned cube is used as a hard safety boundary;
- if the requested camera position would remain inside that boundary, the local camera stand-off is
  increased to the exact ray exit distance before rendering the frame;
- this applies at every orbit angle and while panning, including cube diagonals/corners;
- F9 now exposes the resulting face clearance so a penetration regression is observable.

The large-world **Near [3]** preset now requests an actual close inspection stand-off of roughly five
world units rather than a scaled centre-orbit distance. The hard barrier remains authoritative if that
request would be unsafe.

### Adaptive wheel zoom

Large-world mouse-wheel zoom no longer uses one multiplicative factor across the full range.

- far away, a notch may still move by several world units so crossing empty space is not tedious;
- through the centre-to-surface transition, the additive step is reduced;
- once in close inspection, the step scales with the current stand-off and eventually falls to
  fractions of one block per notch;
- zoom-in and zoom-out use the same distance-adaptive delta;
- ordinary Verdant/Lakebound retain the original small-world multiplicative zoom behavior.

This directly addresses the requested rule: **the closer the camera is to the cube, the more gradual
one wheel tick becomes**, while the independent barrier prevents entering the cube regardless of wheel
input.

### Detail working set and macro behavior

The previous LOD work remains in place:

- close inspection expands the bounded streamed-detail radius;
- depth follows the authored relief band;
- supplied tree models return in sufficiently close detailed land patches;
- detailed chunk construction remains suspended during RMB/MMB drag;
- the macro shell is hidden when close detail has settled and returns while moving/catching up;
- the stress macro proxy remains diagnostic/far art and can be redesigned later without changing the
  validated streaming architecture.

---

## Phase 12 game-feel / UI work

### Manual mining and progression feedback already present

- block-aware debris is capped for multi-block clicks;
- hover highlight has a subtle breathing pulse;
- successful manual mining adds a short hit pulse;
- skill-tree purchases provide state feedback and pulse successful nodes;
- completion overlay and Continue use short fade/scale transitions.

### Obstructive left HUD replaced

The old fixed middle-left information panel covered a large part of the world view. `MiningHud` is now a
compact lower-left dock:

- first line: world, blocks remaining, resources and manual mining power;
- second line: miner count/rate, drill state, shovel state and current shovel search radius;
- detailed controls/render diagnostics are hidden by default;
- **H** expands/collapses those details;
- transient mining feedback appears only briefly.

The separate reference-camera harness was also reduced to one narrow top-left control row rather than a
large title/instruction panel. F9 remains opt-in on the right for engineering diagnostics.

Broader final HUD art direction remains Phase 12 polish; the immediate obstruction is removed without
committing to a final visual skin.

---

## Phase 14 — specialised automation progress

The generic tool architecture supports:

- `toolClass`
- optional allowed block tags
- per-tag rate multipliers
- affinity-aware work credits in live and bounded offline simulation
- swappable pure mining patterns including inward line, wide bore, disc and tangential `surface_strip`

### Drill presentation

Drill-class miners use their own procedural presentation:

- cylindrical motor housing;
- shaft + pointed bit;
- rotating three-fin cutting head;
- material-aware debris at the working face;
- visual advances to the most recently mined block.

The supplied KayKit shovel remains reserved for the shovel tool class.

### Powered Shovel crawler revision

The first shovel implementation enumerated one fixed tangential strip from its origin. That happened to
work on the top face but could mine the first block and stall on a side face because generated relief no
longer lined up with that fixed plane.

Shovel-class miners now use a topology-aware surface crawler:

- placement is accepted only on an exposed block matching the shovel's allowed surface/soil/sand tags;
- after mining its current tile, it searches outward in expanding Chebyshev shells around the last tile;
- the base search radius is **1**, so it only chooses a genuinely neighboring valid surface column;
- a candidate must remain exposed, match shovel material tags and stay on the same cube face;
- the candidate must include tangential movement, preventing the shovel from simply following the newly
  exposed block straight inward like a drill;
- one-block relief changes are allowed, so the shovel can crawl over ordinary generated unevenness;
- equally suitable neighbors use deterministic seeded tie-breaking so save/offline replay is stable;
- if no valid neighbor exists, the shovel stops/exhausts as requested.

The pure `surface_strip` pattern remains in the registry for data compatibility/future broad surface
automations, but the Powered Shovel's actual traversal is now dynamic because surface topology cannot be
represented correctly by one static plane.

### New Terrain Scout upgrade

A new skill-tree node **Terrain Scout** follows Powered Shovel:

- base shovel search radius: 1;
- upgraded shovel search radius: 5;
- the wider search is only relevant after nearer shells contain no candidate, so normal connected-surface
  movement remains local;
- an already exhausted shovel is automatically reactivated when the search-radius upgrade is purchased;
- saved exhausted shovels can likewise retry when loading a save that already owns the upgrade.

This implements the proposed "jump to another sand block up to roughly five tiles away when stuck"
behavior without making the base shovel teleport across gaps.

### Asset-dependent Phase 14 work still deferred

The current branch does not contain dedicated axe, pickaxe or gem models. Rather than silently baking
placeholder visuals into final content, these remain pending assets/content direction:

- persistent tree-feature clearing and axe automation;
- pickaxe final model/content and stone/ore-specific automation;
- deterministic multi-hit bomb blocks and blast presentation;
- gem block models and rare procedural pockets.

The tool/tag/pattern/state architecture remains ready for these without another miner framework rewrite.

---

## Next local checkpoint

At this point the remaining uncertainty is genuinely local/visual/input-specific. Another design pass in
code without seeing the camera and crawler would risk tuning around assumptions again.

### Large-world camera check

1. Run `play_game.bat`, press **F8**, then **F9**.
2. Use individual wheel notches while approaching the cube. The notch distance should visibly become
   smaller as the view gets close.
3. Press **3** from any orbit angle, including a diagonal/corner view. It must stay outside the cube.
4. Continue scrolling inward. F9 `clearance` must never collapse through the surface; at close range
   individual wheel notches should be fractions/small multiples of a block rather than giant jumps.
5. RMB orbit and MMB pan while close. The safety rule should remain true.

### Powered Shovel check

1. Place one shovel on a top surface and one on a side surface.
2. Both should crawl from their first mined tile into neighboring valid dirt/sand/surface tiles.
3. They should not turn inward and behave like drills.
4. Let a shovel reach a disconnected/stuck patch: it should stop when search radius is 1.
5. Buy **Terrain Scout**. An exhausted shovel should wake up and, if a valid patch exists within five
   tiles, jump to it and continue.

### UI check

The old large middle-left information rectangle should be gone. The lower-left HUD should remain compact
until **H** is pressed, and the camera controls should occupy only one small row at the top-left.

If these three checks pass, development can continue without another camera/shovel architecture change;
the next work is deeper Phase 12 polish plus whichever Phase 14 assets/content become available.
