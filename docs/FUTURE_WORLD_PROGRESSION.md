# Future world progression — decision summary

This file is the short design summary. The detailed technical/implementation handoff is:

**`docs/FUTURE_WORLD_PROGRESSION_IMPLEMENTATION_PLAN.md`**

Nothing on `agent/future-world-progression-plan` should change runtime progression. This branch exists to plan the next progression/content implementation while gameplay fixes continue independently on `agent/one-million-squared-plan`.

## Planned shipped progression

1. **1 x 1 x 1** — one cube; teach the base mine/complete loop.
2. **5 x 5 x 5** — mostly dirt; unlock Hover Mining and offer a monetary choice between faster manual mining and the first larger mining footprint.
3. **10 x 10 x 10** — one lake plus a stone core beginning roughly three blocks below the surface; unlock Powered Shovel and teach that water/stone stop it.
4. **15 x 15 x 15** — water + stone core + trees + one authored red gem; unlock Drill; red gem can be consumed once with ordinary resources to permanently transform Drill into Wide Bore.
5. **20 x 20 x 20** — first full generated/main world; persistent main currency begins; unlock Forest Cutter and Rock Breaker.
6. **40 x 40 x 40** — advanced upgrades plus active cloud/lightning and meteor gameplay.
7. **50 x 50 x 50** — Steam demo finale; all visible demo skill-tree nodes can be unlocked; mining every block ends the demo.
8. **100 x 100 x 100** — current full-release destination (~1,000,000 logical addresses). Additional worlds may later be inserted between 50 and 100.

Do not force the progression into a fixed time budget. The desired experience is several hours, but pacing should be tuned from playtests.

## Persistence decisions

Player-bound and permanent:

- skills/unlocks/ranks;
- automation-class unlocks;
- manual-mining upgrades;
- permanent tool transformations;
- special-resource inventory unless consumed;
- world-unlock/completion state.

World-bound:

- mined state;
- physical automation instances and their stop/route state;
- world-event state;
- replay log;
- tutorial-local currency.

Unlocking an automation does not grant its physical copies in later worlds. Every new world starts with zero placed copies; copies must be bought again for a fixed ordinary-resource price. Duplicate price does not scale upward.

Tutorial-world ordinary currency does **not** transfer between 1/5/10/15. Starting at 20 x 20 x 20, ordinary currency becomes the persistent main wallet and carries to later main worlds.

## Revisit vs replay

`Revisit` resumes that world’s real persistent save state.

`Replay` is separate and read-only. It shows a timelapse of the cube being progressively mined; the viewer can move the camera but cannot interfere.

The detailed plan recommends recording authoritative accepted block-removal events rather than camera/input/video data. This is compact, robust against automation-AI changes, and naturally captures manual mining, automation, explosions, lightning and meteors through the same MiningService authority.

## Manual mining direction

Manual click remains one immediate damage application.

Hover Mining:

- unlocked in the 5³ tutorial;
- toggleable in UI;
- continuously mines whatever is hovered without holding a mouse button;
- rate-limited by manual mining cadence;
- moving the cursor immediately moves the active footprint.

Footprint progression:

1. single block;
2. 3x3 plus shape;
3. full 3x3;
4. later 10x10.

Area mining uses **highest surface-layer priority**. Within the footprint, only the front-most/highest layer receives damage on a tick. A three-hit rock sitting above a grass plane must absorb three ticks before the grass around/under it becomes eligible.

## Deterministic authored worlds

Shipped demo/full-release worlds are versioned, hand-approved deterministic worlds.

Build a dedicated World Authoring Tool capable of:

- dimensions/seed/profile editing;
- preview/regeneration;
- candidate seed browsing;
- material/feature metrics;
- slice/cross-section inspection;
- sparse voxel paint/carve overrides;
- tree/feature placement/removal;
- authored gem/special-resource placement;
- undo/redo;
- validation;
- `Freeze for Shipping` with generator version + canonical content hash.

Support Blueprint, Procedural and Hybrid world modes. Runtime must not silently reroll or mutate a frozen shipped world when generator rules improve.

## 40³ active gameplay

Lightning v1:

- player repeatedly clicks an eligible orbiting cloud to charge it;
- once charged, it automatically strikes the point beneath it;
- impact removes a bounded crater through authoritative mining;
- later Cloud Generator and Cloud Charger automation can create/charge clouds.

Meteor v1:

- meteor enters orbit for a limited opportunity;
- player grabs and drags/flicks it into the cube;
- impact creates a bounded crater through authoritative mining;
- later upgrades can improve frequency, capture, targeting, strength and automation.

## Required visual pass

A dedicated reference-look/post-processing phase is part of the demo plan.

The supplied reference still/video remain the visual target. The detailed plan calls for fixed-camera A/B comparisons of Compatibility vs Forward+ capabilities, then staged tuning of materials, lighting, ambient occlusion, tonemapping/color grade, restrained glow, antialiasing and only then a small custom finishing shader if needed.

Post-processing must not be used to disguise poor world generation. Natural terrain zoning, beaches, water depth, forests and plateaus remain structural generation/art requirements.

## Far-future scope

A player-selectable/random/infinite cube generator may be explored after authored demo/full-release progression is complete. It is not part of this implementation plan.
