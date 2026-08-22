# One-million world performance baseline

This note records the last clean local `stress_1000` / F11 baseline supplied during the future-world-progression implementation pass and the code changes made immediately afterward. It is deliberately separate from demo balancing: the 100³ destination remains outside the Steam-demo progression.

## Clean measured baseline

The report was captured after the F11 initial-presentation gate was added, so no initial chunk loading occurred inside the 70-second measurement window.

- 120 physical automation units at the final stage.
- final nominal automation rate: ~538 blocks/s.
- 13,669 authoritative removals during the 70-second suite.
- overall wall-clock average: 58.2 FPS.
- frame P50/P95/P99: 15.0 / 33.3 / 47.9 ms.
- worst frame: 77.0 ms; no frames exceeded 100 ms.
- final 60–70s 120-unit window: 44.0 FPS average, 43.5 ms P95, 55.2 ms P99.
- render CPU/GPU in the final window: ~3.7 / 8.7 ms average.
- final-window draw calls: ~1,682 average.
- 2,855 base chunk builds consumed ~12.4 seconds total CPU time, ~4.34 ms/build.
- sparse cavity overlay: 4,071 builds, ~0.31 ms/build average.
- generated sample cache hit rate: ~90.9%.
- initial/pending stream loads during measured run: 0.

The key result is that the GPU is not the dominant limiter on the supplied Ryzen 5 5600 / RTX 3070 system. Base surface reconstruction and other main-thread simulation/presentation work dominate the worst frames. Sparse cavity reconstruction itself is comparatively cheap.

## Renderer correctness fixes after the baseline

Repeated stress runs had two different causes of apparent see-through holes:

1. old F11 runs were cumulative when the tester stayed in the same non-persistent stress session, so repeated line-miner/manual passes could eventually create genuine tunnels through the cube;
2. deferred cavity presentation and base-shell culling still contained assumptions based on the original cube outward normal, which are invalid once excavation exposes walls facing arbitrary directions.

The follow-up renderer therefore:

- keeps cavity/tunnel geometry in independent sparse render roots;
- never lets a base-surface root replacement destroy an already-reconstructed cavity root;
- bypasses coarse cube-face backface rejection for chunks that participate in excavation while retaining conservative frustum culling and normal GPU depth/backface rejection;
- promotes deferred full-surface automation by chunk/frustum relevance rather than the original voxel cube-face normal;
- tracks camera forward direction as well as position so rotation alone can promote newly visible deferred cavities;
- bounds the outer surface-column inward scan to the authored detailed shell; deeper excavation belongs exclusively to the sparse cavity renderer instead of searching across most of a 100³ cube;
- coalesces visible automation base-surface commits to ~13 Hz while authoritative mining and sparse cavity updates remain independent;
- exposes cavity-root totals/presented/frustum-cull counts and visible/deferred automation flush state in F9.

## Repeatability rule

F11 now resets an already-mined `stress_1000` session to a fresh deterministic baseline before starting another benchmark. It then waits for the replacement world's initial presentation to finish before starting the timer. This is required for meaningful comparisons: a second run must not inherit the first run's tunnels, state density, renderer roots or machine placements.

## Remaining optimization direction

Do not optimize the million-block renderer by increasing simulation approximation or dropping authoritative removals. The next large architectural step, only if future profiling still justifies it, is retaining/reusing base chunk MultiMesh batches instead of replacing base renderer roots. Until then, prefer:

- fewer/coalesced base-surface commits;
- incremental sparse cavity updates;
- compact mined-state bitsets;
- off-screen/deferred presentation;
- bounded cosmetic work;
- screen-space LOD and coarse frustum rejection;
- data from clean F11 runs rather than cumulative stress sessions.
