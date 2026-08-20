# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan/baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete**
- Phase 2 — Reference visual slice: **superseded by the procedural world; reference remains the art target**
- Phase 3 — Virtual world + deterministic generator: **complete**
- Phase 4 — Rendering + picking: **architecture complete; final local visual review remains**
- Phase 5 — Manual mining: **complete**
- Phase 6 — Automation framework: **complete; off-screen computation/presentation split added**
- Phase 7 — Skill-tree runtime: **complete; drill material capability progression added**
- Phase 8 — Skill-tree editor: **complete**
- Phase 9 — Completion/progression: **complete; one-million world is the final configured progression world**
- Phase 10 — Save/load/offline foundation: **complete for mined state/skills/miners/aggregate regions; miner stop state persists**
- Phase 11 — stress/optimization: **architecture complete; current automation/render optimizations await final local measurement**
- Phase 12 — game-feel/reference polish: **implementation pass complete; final visual comparison requires local Godot review**
- Phase 13 — final-scale target: **exactly 1,000,000 authoritative mineable blocks with real-block visible geometry**
- Phase 14 — tool-class automation/world events: **mechanics implemented; final imported art remains replaceable**

The remaining gate is one final local runtime/visual/performance pass. No merge should happen before that pass.

---

## One-million world renderer

The old product-scale macro-cell prototype was rejected. `stress_1000` and `final_target_1m` use the `full_surface` renderer:

- authoritative target is exactly **1,000,000** blocks;
- supplied block meshes represent real voxel addresses;
- no macro proxy is used for product-scale worlds;
- surface shell generation is distributed across frames;
- modified visible chunks rebuild exact exposed voxels so tunnels reveal real interior blocks;
- untouched/interior state remains deterministic and sparse.

### View-dependent full-surface culling

Resident shell chunks no longer imply drawable shell chunks. The full-surface renderer now periodically classifies loaded chunk roots against the camera:

- only cube faces oriented toward the camera remain visible;
- back-side chunk roots are disabled before their MultiMeshes are submitted;
- corner chunks stay visible if any of their shell faces is camera-facing;
- modified interior chunks inherit their nearest outward cube face;
- chunk data stays resident, so orbiting does not cause a regeneration spike merely to redisplay another side.

F9 reports `presented/culled` chunk counts so this can be measured locally.

### Deterministic generated-sample cache

Exact modified-chunk rebuilds used to re-run expensive terrain noise for the same voxel and its six neighbours repeatedly. `VirtualWorld` now has a fixed-size direct-mapped cache for generated/reclassified voxel samples:

- mined state is checked before the cache, so mining needs no cache invalidation;
- generated terrain is deterministic, making cached source samples safe;
- cache size is bounded and cannot grow with the million-block address space;
- neighbouring `IsExposed` checks can reuse the same generated samples instead of re-evaluating fractal noise.

F9 reports cache hits, misses, and hit rate.

---

## Automation simulation vs presentation

Large-world automation now deliberately separates authoritative simulation from presentation.

When a drill/shovel/pickaxe/axe changes a block:

1. world state, rewards, counters and save state update normally;
2. if the automation is on the far side, outside the current rendered working set, or already deep inside an unseen part of the cube, no immediate mesh rebuild or debris effect is produced;
3. affected chunk IDs are coalesced into a deferred set instead of rebuilding hidden geometry every automation tick;
4. when the player later faces that side, deferred modified chunks are promoted to exact rebuilds and catch up to current authoritative state;
5. automation model nodes themselves are hidden when they cannot contribute pixels, and invisible drill rotors are not animated.

This is the intended scaling rule: **simulation continues everywhere; presentation only pays for what can currently be seen.**

F9 exposes:

- automation presentation updates queued;
- updates suppressed as invisible;
- deferred automation chunk count;
- resident vs presented/culled render chunks.

---

## Primary Drill behavior

### Base Drill

- one-block footprint;
- one depth step per second;
- general `Faster Motors` does not accelerate its travel speed;
- initially mines **ordinary stone only**;
- it does not skip an unsupported block;
- hitting unsupported material stops the machine at that exact coordinate;
- the configured safety range is now larger than all current worlds, so normal termination is the physical empty boundary rather than an arbitrary short range.

### Drill material progression

New skill branch:

- **Hardened Bit** — adds dark stone capability;
- **Ore-Cutting Bit** — adds normal copper/silver/gold ore capability;
- gems and unstable/bomb blocks still stop the ordinary Drill, preserving reasons to use specialised tools and interact with world events.

If a stopped drill's blocker becomes supported after buying a bit upgrade, it resumes automatically.

If the player manually removes the blocking voxel with another tool, the stopped drill detects that at the low-frequency automation visibility tick and resumes automatically.

### Wide Bore

- upgrades the existing primary Drill;
- scales its physical cutter footprint to 3x3;
- preflights the full cutter face at each depth;
- any unsupported present block in that 3x3 face stops the entire machine instead of being skipped;
- a work step clears the supported 3x3 slice, then advances one depth layer.

### End-of-world stop

The drill now distinguishes a real terminal condition from a material blocker. When its next depth position is outside/empty after the tunnel reaches the physical world boundary, it enters `RangeComplete` and no longer consumes work or advances forever.

---

## Stopped-automation attention flow

Actionable automation stops now produce a compact clickable alert.

The alert:

