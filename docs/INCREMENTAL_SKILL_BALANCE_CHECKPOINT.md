# Incremental Skill Balance Checkpoint

This checkpoint records the authored balance after the passive-destruction and hover-resource-collection expansions. It complements `INCREMENTAL_SKILL_BALANCE.md`; the simulator remains a manual design tool and is not part of normal CI.

## Current authored graph

- 61 one-purchase skill nodes across the Steam-demo progression.
- The player is intentionally guided by a small set of legal/affordable nodes; equal strategic choice is not a goal for this demo.
- Core repeated axes are Breaker Speed, Breaker Power and Breaker Radius.
- The collection branch adds Collector Reach I-II, Collector Pull I-II, Personal Auto-Collect and Automation Logistics.
- Economy axes are Resource Density, Golden Veins and Critical Yield.
- Automation axes are Shovel/secondary automation speed, drill material tiers and Wide Bore.
- 40^3 introduces the event family: Cloud Charger, lightning, meteors, Radioactive Clouds and Orb Breaker.
- 50^3 exposes the finale capstones, including Aftershock, Forking Lightning, Supernova Impact II and Orb Swarm.

Collection progression is intentionally front-loaded as friction that turns into convenience:

- ordinary manual and live-automation rewards begin as hover-collected world pickups;
- Collector Reach I-II expand the collection field;
- Collector Pull I-II increase collection throughput;
- Personal Auto-Collect removes manual/Hover Mining pickup friction later in the tutorial progression;
- Automation Logistics separately unlocks automatic banking for live automation rewards.

The passive branches retain literal follow-up stat nodes rather than stopping at an unlock:

- Radioactive Clouds -> Radioactive Frequency (6.0s to 4.0s) -> Radioactive Radius (1 to 2).
- Orb Breaker -> Orb Breaker Speed -> Orb Split (second orb) -> Orb Breaker Speed II -> Orb Breaker Radius -> Orb Swarm (third orb in the 50^3 finale).

## Headless simulation result

A one-off GitHub Actions balance run executed `python tools/simulate_skill_economy.py --reserve 0.20` against the committed 61-node graph. The temporary workflow was removed immediately afterwards; the simulator itself remains available for future balance work.

Result:

- modeled clean-demo time: **32.5 minutes**;
- purchased: **61/61 skills**;
- permanent skill spend: **332,898 resources**;
- ending wallet: about **716 resources**;
- **5 timing warnings**, all fast early/carry-over purchases rather than late dead zones;
- no modeled purchase exceeded the 120-second slow-warning threshold;
- final global payout multiplier: **1.875x**;
- final precious-material multiplier: **2.0x**;
- final critical payout: **10% at 4x**;
- final Hover Mining cadence multiplier: **2.484x**;
- final secondary-automation multiplier: **1.673x**;
- final Shovel multiplier: **2.344x**;
- final Cloud Charger multiplier: **1.5x**;
- final meteor frequency multiplier: **1.5x**;
- final Orb Breaker frequency multiplier: **2.025x**.

### Purchase cadence highlights

The simulator's cheapest-legal-node path gives the intended incremental shape:

- 5^3: Collector Reach I, Hover Mining and Collector Pull I arrive almost immediately; Collector Reach II follows before the end of the stage.
- 10^3: Collector Pull II is purchased early, while the existing Shovel/Breaker upgrades remain in the roughly 9-23 second cadence band.
- 15^3: Personal Auto-Collect lands after Wide Bore at about 292 seconds total modeled playtime, so manual pickup friction is an early-game mechanic rather than permanent busywork.
- 20^3: Automation Logistics becomes legal after Resource Sensors but carries into the next stage in the cheapest-node model, preserving a meaningful period where automation produces world pickups.
- 40^3: Automation Logistics is bought immediately from carry-over, then late upgrades mostly sit around 37-55 seconds, with Cloud Charger remaining the largest lock at about 104 seconds.
- 50^3: the passive/event capstones generally take about 26-73 seconds each, ending with Orb Swarm at about 73 seconds.

This keeps the collection system as a visible early progression arc: precise manual pickup -> broader/faster pickup -> manual auto-collect -> separate automation logistics, without materially extending the modeled Steam-demo duration.

## Interpretation

The current prices are internally coherent enough to playtest rather than continue changing from theory. The next balance change should be driven by real pacing reports, especially whether pickup collection feels tactile rather than tedious, how quickly players understand the hover interaction, median resources/sec by world, and whether the 40^3 Cloud Charger lock feels active or merely slow.

The current target remains roughly a **30-35 minute Steam-demo progression** for a normal active+automation player. The model is not a completion-time oracle: actual world-clearing speed, collection behavior, player automation placement and event interaction will determine the real duration.
