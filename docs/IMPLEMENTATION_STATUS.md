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
- one persistent ordinary-resource wallet across tutorial and main worlds, per the later progression decision that resources should follow the player between worlds;
- migration/normalization folds any obsolete tutorial-local balances into the persistent wallet exactly once;
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

The startup main menu now distinguishes **START GAME** from **CONTINUE** and provides presentation settings plus a confirmation-gated **CLEAR SAVE DATA** action. Clearing progression removes current/legacy development saves, temp files and replay history while keeping presentation preferences.

An in-game Esc pause menu freezes the simulation and provides Resume, presentation settings and **Save & Return to Main Menu**. Explicit navigation now requires the current world save to reach disk successfully; a failed save keeps gameplay open instead of pretending the transition succeeded.

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
- tutorial messages queue instead of overwriting one another when several semantic events fire close together;
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
- camera-space multi-raycast footprint resolution so area-hover highlighting is centered on the block actually under the cursor from the player's view;
- `single`, `plus_3` and `square_3` manual footprints with highest-layer resolution;
- hover-mined blocks use the stronger 124% pop while ordinary click mining keeps its smaller pop;
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
- clouds fade gradually relative to camera/screen position so clouds crossing the central play area become less obstructive and regain full opacity toward the sides;
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

The procedural surface has an explicit structural post-pass shared by runtime and CI. It enforces visual/voxel invariants rather than hoping a seed produces them:

- water is a **single inset surface layer**, never a stack/tower sitting above another block;
- every accepted water voxel has solid sand directly inward/behind it;
- any outward cap in a water column is carved away;
- water remains inside a stable face band and leaves a dry shoreline ring before cube seams/corners;
- the first cardinal dry ring beside accepted water is sand;
- boundary water is shallow; dark/deep water is allowed only when all four final neighboring water columns survive;
- narrow one/two-cell post-filter water fragments are rejected;
- procedural material choice remains authoritative at cube seams: sand and stone stay sand/stone, interior grass keeps its dirt-backed/fringed appearance, and only the specifically authored dirt/grass edge case on the true perimeter resolves to the clean green edge appearance.

`reference_natural` remains content `worldVersion` 3, while all three reviewed procedural Steam-demo worlds use structural `generationVersion` 3. The Verdant v3 sparse override was refreshed to generation v3 and still guarantees one red, one blue and one green special gem without adding blocks.

The structural correction intentionally changed the deterministic physical geometry. The reviewed generation-v3 baselines are:

- `reference_natural`: **6,824** mineable blocks;
- `reference_lakes`: **61,225** mineable blocks;
- `reference_ridges`: **123,412** mineable blocks.

CI confirms those worlds still retain trees and special resources. The current authoring scan reports 3, 811 and 4,595 special gems respectively.

---

## Loading/rendering/performance architecture

- world changes, revisits and replay transitions use a persistent space-background loading screen with a pulsing block;
- exact demo-world chunk creation is staged across process frames instead of rebuilding the complete initial view synchronously;
- loading progress remains visible until the replacement `WorldView` has resolved its initial presentation set;
- transition requests are serialized so double-clicks cannot start competing world loads;
- abandoned/failed transitions release the loading overlay instead of leaving it stuck over a still-valid scene;
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

## Replay quality/integrity

- replay playback is read-only and visually recreates mining block pop/debris without granting replay rewards;
- replay timing is sequential rather than preserving idle gaps, with 1x equal to one recorded removal per second;
- playback speed is continuously selectable from 1x to 64x, with 1/2/4/8/16/32/64x presets;
- codec schema/version/world identity/checksum validation is enforced;
- corrupt replay payload lengths are validated before allocation/read;
- decompression is bounded to the declared payload length to prevent malformed local replay files from expanding without limit;
- CI includes a malformed oversized-header fixture in addition to checksum/future-schema rejection and the 125,000-removal compression round-trip.

---

## Quality-review pass

The latest static/code-quality review specifically hardened failure paths that normal happy-path gameplay does not exercise often:

- loading transitions no longer rethrow exceptions from an unobserved fire-and-forget task;
- the loading overlay survives destruction of the UI control that initiated a scene transition;
- stale World Browser requests cancel cleanly and reopen the browser rather than covering the scene with a loader;
- Revisit/Replay require a successful save before leaving the active world;
- a failed Revisit/Replay attempts to restore the previously active world;
- completion-screen transition controls remain usable if a replay/next-world transition fails;
- **Save & Return to Main Menu** no longer leaves gameplay when its save write fails;
- replay decoder allocations/decompression are bounded and contract-tested.

---

## Automated checkpoint

The final non-local checkpoint currently passes:

- repository content validation;
- cross-world progression contracts;
- Release .NET build with 0 warnings / 0 errors in the main game project;
- deterministic generation contracts across candidate seeds and all three procedural Steam-demo worlds;
- water inset/support/shoreline/deep-water/topology contracts;
- current cube-edge material contract;
- exact tutorial clear-through completion contracts;
- exact reviewed physical-count contracts for 20³/40³/50³;
- authored special-resource/tree presence checks;
- replay schema/encode/decode/compression contract at 125,000 recorded removals;
- malformed replay header/checksum/future-schema rejection.

The standalone contract projects still emit the existing Godot source-generator warning about `GodotProjectDir` not being set when compiled outside a Godot project context. The contract executables themselves complete successfully; this warning is not a runtime or main-project build failure.

---

## Remaining work before merge

Automated/static work is now at the point where further confidence primarily requires the actual Godot runtime and visual interaction:

1. From the startup menu, use **Settings -> Clear Save Data**, confirm, then verify Start Game truly begins from the 1³ world with no stale replay/progression state.
2. Clear 1³ and then the entire 5³ world by ordinary clicking/hover mining. Confirm it reaches 125/125, shows the completion/congratulations flow and exposes Replay/next-world behavior rather than becoming empty at 105/125.
3. Verify the 1³ -> 5³ -> 10³ -> 15³ -> 20³ -> 40³ -> 50³ progression can be played/revisited without state leakage and that ordinary resources carry between worlds.
4. Inspect 20³/40³/50³ from multiple camera angles: water must read as recessed lakes/basins with sand shoreline/support, no blocks may visibly sit on top of water, and the locally corrected perimeter/interior grass rules must remain intact.
5. Verify World Browser progress percentages use the real saved physical total and replaying an older world returns to the previously active world afterward.
6. Exercise the loading screen on 20³/40³/50³ transitions and confirm the pulsing block remains animated while initial chunks are staged.
7. Verify incremental pickups read clearly and F9 feedback counts remain bounded under rapid mining.
8. Verify Cloud Charger, lightning and meteor interaction locally.
9. Verify staged tutorial/skill visibility, especially Forest Cutter at 20³ and Cloud Charger at 40³/50³.
10. Verify Revisit resumes actual state, Replay remains read-only, Esc pause/save-return behaves correctly, and the 50³ final clear exposes the dedicated demo-complete/browse flow.

No additional one-million performance benchmark is required for this progression checkpoint unless a regression is observed there.
