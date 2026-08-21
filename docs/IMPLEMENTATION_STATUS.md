# Implementation Status

Source architecture plan: `docs/IMPLEMENTATION_PLAN.md`

Active tutorial/demo progression: `docs/FUTURE_WORLD_PROGRESSION.md`

Detailed progression implementation plan: `docs/FUTURE_WORLD_PROGRESSION_IMPLEMENTATION_PLAN.md`

## Current checkpoint

The non-local implementation pass is now substantially complete. The active runtime progression is no longer the old provisional sequence; it is the reviewed Steam-demo sequence:

1. `tutorial_single_block` — 1 x 1 x 1.
2. `tutorial_dirt_5` — 5 x 5 x 5.
3. `tutorial_lake_core_10` — 10 x 10 x 10.
4. `tutorial_trees_gem_15` — 15 x 15 x 15.
5. `reference_natural` — 20 x 20 x 20 first normal/main-economy world.
6. `reference_lakes` — 40 x 40 x 40 active lightning/meteor world.
7. `reference_ridges` — 50 x 50 x 50 Steam-demo finale.

The 100 x 100 x 100 one-million world remains a full-release/debug destination and is intentionally outside Steam-demo progression.

The latest non-local CI checkpoint passes content/progression validation, Release compilation, deterministic generation contracts and replay-codec contracts. The remaining gate is deliberately local: gameplay feel, presentation and end-to-end Godot regression.

Do **not** merge this branch to `main` until the final local gameplay/visual regression pass is complete.

---

## Progression/save/economy

Implemented:

- stable world IDs and deterministic generation versions;
- tutorial-local wallets for the first four worlds;
- persistent main wallet beginning at 20³;
- player-bound skill/special-resource progression;
- world-bound mined state and physical automation instances;
- revisit-safe per-world save slots;
- completed-world state;
- save schema 3 with normalization/migration support for the development lineage;
- exact initial physical block totals persisted per visited world so the world browser reports real procedural-world progress rather than relying on optional authored target metadata;
- compact authoritative replay recording;
- read-only completed-run replay viewer with accelerated playback;
- world browser separating **Revisit** from **Replay**;
- replay exit restores the world that was active before the replay was opened, including when replaying a different completed world from the browser;
- persistent player-level special-resource event subscriptions are detached when old skill-tree views leave the scene, preventing stale world-session UI callbacks after revisits/replays;
- distinct `STEAM DEMO COMPLETE` flow after the 50³ finale.

The world browser resumes the actual saved run; Replay never silently creates a fresh playable world.

---

## Tutorial progression and semantic event layer

Implemented:

- 1³ single-block introduction;
- 5³ manual/hover-mining tutorial;
- 10³ authored lake + stone-core Shovel tutorial;
- 15³ authored tree blockers + exact central red gem + Drill/Wide Bore lesson;
- Forest Cutter remains hidden in 15³: that world first teaches that trees are physical route blockers that can be cleared manually; the Forest Cutter branch becomes visible in the 20³ main world;
- per-world category staging plus exact per-skill staging for late nodes such as Cloud Charger;
- semantic gameplay event hub/bridge;
- contextual `TutorialDirector` driven by semantic events rather than mechanics containing popup text;
- one-time tutorial milestones persisted in save state;
- semantic events for world start/manual/area mining, automation unlock/place/stop, Shovel blockers, special resources, transformations, lightning and meteors.

Mechanics remain functional when tutorial presentation is absent.

---

## Incremental-game feedback layer

The game now has an explicit incremental presentation architecture rather than relying only on raw HUD numbers.

Implemented:

- prominent current-world **Blocks Mined** counter;
- separate ordinary **Resources** counter;
- separate dynamic special-resource counters;
- abbreviated large-number formatting;
- immediate authoritative counter updates followed by presentation-only pulses;
- flying pickups from visible mined locations to the appropriate HUD destination;
- cached miniature renders of the actual block meshes;
- deterministic tree-miniature feedback when a tree-bearing support tile is harvested;
- stronger special-resource/gem feedback;
- short aggregation buckets for rapid repeated gains;
- hard caps on active/spawned-per-frame pickup presentation;
- pooled/reused pickup controls;
- off-screen/offline mining avoids fake world-position pickup flights;
- replay sessions do not instantiate the incremental pickup layer;
- F9 exposes active/pool/spawned/aggregated/dropped feedback metrics.

Authoritative mining, currency, special-resource credit, save state and replay never depend on an animation finishing. Dropping a presentation effect is therefore harmless.

Final easing, sound, exact particle art, screen shake and accessibility intensity/reduced-motion controls remain demo-polish work rather than simulation dependencies.

---

## Manual mining and automation

Implemented and retained:

- click mining and controlled Hover Mining;
- `single`, `plus_3` and `square_3` manual footprints with highest-layer resolution;
- Powered Shovel with soft-surface material rules, tree/obstruction blocking, speed upgrades, Slope Sensor and Terrain Scout;
- starter Drill fixed to one depth layer/second;
- starter Drill initially cuts ordinary stone only;
- Hardened Bit adds dark stone;
- Ore-Cutting Bit adds ordinary ore;
- unsupported Drill material stops the machine rather than being skipped;
- actionable stopped-automation alert/focus/relocation flow;
- Wide Bore transforms current/future Drills into a 3 x 3 cutter and preflights the whole face for blockers;
- real end-of-world termination for Drill traversal;
- Rock Breaker material-specialized automation;
- Forest Cutter tree-seeking automation;
- per-world physical unit purchasing separated from permanent class unlocks;
- transactional buy-and-place cancellation;
- hidden/back-side large-world automation remains computational where possible, with deferred visual catch-up.

