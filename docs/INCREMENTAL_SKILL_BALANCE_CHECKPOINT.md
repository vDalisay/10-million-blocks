# Incremental Skill Balance Checkpoint

This checkpoint records the authored balance after the passive-destruction expansion. It complements `INCREMENTAL_SKILL_BALANCE.md`; the simulator remains a manual design tool and is not part of normal CI.

## Current authored graph

- 55 one-purchase skill nodes across the Steam-demo progression.
- The player is intentionally guided by a small set of legal/affordable nodes; equal strategic choice is not a goal for this demo.
- Core repeated axes are Breaker Speed, Breaker Power and Breaker Radius.
- Economy axes are Resource Density, Golden Veins and Critical Yield.
- Automation axes are Shovel/secondary automation speed, drill material tiers and Wide Bore.
- 40^3 introduces the event family: Cloud Charger, lightning, meteors, Radioactive Clouds and Orb Breaker.
- 50^3 exposes the finale capstones, including Aftershock, Forking Lightning, Supernova Impact II and Orb Swarm.

The passive branches now have literal follow-up stat nodes rather than stopping at an unlock:

- Radioactive Clouds -> Radioactive Frequency (6.0s to 4.0s) -> Radioactive Radius (1 to 2).
- Orb Breaker -> Orb Breaker Speed -> Orb Split (second orb) -> Orb Breaker Speed II -> Orb Breaker Radius -> Orb Swarm (third orb in the 50^3 finale).

## Headless simulation result

A one-off GitHub Actions balance run executed `python tools/simulate_skill_economy.py` against the committed graph using the default 20% spending reserve. The temporary CI hook was removed immediately afterwards; the simulator itself remains available for future balance work.

Result:

- modeled clean-demo time: **32.5 minutes**;
- purchased: **55/55 skills**;
- permanent skill spend: **329,616 resources**;
- ending wallet: about **7,955 resources**;
- only **4 timing warnings**, all fast early/carry-over purchases rather than late dead zones;
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

- 5^3: first permanent upgrades arrive about every 1-8 seconds.
- 10^3: most purchases take about 9-23 seconds.
- 15^3: Drill/Hardened Bit/Wide Bore progression takes about 16-27 seconds between purchases.
- 20^3: most permanent upgrades take roughly 16-38 seconds after carry-over is consumed.
- 40^3: late upgrades mostly sit around 37-55 seconds, with Cloud Charger acting as the largest lock at about 104 seconds.
- 50^3: the passive/event capstones generally take about 26-73 seconds each, ending with Orb Swarm at about 73 seconds.

This is intentionally close to the reference-style rhythm: very fast early reinforcement, then steadily lengthening costs while new mechanics and higher throughput keep the player active.

## Interpretation

The current prices are now internally coherent enough to playtest rather than continue changing from theory. The next balance change should be driven by real pacing reports, especially median resources/sec by world and whether the 40^3 Cloud Charger lock feels active or merely slow.

The current target is roughly a **30-35 minute Steam-demo progression** for a normal active+automation player. The model is not a completion-time oracle: actual world-clearing speed, player automation placement and event interaction will determine the real duration.