- appears when a Drill is blocked by unsupported material, a Shovel runs out of reachable terrain, or a Forest Cutter has no reachable tree target;
- shows the stop reason/material;
- when one automation needs attention, clicking focuses that automation;
- when several need attention, repeated clicks cycle through/focus them;
- focuses a blocked Drill at its visible tunnel entrance so the player can inspect/unblock its path;
- disappears automatically after all actionable automations resume or are otherwise resolved.

Normal `RangeComplete` Drill termination is not treated as an error/attention condition.

Miner stop reason, blocker voxel and blocker material are included in miner snapshots so save/load does not lose why a machine was stopped.

---

## Powered Shovel

Valid shovel terrain now includes:

- normal `sand`;
- the profile's grass-edged dirt/surface-edge block (`dirt_grass`);
- any future block carrying the `sand` content tag.

`dirt_grass` itself now carries the `sand` tag so the rule is also visible in data instead of existing only as a special-case code path.

Base shovel remains deliberately primitive:

- about one block/second;
- placement tile first;
- cardinal same-height neighbours only;
- stops when that local search is exhausted.

Upgrades remain:

- **Shovel Gearbox** — repeatable speed increase;
- **High-Torque Drive** — later speed multiplier;
- **Slope Sensor** — one block of local height change;
- **Terrain Scout** — nearest-shell-first fallback up to radius 5 and wakes a stuck shovel.

---

## Phase 14 specialised tools/events

### Rock Breaker

- placeable pickaxe-class automation;
- skips unsuitable soft terrain;
- specialises in stone, ore and gem tags;
- uses material affinity rate bonuses;
- current presentation is a procedural placeholder until final imported art exists.

### Forest Cutter

- placeable only on deterministic tree-bearing surface voxels;
- searches neighbouring surface terrain for other tree anchors;
- clearing the support block also removes the deterministic tree feature;
- current presentation is a procedural placeholder until final imported art exists.

### Gem pockets

Deterministic deep pockets provide `gem_green`, `gem_blue`, and `gem_red` reward tiers. Placement is a pure function of world seed/address and therefore requires no per-gem save objects.

### Unstable blocks

- rare deterministic deep placement;
- manual hit 1/3 and 2/3 leave the block in place;
- hit 3/3 triggers a bounded radius-2 blast;
- every removed block still passes through authoritative world accounting;
- destroyed/mined state persists normally.

Partial 1/3 or 2/3 bomb-hit progress remains session-local and is not a milestone blocker.

---

## Current controls

- LMB: mine / UI
- RMB drag: orbit
- MMB drag: pan
- wheel: adaptive zoom
- 1 / 2 / 3: camera presets
- F: recenter
- K: skill tree
- H: HUD details
- A: automation menu
- M: Drill menu
- N: Powered Shovel menu
- P: Rock Breaker menu
- C: Forest Cutter menu
- F8: one-million debug world
- F9: performance diagnostics
- F7: stress benchmark
- F10: completion-flow preview

---

## Final local review checklist

### 1. Build/regression

- `play_game.bat` builds and launches without errors;
- Verdant/Lakebound still render and mine correctly;
- save/load restores mined state, skill ranks, miners, stopped-miner state and progression;
- runtime skill tree scrolls to the new Drill bit and Shovel branches.

### 2. One-million performance

1. Press `F8`, then `F9` and allow initial shell population to settle.
2. Record baseline FPS with no automation.
3. Place a Drill on the visible face and record FPS while it works.
4. Orbit so that Drill is on the far side. FPS should no longer collapse merely because it keeps mining.
5. F9 `presented/culled` should show a substantial portion of resident shell chunks culled.
6. While the Drill is hidden, `automation presentation ... suppressed` should increase and deferred chunk count should remain bounded/coalesced rather than growing once per mined block.
7. Orbit back toward the Drill. Deferred chunks should drain/catch up and the visible tunnel should represent the current mined state.
8. Generated sample cache hit rate should become meaningful during exact rebuilds; compare `chunk build ms` against the previous ~15-FPS automation behavior.

### 3. Drill blockers/end

1. Base Drill should mine ordinary `stone` but stop on `stone_dark`, ore, gem or bomb.
2. A top-right `AUTOMATION STOPPED` alert should name the blocking material.
3. Click the alert; it should focus the stopped Drill. With multiple stopped machines, repeated clicks should cycle them.
4. Manually remove the blocker using an appropriate tool; the Drill should resume without re-placement.
5. Alternatively buy **Hardened Bit** for dark stone or **Ore-Cutting Bit** for normal ore; a compatible stopped Drill should resume.
6. Let a Drill reach the physical end of the cube; it should stop permanently rather than continue moving/working forever.
7. Wide Bore should stop if any supported cutter-face path is blocked by unsupported material.

### 4. Shovel

1. Place on normal sand.
2. Place on a grass-edged dirt (`dirt_grass`) patch; this must now also succeed.
3. Base behaviour should remain cardinal/same-height and ~1 block/sec.
4. Verify Gearbox, Slope Sensor and Terrain Scout alter only their intended properties.

### 5. Remaining Phase 14 presentation

- Rock Breaker progresses through stone/ore/gems;
- Forest Cutter seeks deterministic trees;
- gem rewards appear when deep pockets are exposed;
- unstable block reports 1/3, 2/3, then detonates on 3/3.

After this checklist, remaining work should be visual/art-direction tuning rather than another renderer/automation architecture rewrite.
