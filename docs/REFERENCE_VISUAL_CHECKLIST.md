# Reference Visual Slice — Validation Checklist

This is the first local checkpoint in `IMPLEMENTATION_PLAN.md`. It deliberately validates presentation before the scalable generator replaces the temporary reference planet.

## Launch

Run:

```bat
play_game.bat
```

A successful boot should show the floating cube planet immediately and should not show the startup-validation error panel.

## Controls

- Left mouse drag: orbit
- Right or middle mouse drag: bounded pan
- Mouse wheel: zoom
- `1`: far framing
- `2`: medium framing
- `3`: near framing
- `F`: recenter pan

The small harness panel also exposes the three presets and recenter action as buttons.

## What to validate visually

Compare against the supplied screenshot/video rather than judging the slice in isolation.

### Composition

- The planet remains centered and visually dominant at the Medium preset.
- There is substantial dark negative space around the planet.
- The complete rounded/cube-like silhouette is readable at Far.
- Near is close enough to inspect the authored block models without clipping into the planet.

### Terrain language

- Grass is clearly readable as the bright surface layer.
- Side/lower faces transition through dirt and stone rather than reading as one flat material.
- The recessed-looking blue lake on the camera-facing side is obvious.
- Ore variation is occasional rather than visually noisy.
- Trees and the small brick tower make the surface feel like a miniature world rather than a plain cube.

### Presentation

- Block clouds sit at several depths around the world.
- Lighting has a bright readable key side and a cooler/darker shadow side.
- The background stays very dark and the star field remains subtle.
- Imported block models retain their supplied texture/material appearance.

### Camera feel

- Orbit is smooth and does not jump when the button is first pressed.
- A normal click without dragging does not noticeably rotate the camera.
- Zoom eases rather than snapping.
- Pan is bounded, and `F` reliably recovers the centered composition.
- Switching Far/Medium/Near eases to stable repeatable viewpoints.

## LOD expectations established by these presets

These are visual contracts for the later renderer, not the final thresholds:

- **Near:** preserve the authored uneven block geometry and decorative detail.
- **Medium:** preserve exact world silhouette, major terrain bands, water, trees, and structures; dense terrain can transition to chunk meshes.
- **Far:** prioritize the cube-world silhouette, biome/color masses, major water/feature landmarks, and clouds; tiny per-block detail may be simplified.

A later LOD implementation may change how the scene is rendered, but these three views should remain visually consistent.

## Feedback needed at this checkpoint

A screenshot from the Medium preset is the most useful first comparison. If something is wrong, note whether it is primarily:

- planet shape/terrain,
- asset scale/spacing,
- material appearance,
- lighting,
- clouds/background,
- camera framing,
- camera controls.

After the visual direction is confirmed, implementation proceeds into the deterministic virtual-world generator and chunk/storage architecture.
