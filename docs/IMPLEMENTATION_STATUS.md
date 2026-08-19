# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **complete and locally validated**
- Phase 2 — Reference visual slice: **functionally validated; visual shortcomings fed into the real generator**
- Phase 3 — Virtual world data + deterministic generator: **implemented**
- Phase 4 — Scalable rendering + picking: **near-world/chunk path implemented; later mesh/LOD tiers remain**
- Phase 5 — Manual mining gameplay loop: **implemented, awaiting local gameplay/visual validation**
- Phase 6 — Automation/miner framework: **next after this checkpoint**

The first reference slice proved the imported assets/camera pipeline but was visibly too artificial compared with the supplied cube-world reference. The current branch now replaces that temporary hand-built planet with the actual deterministic virtual-world system and folds the visual feedback into generation rather than polishing the disposable prototype.

## Phase 1 — validated foundation

Implemented and locally booted successfully:

- Godot 4.6.1 C# project
- configured `Main.tscn` and `GameRoot`
- versioned JSON block catalog
- `ContentDatabase` schema validation
- `BlockAssetRegistry` resource validation/preloading
- canonical glTF runtime source path
- representative grass/dirt/stone/water/ore/wood/brick/tree registrations
- Windows build/play helpers
- manual GitHub Actions compile workflow

## Reference-feedback changes

The user specifically identified two problems in the first visual slice: static clouds and terrain that looked much less natural/earth-like than the reference.

Addressed in the current implementation:

- clouds now live in multiple depth/radius layers and continuously orbit at different speeds
- cloud layers also have restrained vertical drift/bobbing so the sky no longer looks frozen
- the temporary striped shell generator is no longer used by `GameRoot`
- exterior shape now comes from low-frequency + detail deterministic 3D fractal fields sampled continuously over the cube surface
- terrain height is quantized into broad terraces so it reads as coherent landforms rather than random per-block dents
- surface material decisions use large moisture/basin/cliff fields, producing contiguous grassy regions, shore bands, water regions, and occasional rocky cliffs
- grass decoration exists on the actual exterior only; buried layers are dirt/stone, removing the repeated horizontal green stripes from the prototype
- grass-facing block meshes are rotated to the local cube-face outward direction instead of assuming world-up
- tree placement uses grove masks and local cube-face gravity, so vegetation can occur coherently on side/bottom faces rather than only on the global top
- deep block selection uses coherent stone/ore noise rather than independent random speckling

This is still procedural content under active tuning; final seed selection and art-direction cleanup remain later polish work.

## Phase 3 — virtual world + deterministic generation

Implemented:

- versioned `data/worlds/worlds.json`
- data-driven `WorldProfile`/`WorldCatalog`
- separate logical dimensions, procedural radius, render spacing, and chunk size
- exact negative-safe voxel -> chunk -> region coordinate math
- pure deterministic fractal/value-noise utility
- side-effect-free `ProceduralWorldSource.SampleVoxel`
- coherent cube-planet surface field
- depth-relative surface / soil / stone material layering on every face
- coherent water/shore/cliff masks
- deterministic ore fields
- deterministic tree/grove sampling
- sparse `WorldStateStore` containing only mined deviations
- `VirtualWorld` combining generated untouched state with sparse modifications
- exact small-world mineable count without using floating-point counters
- `stress_1000` profile that can sample arbitrary coordinates without allocating its address space
- startup self-tests covering negative chunk boundaries, determinism, sparse modifications, and 1000-address-space probes

## Phase 4 — rendering and picking foundation

Implemented now:

- per-chunk supplied-mesh MultiMesh batches instead of a Node3D/collider per block
- only exposed logical blocks are put into the visible chunk batches
- local-face orientation for grass and decorative trees
- dirty-chunk queue with a fixed rebuild budget per frame
- removal invalidates only the touched chunk plus directly affected neighboring chunk boundaries
- no per-block physics bodies
- custom screen-ray -> voxel DDA picking against logical world queries
- one reusable translucent selection highlight

Still intentionally deferred within Phase 4 until the near representation is profiled:

- greedy `ArrayMesh` medium-distance terrain path
- far macro proxy/LOD path
- camera-driven chunk streaming for worlds too large to render all surface chunks at once

Those are architectural extensions of the current chunk/query model and do not require replacing the logical generator or mining system.

## Phase 5 — playable manual mining slice

Implemented:

- `MiningService` is the authority that changes logical mined state
- base interaction removes exactly one exposed block per successful click
- manual click and camera drag are separated with a movement threshold
- hovered block is highlighted through logical DDA picking
- mined blocks disappear via dirty chunk rebuilds
- newly exposed interior blocks become targetable
- rewards remain on a 64-bit economy/counter path
- exact total-mined and remaining-block counters
- minimal HUD with block/resource counts and chunk diagnostics

## Local validation requested at this checkpoint

Run `play_game.bat` and verify:

1. the new world is visibly less striped/random and has coherent grassy, rocky, shoreline/water regions on multiple cube faces
2. clouds visibly move over time
3. trees/grass do not all assume global-up when orbiting to a side/bottom face
4. hover highlighting follows the correct visible block while orbiting
5. a simple LMB click removes exactly one highlighted block
6. an LMB drag orbits without accidentally mining
7. mining a surface block reveals and allows mining the block underneath it
8. counters update exactly once per mined block
9. frame rate remains reasonable while mining repeatedly

A Medium screenshot plus any launch/build/runtime errors is sufficient. If this checkpoint works, continue into Phase 6 automation/miners and then Phase 7 skill-tree runtime without revisiting the disposable reference builder.
