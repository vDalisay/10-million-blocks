# Terrain Generation Research and Adaptation

This document records the terrain-generation model used to move the cube worlds away from independent per-block randomness and toward coherent Minecraft-like landscapes while retaining the game's cube-planet topology.

## Primary research basis

The implementation was informed by Mojang/Microsoft's current Minecraft Creator documentation rather than by copying Minecraft implementation code.

Useful concepts from the official documentation:

- World generation is staged. Broad terrain/landforms are established before biome-specific surface content and features.
- Smooth noise fields are used for large-scale terrain rather than unrelated random decisions at every coordinate.
- Biome/climate systems use multiple continuous parameters such as temperature, humidity and erosion, with additional variation parameters.
- Surface builders distinguish top material, mid/subsurface material, sea material and sea-floor material.
- Overworld height presets explicitly distinguish beaches, oceans, deep oceans, lowlands, highlands and mountains.
- Features such as vegetation are placed later through rule/condition/distribution passes rather than being part of the base terrain density test.

These are architectural ideas, not a goal of reproducing Minecraft's exact generator.

## Adaptation to a cube planet

A normal Minecraft world has one global vertical axis. This project instead treats the dominant coordinate axis at each point as local outward gravity, so the same generation logic wraps around all six faces.

The current pipeline is:

1. **Cube-surface coordinates**
   - Convert a voxel coordinate to a continuous point on the normalized cube surface.
   - Sampling remains continuous across face boundaries.

2. **Landform pass**
   - `continentalness`: broad land-versus-lowland tendency.
   - `erosion`: controls how much local relief survives.
   - `ridge`: produces coherent mountain/ridge masks.
   - `weirdness`: secondary macro variation.
   - detail noise is deliberately lower-amplitude than the macro fields.
   - resulting radius is quantized to configurable plateau steps for strong voxel terraces.

3. **Climate pass**
   - humidity
   - temperature
   - basin/lake field
   - forest field

4. **Hydrology pass**
   - oceans are selected from large low continental/hydrology regions.
   - inland lakes use a separate basin signal.
   - water occupies a stable local radial sea level.
   - terrain beneath water is lowered to a lake/ocean floor instead of replacing arbitrary grass blocks with blue blocks.

5. **Surface-builder rules**
   - waterfront and near-water ground uses sand.
   - ordinary land uses grass with dirt underneath.
   - strong ridge/cliff regions expose stone.
   - deeper layers transition to stone/ore fields.
   - water has shallow, normal and deep visual tiers using the supplied water mesh with lightweight material tint variants.

6. **Feature pass**
   - trees require appropriate exposed grass, no water/shore conflict and acceptable cliff slope.
   - a broad forest field plus humidity/temperature suitability creates groves and clearings.
   - a final deterministic hash spaces individual trees.

## Why this is closer to the reference

The reference image communicates authored environmental rules: lakes form regions, shorelines usually transition through sand, water reads differently by depth, large grassy plateaus remain coherent, exposed stone appears in geological areas, and vegetation is clustered rather than randomly peppered over every surface.

The staged pipeline creates those correlations deliberately. Individual world profiles can now vary the same generator through data: sea level, ocean threshold, lake threshold, erosion, ridge frequency, forest threshold, plateau step and other values can produce distinct worlds without another generator rewrite.

## Current tuning targets

`reference_natural` is the main baseline world. Startup self-tests now require it to contain meaningful water volume, both shallow and deep water tiers, beach material and a non-trivial deterministic tree population.

`reference_lakes` is a deliberately more water-forward second test profile used to validate world-to-world content variation and the completion/Continue flow.

The final game should eventually have an authored seed-selection workflow: generate candidates, compare them against the visual target, and commit the chosen seed/profile values rather than relying on arbitrary runtime random generation.

## Deliberate differences from Minecraft

- Terrain wraps around a cube planet using local radial/outward gravity.
- Water is currently static authored-generation volume, not a fluid simulation.
- The generator is deterministic and primarily intended as a development/content-authoring tool; shipped worlds may use fixed approved seeds.
- The rendering and simulation architecture remains sparse/chunked so logical world size is independent of the number of resident Godot objects.
