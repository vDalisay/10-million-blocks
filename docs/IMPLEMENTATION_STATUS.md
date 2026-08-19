# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan/baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete**
- Phase 2 — Reference visual slice: **superseded by the procedural world; reference remains the art target**
- Phase 3 — Virtual world + deterministic generator: **complete**
- Phase 4 — Rendering + picking: **complete architecture; final one-million visual/performance review is local**
- Phase 5 — Manual mining: **complete**
- Phase 6 — Automation framework: **complete**
- Phase 7 — Skill-tree runtime: **complete**
- Phase 8 — Skill-tree editor: **complete**
- Phase 9 — Completion/progression: **complete; one-million world is now the final configured progression world**
- Phase 10 — Save/load/offline foundation: **complete for mined state/skills/miners/aggregate regions**
- Phase 11 — stress/optimization architecture: **complete; previous macro-stream benchmark passed and the product renderer has since changed**
- Phase 12 — game-feel/reference polish: **implementation pass complete; final visual comparison requires local Godot review**
- Phase 13 — final-scale target: **implemented as exactly 1,000,000 authoritative mineable blocks with real-block visible geometry**
- Phase 14 — tool-class automation/world events: **mechanics implemented; some final art remains replaceable**

CI is configured on the draft integration PR and has been run after the major implementation batches. The drill/shovel fixes, real-block full-surface renderer, specialized tools/gems, unstable-block mechanics, and HUD/diagnostic pass all compile successfully in GitHub Actions.

---

## Major direction change: one-million world

The old product-scale prototype used coarse `MacroWorldProxy` cells plus a small local detail patch. That was rejected as the player-facing direction because the world looked like large blocks made from mini blocks rather than one enormous mineable block world.

The new `full_surface` renderer is documented in `docs/ONE_MILLION_WORLD_RENDERING.md`.

For both `stress_1000` (kept as a legacy debug ID so F8 remains useful) and `final_target_1m`:

- logical profile is now a roughly 100-axis one-million-block world rather than a fake 1000^3 visual cube;
- authoritative target is exactly **1,000,000** blocks;
- visible terrain is built from actual supplied block meshes at actual voxel addresses;
- no macro proxy is instantiated;
- initial rendering samples all surface-shell chunks, distributed across frames;
- modified/mined chunks switch to exact exposed-voxel rebuilds so holes/tunnels reveal real interior blocks;
- interior state remains deterministic/sparse until modified;
- the world profiles carry some physical generator headroom so every authoritative region can satisfy its quota without the player running out of generated rock before reaching one million.

The macro renderer remains available only for separate diagnostic experiments using `rendererMode: auto`; it is no longer the intended final-world presentation.

---

## Drill behavior

The primary Drill now follows the requested physical model:

### Base Drill

- approximately one block wide;
- one work tick per second;
- moves exactly one block of depth per tick;
- removes the center block at that depth;
- general `Faster Motors` does not make this primary drill travel faster.

### Wide Bore

- upgrades the **existing primary drill**;
- visibly scales its tangent footprint to 3x3 blocks;
- still advances exactly one depth block per second;
- each depth step clears the full 3x3 slice in the same work tick;
- resetting an existing drill after purchase starts at the tunnel mouth and fills the eight surrounding cells around the old center tunnel.

### Radial Excavator

Radial excavation is now a **separate placeable miner** (`B`) rather than changing the primary drill into a disc. This prevents the earlier tiny-drill/disc-shaped excavation mismatch.

---

## Powered Shovel

Placement was relaxed at the correct boundary: the selected placement voxel itself must be exposed sand (profile sand ID or a block carrying the `sand` tag), but placement no longer rejects valid sand because a cube-face normal tie resolves to a neighboring face.

Base shovel:

- sand-only;
- about one block/second;
- placement tile first;
- cardinal same-height neighbors only;
- stops when that primitive local search is exhausted.

Upgrades:

- **Shovel Gearbox** — repeatable speed increase;
- **High-Torque Drive** — later speed multiplier;
- **Slope Sensor** — allows one block of local height change;
- **Terrain Scout** — nearest-shell-first fallback search up to radius 5 and wakes a previously stuck shovel.

---

## Phase 14 specialized tools/events

### Rock Breaker / pickaxe class

- unlocked from the skill tree;
- placed with `P`;
- ignores unsuitable soft blocks while following its inward path;
- material affinities make it faster on stone, ore and gems;
- currently uses a procedural pickaxe-shaped placeholder presentation so mechanics are not blocked by missing final imported art.

