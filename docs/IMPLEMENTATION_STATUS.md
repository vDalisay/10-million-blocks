# Implementation Status

Source plan: `docs/IMPLEMENTATION_PLAN.md`

## Current checkpoint

- Phase 0 — Plan and baseline: **complete**
- Phase 1 — Project foundation + asset catalog: **implemented, awaiting first local launch confirmation**
- Phase 2 — Reference visual slice: **implemented, awaiting visual confirmation**
- Phase 3 — Virtual world data + deterministic generator: **not started intentionally**

Phase 3 is being held until the reference visual slice is seen locally. The generator/render architecture can then preserve the approved asset scale, framing, and terrain language instead of baking in unverified assumptions.

## Phase 1 implementation

Implemented:

- Godot 4.6.1 C# `.csproj`
- configured `Main.tscn` and `GameRoot`
- versioned JSON block catalog
- `ContentDatabase` schema validation
- `BlockAssetRegistry` resource validation/preloading
- canonical glTF runtime source path
- representative grass/dirt/stone/water/ore/wood/brick/tree registrations
- asset scale/pivot/runtime rules documentation
- Windows build/play helpers
- manual GitHub Actions compile workflow

## Phase 2 implementation

Implemented:

- temporary rounded cube-planet reference builder
- supplied-mesh MultiMesh batching rather than per-block scene nodes
- bright grass / dirt / stone terrain bands
- camera-facing water basin with stone rim
- deterministic ore accents
- top-surface tree placement
- small brick tower landmark
- layered block cloud field
- subtle distant star field
- dark-space clear color
- key/fill/rim lighting setup
- smooth orbit, bounded pan, and eased zoom
- Far / Medium / Near fixed camera presets
- reference harness UI and recenter action
- visual/LOD validation checklist

## Next work after checkpoint

If the visual slice is accepted or adjusted to an acceptable baseline, continue directly with Phase 3:

1. data-driven `WorldProfile`
2. exact chunk/region coordinate math
3. deterministic side-effect-free `ProceduralWorldSource`
4. rounded-cube/cube-planet scalar field
5. layered FastNoiseLite terrain and thematic block palette selection
6. sparse chunk/region state foundation
7. independently generated chunk queries
8. 1000 × 1000 logical-address stress profile without proportional startup allocation
9. seed/profile preview controls and deterministic tests
