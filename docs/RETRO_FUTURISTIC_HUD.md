# Retro-Futuristic Gameplay HUD

This document records the shipping HUD direction established during Phase Q so later feature work does not rebuild the old panel-heavy interface.

## Information hierarchy

1. **Blocks mined is the primary number.** It stays isolated in the upper-left with current completion percentage and total world size as secondary context.
2. **Automation is operational information.** The left rail shows the automation classes available at the current progression stage, how many physical units exist in this world, and whether they are running, stopped, completed or merely ready. The full buy/place interface remains behind the Automation action instead of permanently consuming screen space.
3. **Resources are destinations.** The right-side resource ledger contains ordinary resources plus the persistent special-resource buckets. Collection presentation should visibly terminate at the bucket that receives the value.
4. **World status is peripheral.** World name, remaining blocks, completion percentage, transient feedback and hotkeys share one thin bottom strip rather than several opaque panels.
5. **The cube owns the center of the screen.** New persistent HUD elements should prefer the screen edges and should not create large center/top-center boxes during normal play.

## Resource feedback rules

- A collected ordinary block with resource value flies to the ordinary Resources bucket.
- Zero-value mined material such as water still contributes to Blocks Mined and flies to the mined-total display.
- Core, Azure and Verdant gems have persistent individual buckets, visible even at zero.
- For manual and live-automation mining, special-resource presentation waits until the physical pickup is collected; authoritative inventory accounting remains independent of the animation.
- Rapid mining may aggregate or drop presentation effects for performance, but the authoritative totals must never depend on the animation.

## Automation rail

Current short codes:

- `DRL` — Drill
- `SHV` — Powered Shovel
- `RBK` — Rock Breaker
- `CUT` — Forest Cutter

Each row may show `LOCKED`, `READY`, `LIVE`, `FAULT`, or `DONE` state plus the world-local unit count. Clicking a row opens/focuses the existing automation drawer for that class. Stopped automation uses the compact fault control beneath the rail rather than a large floating warning over the world.

The automation drawer is part of the same visual system as the rail: `AUTO// DEPLOYMENT BUS`, coded unit names (`DRL // DRILL`, etc.), bracketed module chrome and instrument-style actions. Opening it must not revert to conventional modal/card styling.

## Anti-generic rule

A dark rectangle with a one-pixel neon outline is not, by itself, the visual identity. Shipping gameplay HUD modules use broken corner brackets, registration ticks, scan-lines, asymmetric accent rails and segmented progress. Avoid full four-sided borders on every module, rounded SaaS-style cards, large soft shadows, and decorative gradients. The UI should read as a compact mining instrument panel assembled from hardware modules.

## Visual language

- Deep navy/black translucent glass rather than opaque gray boxes.
- Broken bracket corners and asymmetric rails rather than full outlines on every surface.
- Registration dots/ticks, restrained scan-lines and segmented meters provide the retro-future hardware texture.
- Near-square corners; avoid large pill buttons and exaggerated rounded cards.
- Large numeric readouts, small uppercase labels, compact system-like status copy.
- Terse console identifiers such as `MINE CORE`, `STORAGE BUS`, `AUTO BUS`, `SECTOR`, `REM`, `CLR`, `LIVE`, and `FAULT` are preferred over verbose dashboard headings.
- No decorative chrome that does not communicate grouping, state or instrumentation.
- Accent color communicates category/status; it should not fill entire large panels.
- Animation should reinforce collection, state changes or attention, not continuously move the HUD.

## Hover mining control

Hover Mining is a gameplay instrument, not a detached stock button. Its persistent lower-right control uses the same bracket/scan-line treatment and reads `HVR// STANDBY [CLICK: ARM]` or `HVR// ACTIVE [CLICK: DISARM]` so its state feels integrated with the console language.

## Debug overlays

The reference visual A/B harness remains debug-only and is centered along the top so it does not obscure the upper-left mined counter or the right resource ledger during local visual testing. Because it appears in art-review screenshots, it also uses the same restrained instrument chrome instead of stock Godot buttons.

## Future additions

New currencies or automation types should extend the appropriate rail instead of creating a new top-level HUD cluster. If a rail becomes too long at later progression tiers, prefer compact grouping/collapsing or paging over returning to a full-width top bar.