### Forest Cutter / axe class

- unlocked from the skill tree;
- placed with `A` on a deterministic tree-bearing surface voxel;
- searches neighboring surface terrain for the next tree-bearing voxel;
- mines the supporting block, which also removes the deterministic tree feature because its anchor no longer exists;
- uses a procedural axe-shaped placeholder presentation pending final art replacement.

### Gem pockets

Three high-value deterministic deep-block tiers are generated from a broad 3D pocket field plus a per-voxel grain test:

- `gem_green` — commonest/lowest reward;
- `gem_blue` — deeper/rarer;
- `gem_red` — deepest/rarest.

They currently reuse supplied colored block meshes, carry `ore`/`gem` tags, work with the Rock Breaker, and need no per-instance save data because placement is a pure world-seed/address function.

### Unstable blocks / block bombs

- deterministic rare placement in deep rock;
- current visual uses a supplied yellow colored block mesh;
- manual mining requires three hits;
- first two hits leave the block in place and report hit progress;
- third hit detonates a bounded radius-2 sphere;
- automation detonates one immediately on contact rather than silently stepping past a half-damaged bomb;
- every removed voxel still goes through `VirtualWorld.TryMine`, preserving exact global/region accounting and normal progression events;
- manual blast presentation dirties the affected render area and emits mining debris.

Partial bomb hit count is currently session-local. Bomb *placement* and destroyed/mined state remain deterministic/persistent; persisting the 1/3 or 2/3 intermediate hit counter can be added later if that tiny state detail is desired.

---

## Phase 12 polish implemented

- block-aware mining/debris bursts;
- capped presentation bursts for multi-block actions;
- hover breathing and successful-hit pulse;
- smoothed camera interpolation;
- adaptive large-world wheel zoom and hard anti-penetration barrier;
- animated drill rotor/cutter and physical footprint scaling;
- distinct shovel/pickaxe/axe presentations;
- compact lower-left progress HUD with optional `H` details;
- gem and unstable-block feedback;
- scrollable runtime skill-tree graph with purchase feedback;
- completion overlay/Continue transitions;
- clumped voxel clouds with slow world orbit and inward-facing undersides;
- F9 renderer diagnostics now explicitly distinguish `real-block full surface` from macro experiments.

Final visual parity against the supplied reference cannot be judged from repository/CI alone and remains part of the final local review pass.

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
- M: Drill
- N: Powered Shovel
- P: Rock Breaker
- A: Forest Cutter
- B: Radial Excavator
- F8: one-million debug world
- F9: performance diagnostics
- F7: stress benchmark on a large profile
- F10: completion-flow preview

---

## Final local review checklist

### One-million world

1. `F8`, then `F9`: renderer must say `real-block full surface` and macro must be disabled.
2. Allow the initial shell queue to settle; surface should be continuous actual block meshes, not coarse proxy cells.
3. Far/medium/near should all show the same world at different scales.
4. Mine inward and verify newly exposed blocks/tunnel walls remain proper supplied block meshes.
5. Record F9 FPS, queue length and chunk-build average while the shell is initially filling and while mining/orbiting.

### Drill

1. Base Drill: one depth block/second and roughly one-block physical footprint.
2. Buy Wide Bore: same drill should visibly become ~3x3 and clear one 3x3 slice/second.
3. It must remain square; Radial Excavator is placed separately with `B`.

### Shovel

1. Place `N` on a clearly visible sand block; placement must succeed.
2. Base behavior should be cardinal/same-height and roughly one block/second.
3. Verify Gearbox, Slope Sensor and Terrain Scout progressively change only their intended behavior.

### Phase 14

1. `P` Rock Breaker should progress into stone/ore and visibly accelerate on affinity materials.
2. `A` Forest Cutter should only place on a tree-bearing tile and seek other trees after clearing one.
3. Deep mining should eventually expose colored gem pockets with elevated rewards.
4. A yellow unstable block should show hit 1/3, 2/3, then detonate on the third manual hit.

### Regression

- normal Verdant/Lakebound worlds still render/mine correctly;
- skill tree scrolls to all new branches;
- save/load restores existing mined state, miners, skill ranks and world progression;
- completion Continue reaches `final_target_1m` after Lakebound.

After this checklist, remaining changes should be tuning/art-direction fixes rather than another architecture rewrite.
