# Early-game pacing: first three worlds

This is the current product priority before further work on the one-million-block finale.

The target is an opening arc of roughly two hours, but **world clear times are not yet considered calibrated**. The three worlds below are deliberately normal-scale/eager-rendered profiles so iteration can focus on progression, automation decisions and game feel rather than final-scale renderer constraints.

## World 1 — Verdant Cube

**Role:** teach the base loop and first automation in a forgiving environment.

The player should leave this world understanding:

- orbit / hover / manual mining;
- resources and the skill tree;
- buying an automation only commits its cost after valid placement;
- moving a stopped automation rather than abandoning it;
- the base Drill is intentionally limited and can be blocked;
- trees are meaningful surface obstacles rather than decoration that every tool ignores.

Terrain direction:

- strongest forest identity of the opening worlds;
- mixed grass/dirt and readable stone exposure;
- some water, but not enough to make shoreline navigation the dominant challenge;
- generous visible surfaces for manual mining and first placements.

Progression pressure should be light. The player is learning why an automation stopped, not yet managing a large fleet.

## World 2 — Lakebound Cube

**Role:** introduce surface-routing constraints and make the Powered Shovel's progression legible.

The player should encounter:

- larger water bodies and shoreline sand;
- broken-up surface patches where a primitive Shovel eventually stops;
- trees that must be cleared before the Shovel can consume their support tile;
- obvious reasons to purchase Shovel Gearbox, Slope Sensor and eventually Terrain Scout;
- multiple stopped automations so the attention/cycle/move interaction becomes useful rather than tutorial-only.

Terrain direction:

- more water and shallow/deep-water contrast than Verdant;
- sand concentrated around shorelines;
- enough height changes to make Slope Sensor visibly useful;
- surface obstacles should create decisions without turning every Shovel route into a dead end after one block.

## World 3 — Copper Ridge Cube

**Role:** transition from surface tools into rock/ore specialization before the finale.

This is currently a provisional authored profile and should be tuned from playtesting rather than treated as final art direction.

The player should encounter:

- more exposed stone and dark stone;
- steeper ridges and less water/forest dominance;
- Drill blockers often enough that Hardened Bit has a clear purpose;
- ore veins often enough that Ore-Cutting Bit and Rock Breaker become attractive;
- enough remaining surface terrain that the Shovel/Forest Cutter are not made obsolete instantly.

The third world should feel like a synthesis test: the player now has several automation classes, sees why they specialize, and has enough resource income to make choices between upgrading an existing machine and unlocking another tool.

## Automation interaction rules for all three worlds

- New automation purchase enters placement preview first.
- Ghost model is green when placement is valid and red when invalid.
- Resources are deducted only after a valid LMB placement commits successfully.
- RMB or Esc cancels placement for free.
- Actionable stopped automations appear in the attention flow.
- Cycling to one shows a thick stencil-backed x-ray **outline only**; terrain must never turn the whole machine into a solid orange silhouette.
- Hovering the stopped automation brightens/thickens the outline; LMB picks it up for relocation.
- Relocation uses the same green/red ghost and does not purchase a second machine.

## Surface-tool ownership rules

The Powered Shovel is a terrain remover, not an all-purpose prop remover.

A shovelable terrain tile is still blocked while it carries a deterministic tree or another physical outward obstruction. The obstruction must be cleared first manually or by the appropriate automation. Once the support tile no longer owns that feature, the Shovel may traverse it normally.

This rule should be extended to future deterministic rocks/props through the same surface-feature policy rather than by adding one-off Shovel exceptions.

## What to measure in the eventual pacing playtest

For each world, record:

- real clear time;
- manual blocks vs automated blocks;
- first automation purchase time;
- number of automation stops and relocations;
- resources at world completion;
- skill ranks purchased before transition;
- which upgrades felt mandatory versus optional;
- any period longer than ~1–2 minutes where the player has no meaningful decision besides repeated clicking/waiting.

Do not balance by simply making later worlds larger. The intended escalation is primarily **more interesting material/terrain constraints and more automation decisions**, with world size increasing only enough to support that progression.
