# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

Future tutorial/demo progression: `docs/FUTURE_WORLD_PROGRESSION.md`

## Current checkpoint

- Phase 0 — Plan/baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete**
- Phase 2 — Reference visual slice: **superseded by procedural worlds; reference remains the art target**
- Phase 3 — Virtual world + deterministic generator: **complete**
- Phase 4 — Rendering + picking: **complete architecture; final art tuning remains iterative**
- Phase 5 — Manual mining: **complete**
- Phase 6 — Automation framework: **complete**
- Phase 7 — Skill-tree runtime: **complete**
- Phase 8 — Skill-tree editor: **complete**
- Phase 9 — Completion/progression foundation: **complete; current provisional authored sequence remains until the future tutorial-world pass begins**
- Phase 10 — Save/load/offline foundation: **complete for current gameplay state**
- Phase 11 — stress/optimization architecture: **complete enough for current scope; one-million manual-mining tuning is deliberately deferred**
- Phase 12 — game-feel/reference polish: **current implementation scope complete**
- Phase 13 — final-scale architecture/target: **current implementation scope complete; exact 1,000,000 target retained as the planned 100³ full-release destination**
- Phase 14 — specialized automation/world events foundation: **current implementation scope complete**

Phase 12-14 are now treated as closed for this implementation branch. Further additions described in `docs/FUTURE_WORLD_PROGRESSION.md` are new future progression/content work, not unfinished Phase 12-14 blockers.

No merge to `main` should happen before the final local gameplay/visual regression pass.

---

## Latest Phase 12 UX/game-feel fixes

### Manual mining pop

A successful manual block removal now spawns a short-lived copy of that block which scales from roughly 0.985 -> 1.12 -> 0.92 before disappearing. This is presentation-only: authoritative mining and chunk rebuilding still happen normally and no persistent per-block node is introduced.

Only the first few blocks of a multi-block manual burst receive the effect so upgraded manual mining cannot create unbounded presentation spam.

### Camera-friendly placement/move cancellation

RMB no longer cancels automation placement or relocation. While a ghost is active:

- LMB commits a valid green placement;
- RMB remains available for normal camera orbit;
- Esc cancels;
- a bottom-right **Cancel** / **Cancel Move** button is shown for mouse-driven cancellation.

If relocation is cancelled, the original stopped automation reappears at its original position/state.

First-time automation purchases remain transactional: resources are charged only after a valid placement successfully commits.

---

## Stopped-automation interaction

There are now two discovery paths.

### Explicit attention flow

The existing automation-stopped alert can cycle/focus actionable stopped machines. The focused machine uses the stencil-backed x-ray silhouette locator so a buried Drill can still be found through terrain. The previous solid orange fill remains rejected; only the border should be visible.

### Ambient world hover

When the player is simply looking around the world, a **visible** actionable stopped automation can be discovered directly:

- moving the mouse over the visible machine gives it the orange outline;
- the outline exists only while it is hovered in this ambient mode;
- LMB selects that stopped automation and enters the same relocation ghost flow;
- ambient detection is visibility/occlusion gated so it is not intended to reveal buried machines — the explicit attention flow still handles those.

---

## Powered Shovel surface rules

Shovelable soft terrain now includes:

- sand;
- vegetated grass surface;
- `dirt_grass` / grass-edged dirt;
- ordinary brown `dirt`.

The three dirt/grass variants share the shovelable content tag so placement and route-following use the same data rule.

Vegetated `grass` now renders with the dirt-backed grass mesh. This keeps the outward face grassy while exposed side/interior faces read as brown soil instead of producing isolated solid-green blocks after nearby mining.

The Shovel still cannot remove a soft-terrain tile while its outward surface is occupied:

- deterministic tree feature => blocked;
- a real outward voxel/physical obstruction => blocked;
- once that obstruction is cleared manually or by the appropriate automation, the tile becomes eligible again.

Future deterministic decorative rocks/props should join this same surface-feature ownership policy rather than becoming one-off Shovel exceptions.

---

## Primary Drill / automation foundation retained

The starter Drill:

- advances one depth layer/sec;
- initially cuts ordinary stone only;
- stops on unsupported material rather than skipping it;
- Hardened Bit adds dark stone;
- Ore-Cutting Bit adds normal copper/silver/gold ore;
- gems/unstable blocks retain specialist/manual interactions;
- reaches a real end-of-world condition instead of travelling forever;
- Wide Bore preflights its complete 3x3 cutter face and stops if an occupied cutter cell is unsupported.

Hidden/back-side automation remains computational where possible; expensive presentation is deferred until relevant to the camera. Further giant-world tuning is postponed while the opening progression is redesigned.

---

## Specialized systems already implemented

- Rock Breaker / pickaxe-class automation;
- Forest Cutter / axe-class automation;
- Powered Shovel with speed/slope/search upgrades;
- deterministic gem pockets;
- deterministic multi-hit unstable blocks / bounded blasts;
- stopped-automation attention/cycling/relocation;
- transactional buy-and-place ghosts;
- clumped orbiting clouds;
- adaptive orbit/zoom camera;
- compact HUD + H details;
- scrollable runtime skill tree;
- standalone grid-based skill-tree editor with routed prerequisite lines and rank gates;
- completion overview / Continue flow;
- sparse save/load and offline automation foundation;
- one-million/full-surface architecture retained for the eventual 100 x 100 x 100 destination.

---

## Future progression direction

The current Verdant -> Lakebound -> Copper Ridge -> million-block sequence remains a temporary runtime sequence only. The planned replacement is documented in `docs/FUTURE_WORLD_PROGRESSION.md` and begins with tiny tutorial worlds before the first fully generated cube:

1. 1 x 1 x 1 single-block introduction.
2. 5 x 5 x 5 dirt tutorial for manual-mining upgrades.
3. 10 x 10 x 10 lake + stone-core tutorial for tool/material restrictions.
4. 15 x 15 x 15 water/stone/trees + first special upgrade resource.
5. 20 x 20 x 20 first full Verdant-style generated world.
6. 40 x 40 x 40 larger world with rare upgrades and planned active lightning/meteor gameplay.
7. 50 x 50 x 50 Steam demo finale.
8. 100 x 100 x 100 current full-release end target, with optional intermediate worlds still undecided.

All shipped worlds are intended to use deterministic authored profiles/seeds that are generated/reviewed ahead of time and committed as predetermined worlds. A player-facing infinite/random cube generator is explicitly far-future scope.

---

## Final local regression gate for this branch

The next local check can stay focused and does not need another one-million performance benchmark:

1. Place the Powered Shovel on green grass, grass-edged dirt and plain brown dirt. All three should be valid when unobstructed.
2. Confirm a tree-bearing/outward-obstructed soft tile remains invalid for the Shovel until cleared.
3. Move a stopped automation: RMB must orbit instead of cancelling; Esc and the bottom-right Cancel Move button must restore the original unit.
4. Buy/place a normal automation: the bottom-right Cancel button and Esc should cancel without spending resources; RMB must remain camera orbit.
5. Without clicking the attention alert, hover a visible stopped automation. It should receive the orange outline only while hovered and LMB should let it be moved.
6. Cycle to a buried stopped automation through the attention alert and verify the existing x-ray outline remains outline-only through terrain.
7. Manually mine a few blocks and verify the new small block-pop effect reads as a quick scale-up rather than a lingering duplicate.
8. Confirm the grass/dirt presentation no longer creates the isolated fully-green interior blocks shown in the previous screenshots.
