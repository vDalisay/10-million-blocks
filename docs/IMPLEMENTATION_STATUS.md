# Implementation Status

Source architecture plan: `docs/IMPLEMENTATION_PLAN.md`

Active tutorial/demo progression: `docs/FUTURE_WORLD_PROGRESSION.md`

Detailed progression implementation plan: `docs/FUTURE_WORLD_PROGRESSION_IMPLEMENTATION_PLAN.md`

## Current checkpoint

The non-local implementation pass is substantially complete. The active Steam-demo sequence is:

1. `tutorial_single_block` — 1 x 1 x 1.
2. `tutorial_dirt_5` — 5 x 5 x 5.
3. `tutorial_lake_core_10` — 10 x 10 x 10.
4. `tutorial_trees_gem_15` — 15 x 15 x 15.
5. `reference_natural` — 20 x 20 x 20 first main-economy world.
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
- replay exit restores the world that was active before the replay was opened;
- persistent player-level special-resource event subscriptions are detached when old skill-tree views leave the scene;
- distinct `STEAM DEMO COMPLETE` flow after the 50³ finale.

A temporary startup main menu now provides **PLAY GAME** and **SETTINGS**. Settings includes a confirmation-gated **CLEAR SAVE DATA** action that removes current/legacy development saves, temp files and replay history so progression can be retested from a genuinely clean state.

The world browser resumes the actual saved run; Replay never silently creates a fresh playable world.

---

## Tutorial progression and completion accounting

Implemented:

- 1³ single-block introduction;
- 5³ manual/hover-mining tutorial;
- 10³ authored lake + stone-core Shovel tutorial;
- 15³ authored tree blockers + exact central red gem + Drill/Wide Bore lesson;
- Forest Cutter remains hidden in 15³ and first becomes visible in the 20³ main world;
- per-world category staging plus exact per-skill staging for late nodes such as Cloud Charger;
- semantic gameplay event hub/bridge;
- contextual `TutorialDirector` driven by semantic events rather than mechanics containing popup text;
- one-time tutorial milestones persisted in save state;
- semantic events for world start/manual/area mining, automation unlock/place/stop, Shovel blockers, special resources, transformations, lightning and meteors.

A completion deadlock discovered in the 5³ tutorial has been fixed at the accounting layer. Exact small worlds no longer use large-world aggregate region quotas. CI now exhaustively clears every tutorial world and proves a real zero-block state at **1/1, 125/125, 1,000/1,000 and 3,375/3,375**. This specifically prevents the previous 105/125 state where the renderer had nothing left to mine while completion still expected 125 blocks.

Mechanics remain functional when tutorial presentation is absent.

---

## Incremental-game feedback layer

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

Authoritative mining, currency, special-resource credit, save state and replay never depend on an animation finishing.

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

---

## Deterministic generation and authoring

Implemented:

- runtime deterministic profile/seed generator;
- coherent terrain columns, plateaus/cliffs, forest fields, ore and depth rules;
- sparse world override files for tutorial/main-world authoring;
- runtime-backed world-authoring candidate browser/preview;
- exact authoring metrics and draft export;
- shipping freeze backend that refuses accidental overwrite of an existing frozen version;
- standalone deterministic-generation tooling can load the same `res://` authored override data through an explicit managed resource root without booting Godot native runtime APIs.

The procedural surface now has an explicit structural post-pass shared by runtime and CI. It enforces visual/voxel invariants rather than hoping a seed produces them:

- water is a **single inset surface layer**, never a stack/tower sitting above another block;
- every accepted water voxel has solid sand directly inward/behind it;
- any outward cap in a water column is carved away;
- water remains inside a stable face band and leaves a dry shoreline ring before cube seams/corners;
- the first cardinal dry ring beside accepted water is sand;
- boundary water is shallow; dark/deep water is allowed only when all four final neighboring water columns survive;
- narrow one/two-cell post-filter water fragments are rejected;
- the literal cube perimeter does not use dirt-sided grass/soil where a second camera angle could expose brown sides; that edge band resolves to the uniform-green surface material instead.

`reference_natural` remains content `worldVersion` 3, while all three reviewed procedural Steam-demo worlds now use structural `generationVersion` 3. The Verdant v3 sparse override was refreshed to generation v3 and still guarantees one red, one blue and one green special gem without adding blocks.

The structural correction intentionally changed the deterministic physical geometry. The reviewed generation-v3 baselines are now:

- `reference_natural`: **6,824** mineable blocks;
- `reference_lakes`: **61,225** mineable blocks;
- `reference_ridges`: **123,412** mineable blocks.

CI confirms those worlds still retain trees and special resources. The current authoring scan reports 3, 811 and 4,595 special gems respectively.

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
- deterministic generation contracts across candidate seeds and all three procedural Steam-demo worlds;
- water inset/support/shoreline/deep-water/topology contracts;
- uniform outer-edge surface-material contract;
- exact tutorial clear-through completion contracts;
- exact reviewed physical-count contracts for 20³/40³/50³;
- authored special-resource/tree presence checks;
- replay schema/encode/decode/compression contract at 125,000 recorded removals.

The standalone contract projects still emit the existing Godot source-generator warning about `GodotProjectDir` not being set when compiled outside a Godot project context. The contract executables themselves complete successfully; this warning is not a runtime or main-project build failure.

---

## Remaining work before merge

Automated/static work is now at the point where further confidence primarily requires the actual Godot runtime and visual interaction:

1. From the temporary main menu, use **Settings -> Clear Save Data**, confirm, then verify Play Game truly starts from the 1³ world with no stale replay/progression state.
2. Clear 1³ and then the entire 5³ world by ordinary clicking/hover mining. Confirm it reaches 125/125, shows the completion/congratulations flow and exposes Replay/next-world behavior rather than becoming empty at 105/125.
3. Verify the 1³ -> 5³ -> 10³ -> 15³ -> 20³ -> 40³ -> 50³ progression can be played/revisited without state leakage.
4. Inspect 20³/40³/50³ from multiple camera angles: water must read as recessed lakes/basins with sand shoreline/support, no blocks may visibly sit on top of water, and cube edge/corner grass must remain uniformly green from adjoining faces.
5. Verify World Browser progress percentages use the real saved physical total and replaying an older world returns to the previously active world afterward.
6. Verify incremental pickups read clearly and F9 feedback counts remain bounded under rapid mining.
7. Verify Cloud Charger, lightning and meteor interaction locally.
8. Verify staged tutorial/skill visibility, especially Forest Cutter at 20³ and Cloud Charger at 40³/50³.
9. Verify Revisit resumes actual state and Replay remains read-only.
10. Verify the 50³ final clear displays the dedicated Steam-demo completion screen.

No additional one-million performance benchmark is required for this progression checkpoint unless a regression is observed there.