---

## Active world events and incremental transition to automation

Implemented:

- deterministic clumped/orbiting cloud presentation;
- five-click charged lightning strike;
- authoritative bounded lightning crater through `MiningService`;
- deterministic catchable meteor windows;
- drag/flick meteor interaction with assisted impact targeting;
- authoritative bounded meteor crater;
- save/restore of cloud charge, orbit phases and meteor opportunity state;
- semantic lightning/meteor events;
- late-game **Cloud Charger** skill staged only in the 40³/50³ worlds;
- Cloud Charger contributes one automatic cloud charge every three seconds while manual clicks still accelerate the same cloud.

This is deliberately partial automation: the 40³ active mechanic remains useful manually, while late progression demonstrates the incremental-game shift from repeated interaction toward automation.

---

## Deterministic generation and authoring

Implemented:

- runtime deterministic profile/seed generator;
- coherent terrain columns, plateaus/cliffs, forest fields, ore and depth rules;
- water basin/depth/shore material rules;
- generated water kept away from unstable cube-face seam transitions;
- water materialization requires coherent local 2D basin support rather than one-cell/tendril noise;
- deterministic generator CI contracts for supported terrain, shoreline sand, deep-water interior behavior and water-component shape;
- sparse world override files for tutorial/main-world authoring;
- `reference_natural` is now an explicitly versioned reviewed world (`worldVersion` 3) with a sparse v3 override guaranteeing one red, one blue and one green special gem without increasing its physical block count;
- runtime-backed world-authoring candidate browser/preview;
- exact authoring metrics and draft export;
- shipping freeze backend that refuses accidental overwrite of an existing frozen version;
- standalone deterministic-generation tooling can load the same `res://` authored override data through an explicit managed resource root without booting Godot native runtime APIs.

CI validates the reviewed progression order/dimensions/wallet scopes, staged skill IDs, tutorial authored structures, one-million target invariants and deterministic generation contracts. The current reviewed procedural baselines remain exactly 7,728 mineable blocks for 20³ metadata, 64,611 for 40³ and 125,934 for 50³. CI also confirms those worlds retain trees and special resources; the current authoring scan reports 3, 811 and 4,595 special gems respectively.

---

## Rendering/performance architecture retained

- small/demo worlds use normal authored-scale exact rendering rather than inheriting unnecessary million-world complexity;
- one-million destination uses real-block full-surface rendering;
- camera-dependent surface culling;
- deterministic generated-sample cache;
- deferred off-screen automation presentation;
- sparse authoritative state;
- F9 render/state/automation/feedback diagnostics;
- F7 stress benchmark for large profiles.

The known expensive path for any future one-million visible high-rate automation tuning remains exact modified-chunk rebuild. That is intentionally not a Steam-demo blocker unless local regression shows it affects the reviewed 20/40/50 worlds.

---

## Automated checkpoint

The final non-local checkpoint currently passes:

- repository content validation;
- cross-world progression contracts;
- Release .NET build with 0 warnings / 0 errors in the main game project;
- deterministic generation contracts for candidate seeds and all three procedural Steam-demo worlds;
- exact reviewed physical-count contracts for 20³/40³/50³;
- authored special-resource presence checks;
- replay schema/encode/decode/compression contract at 125,000 recorded removals.

The standalone contract projects still emit the existing Godot source-generator warning about `GodotProjectDir` not being set when they compile outside a Godot project context. The contract executables themselves complete successfully; this warning is not a runtime or main-project build failure.

---

## Remaining work before merge

Automated/static work is now at the point where further confidence primarily requires the actual Godot runtime and visual interaction. Remaining genuinely subjective/runtime items are concentrated in one final pass:

1. Verify the 1³ -> 5³ -> 10³ -> 15³ -> 20³ -> 40³ -> 50³ progression can be played/revisited without state leakage.
2. Verify World Browser progress percentages use the real saved physical total on procedural worlds and that replaying an older world returns to the previously active world afterward.
3. Verify incremental pickups read clearly: normal block, rapid area mining, automation aggregation, tree miniature and special gem destination.
4. Verify F9 feedback counts remain bounded under rapid automation/event mining.
5. Verify Cloud Charger starts after purchase, adds one charge about every three seconds, coexists with manual cloud clicks and triggers the normal authoritative strike.
6. Verify lightning/meteor interaction feel and crater presentation locally.
7. Verify staged tutorial/skill visibility at each world boundary, especially Forest Cutter first appearing at 20³ and Cloud Charger appearing at 40³/50³.
8. Verify Revisit resumes actual state and Replay remains read-only.
9. Verify the 50³ final clear displays the dedicated Steam-demo completion screen.
10. Perform the final visual/art-direction comparison of 20³/40³/50³ against the supplied reference target.

No additional one-million performance benchmark is required for this progression checkpoint unless a regression is observed there.
