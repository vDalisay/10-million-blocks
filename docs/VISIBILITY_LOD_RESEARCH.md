# Visibility, LOD and culling research for million-block worlds

This note records the second large-world rendering pass, focused on visibility work rather than authoritative mining work. The goal is to keep the one-million-block destination visually faithful while ensuring that resident chunks, decorations, shadows and automation presentation are not submitted when they cannot materially affect the current frame.

## Sources reviewed

### Noita

- GDC Vault: *Exploring the Tech and Design of Noita* — Petri Purho / Nolla Games.
- 80.lv summary of the Noita engine talk.

The transferable lesson is not Noita's 2D pixel representation itself. It is the separation of a huge persistent world into fixed chunks and the use of dirty regions so stable/off-screen areas do not receive the same processing as active areas. Noita's falling-sand simulation divides the world into 64x64 chunks, tracks dirty rectangles, and uses chunk scheduling to limit work to changing areas.

For this project that reinforces the existing sparse dirty-frontier design and the rule that off-screen automation should mutate authoritative state without paying visible presentation/rebuild costs until the area becomes relevant again.

### Red Dead Redemption 2 / large open worlds

RDR2 is not a voxel game, so only renderer-level principles were considered. Frame analysis of its environment-map passes shows the same broad hierarchy used by mature open-world renderers: frustum culling first, reduced LOD for secondary views, only necessary object classes, and separate quality budgets for expensive effects. Rockstar also exposes separate far-shadow/LOD quality controls on PC.

The useful project-level translation is:

- cull whole spatial groups before their individual instances reach the renderer;
- treat small decorations and shadows as screen-space LOD, not as mandatory geometry at every distance;
- keep full-detail authoritative terrain data independent from the cheaper presentation chosen for a particular camera.

### reddit.com/r/VoxelGameDev

Threads reviewed included:

- `hidden chunk culling?` (2026)
- `Occlusion Culling for Chunks?` (2021)
- `chunk meshing` (2026)
- `Finally got LOD and large distance generation working` (2025)
- `Improving Rendering Distance in my Micro Voxel Engine` (2026)
- `Starting of a voxel terrain engine` (2022)
- `LOD Techniques for Voxel Engines ?` (2025)
- `Backface culling and mesh generation` (2024)

The recurring community advice is consistent:

1. split the world spatially into chunks;
2. reject chunks by distance/frustum before issuing draw work;
3. avoid generating permanently hidden voxel faces where the renderer uses generated cube meshes;
4. use progressively coarser representations for distant geometry;
5. use hierarchical/occlusion approaches only after simple frustum/distance/front-to-back techniques have been measured;
6. remember that frustum culling and occlusion culling solve different problems;
7. preserve shadows separately when needed rather than assuming a camera-invisible object must always disappear from every render pass.

This project uses supplied artistic block meshes rather than generated six-face cube meshes, so classic greedy/face meshing is not applied globally. Coarse chunk-level visibility and screen-space LOD are a better fit without replacing the art assets.

## Implemented in this pass

### 1. Conservative chunk-frustum culling

The full-surface renderer previously culled resident chunks only by which cube face pointed toward the camera. A close surface-inspection view could therefore keep submitting many front-facing shell chunks that were completely outside the camera.

Full-surface chunks now pass two CPU-level gates before their MultiMesh children are visible:

1. cube-face/back-side test;
2. conservative sphere-vs-camera-frustum test using chunk bounds plus decoration padding.

The test is deliberately padded so a borderline chunk remains visible rather than producing edge pop-in.

### 2. Independent visibility refresh cadence

Culling previously piggy-backed on automation's slower policy refresh. Once the large-world visibility system is first activated, a small presentation-only ticker refreshes visibility at a 50 ms cadence while still using camera-pose caching. This lets chunk visibility follow orbit/zoom motion without increasing the expensive automation policy-scan frequency.

### 3. Screen-space LOD for decorations and shadows

A full-resolution terrain block is never replaced or deleted because of distance. Instead, expensive secondary presentation is reduced based on projected block size:

- very small tree batches can be hidden when their block footprint is below the decorative threshold;
- tree shadows disappear before nearby trees themselves disappear;
- terrain shadow batches can be disabled at very small projected size;
- close surface focus overrides these reductions so inspection remains full-detail.

This follows the open-world principle of spending detail where it contributes visible pixels while preserving exact terrain geometry and gameplay state.

### 4. Frustum-aware automation presentation

Automation continues mining exactly regardless of camera visibility. However, a full-surface chunk that is on the back side or outside the camera frustum is now treated as presentation-inactive.

Those mutations:

- still update authoritative mined state, rewards, replay and saves;
- invalidate sparse presentation state as necessary;
- collapse visual work to deferred chunk markers;
- rebuild only when the camera can actually see the affected region again.

Boundary chunks follow the same policy, preventing a visible miner from causing unnecessary rebuilds on an adjacent off-screen chunk.

### 5. New profiling counters

F9 now separates:

- backface-culled chunks;
- frustum-culled chunks;
- hidden tree LOD batches;
- shadow batches disabled by LOD;
- existing resident/presented chunk counts;
- existing sparse-frontier and automation suppression metrics.

This makes the next local million-block benchmark able to answer whether the bottleneck is still geometry submission, shadows, sparse exposure reconstruction, mining simulation, or something else.

## Why Godot OccluderInstance3D was not enabled globally

Godot's own occlusion-culling documentation warns that its CPU/Embree occlusion system is primarily useful for closed or semi-open scenes and that large open scenes often benefit more from mesh LOD and visibility ranges. It also states that runtime movement/visibility changes of occluders trigger recomputation and that dynamic occluders are not the intended workload.

A destructible cube world continuously changes holes and tunnels. Baking the current terrain into a static occluder would therefore risk hiding geometry through newly mined openings, while rebuilding complex occluders during mining could cost more CPU than it saves.

For that reason this pass uses conservative structural culling that is always correct for the current chunk/camera state instead of enabling global Godot occlusion culling by default.

A future measured experiment could use a few very simple, deliberately conservative interior box occluders for never-exposed solid core regions, but only if profiling shows fill/vertex overdraw remains a bottleneck after the current frustum/LOD pass.

## Deliberately not changed

- No replacement of supplied block meshes with greedy flat quads.
- No macro proxy substituted for the real one-million-block surface.
- No approximation of mining state, blockers, gems or replay order.
- No blind HZB/portal system added without profiling evidence.
- No global removal of shadows for camera-invisible chunks because shadow visibility is a separate rendering concern.

## Local benchmark focus

On `stress_1000`, compare Far, Medium and close surface-focus views while F9 is open. Useful observations:

- `presented/culled` should fall sharply during close inspection;
- `backface/frustum` should show which coarse gate is doing the work;
- tree/shadow LOD counters should increase as the projected block size falls;
- `automation presentation ... suppressed` should rise when miners work outside the current view;
- FPS, chunk-build time and sparse-overlay time should be compared with the previous build under the same automation layout.

If GPU time remains high while chunk submission falls substantially, the next likely target is material/mesh cost or directional-shadow rendering. If CPU time remains high, the next target should be measured from sparse-overlay build time, automation scheduler budget and generated-sample cache metrics rather than adding another culling layer speculatively.
