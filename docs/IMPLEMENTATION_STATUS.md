# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

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
- Phase 9 — Completion/progression: **complete; now routes through three normal early-game worlds before the finale**
- Phase 10 — Save/load/offline foundation: **complete for current gameplay state**
- Phase 11 — stress/optimization architecture: **complete enough for now; one-million manual-mining tuning is deliberately deferred**
- Phase 12 — game-feel/reference polish: **major implementation pass complete; ongoing local tuning**
- Phase 13 — final-scale target: **exact 1,000,000 authoritative target implemented**
- Phase 14 — specialized automation/world events: **mechanics implemented; some final art remains replaceable**

The product priority is now the **opening ~2-hour experience**, not additional optimization of the one-million finale. See `docs/EARLY_GAME_PACING.md`.

No merge to `main` should happen before the final local gameplay/visual pass.

---

## Opening progression

Current configured progression:

1. **Verdant Cube** (`reference_natural`) — forgiving forested introduction; manual mining, first Drill, stopped-automation interaction.
2. **Lakebound Cube** (`reference_lakes`) — shoreline/sand routing; Powered Shovel and its intelligence upgrades become useful.
3. **Copper Ridge Cube** (`reference_ridges`) — provisional third authored world; more ridges/exposed rock, intended to emphasize Drill material upgrades and Rock Breaker.
4. **One Million Block World** (`final_target_1m`) — finale, deliberately outside the current tuning priority.

World profiles now carry authored `introText`; the completion screen previews the next world's gameplay role instead of only naming it.

CI guards the existence/order of the three early normal-scale profiles so the opening arc cannot accidentally regress directly into the million-block renderer.

---

## Automation placement / purchase / relocation

All automation placement routes now share one interaction:

- the real automation model is reused as a placement ghost;
- **green** ghost = valid placement;
- **red** ghost = invalid placement;
- LMB commits;
- RMB or Esc cancels;
- moving a stopped automation uses the same preview and moves the existing instance;
- automation anchors cannot be stacked on each other.

First-time automation purchases are transactional. Buying from the Automation drawer or an `unlock_miner` Skill Tree node starts preview **before spending**. Resources are deducted only after a valid placement successfully commits. Cancelling or clicking invalid terrain costs nothing.

---

## Stopped-automation attention interaction

Actionable stops still produce the attention popup and can be cycled/focused.

The visibility locator has been changed after local feedback:

- the previous solid orange x-ray fill was rejected because it obscured the automation's shape;
- the current implementation uses a Godot 4.6 stencil mask plus a pixel-stable expanded silhouette pass;
- only a thick orange/yellow **outer outline** should remain visible through terrain;
- hovering brightens/thickens that outline rather than filling the model;
- LMB on the highlighted stopped automation enters relocation mode;
- the overlay casts no shadows.

This shader still needs the final local Godot visual check because .NET CI cannot compile/render Godot shader code.

---

## Powered Shovel surface ownership

Shovelable terrain includes:

- normal sand;
- `grass`;
- `dirt_grass` / the grass-edged dirt model;
- future blocks carrying the `sand` content tag.

The Shovel is not allowed to remove a tile while something owns/occupies its outward surface:

- deterministic tree feature on that support voxel => blocked;
- a real present voxel immediately outward from that tile => blocked;
- after the obstruction/tree is cleared manually or by the appropriate automation, the tile becomes eligible again.

The same rule is applied both to initial placement validation and to every subsequent Shovel route candidate, so an already-running Shovel cannot silently eat the ground underneath a tree.

Future deterministic decorative rocks/props should plug into this same surface-feature policy instead of becoming one-off Shovel exceptions.

Base Shovel remains ~1 block/sec, cardinal/same-height only. Gearbox, Slope Sensor, Terrain Scout and High-Torque Drive progressively add speed and intelligence.

---

## Primary Drill

The starter Drill:

- advances one depth layer/sec;
- initially cuts ordinary stone only;
- stops on unsupported material rather than skipping it;
- **Hardened Bit** adds dark stone;
- **Ore-Cutting Bit** adds normal copper/silver/gold ore;
- gems/unstable blocks retain specialist/manual interactions;
- reaches a real physical end-of-world condition instead of travelling forever;
- Wide Bore preflights its complete cutter face and stops if any occupied cutter cell is unsupported.

Hidden/back-side automation remains computational where possible; expensive presentation is deferred until relevant to the camera. Further giant-world tuning is postponed while the opening worlds are refined.

---

## Specialized systems already implemented

- Rock Breaker / pickaxe-class automation;
- Forest Cutter / axe-class automation;
- deterministic gem pockets;
- deterministic multi-hit unstable blocks / bounded blasts;
- clumped orbiting clouds;
- adaptive orbit/zoom camera;
- compact HUD + H details;
- scrollable runtime skill tree;
- standalone grid-based skill-tree editor with routed prerequisite lines and rank gates;
- completion overview / Continue flow;
- sparse save/load and offline automation foundation.

---

## Current local gate — early-game only

Do **not** spend the next validation pass benchmarking the one-million world unless an obvious regression appears.

The remaining useful local checks are:

1. Stop a Drill and cycle/focus it from the attention alert. Through covering terrain, the locator should be a thick **outline only**, not a filled orange blob. Hover should only strengthen the border. Clicking it should still enter the green/red relocation ghost.
2. Place/run a Powered Shovel near tree-bearing terrain. Tree support tiles should show invalid placement and should not be chosen by an already-running Shovel. Clear the tree/support obstruction manually or with Forest Cutter and confirm the terrain can then be used.
3. Preview/play progression through Verdant -> Lakebound -> Copper Ridge. Copper Ridge is intentionally provisional: verify that it reads as the more rocky/ridge-heavy third step and creates natural reasons for Hardened Bit, Ore-Cutting Bit and Rock Breaker.
4. Confirm previous transactional buy-and-place, RMB/Esc cancellation, save/load and normal mining/automation interactions have not regressed.

After these pass, remaining work should mostly be **early-game pacing, resource-cost tuning, terrain/art direction and presentation polish**, rather than another architecture rewrite.
