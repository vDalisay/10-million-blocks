# Incremental Skill Progression / Economy Balance

## Intent

The Steam-demo skill tree is deliberately tuned to feel strongly familiar to players of **A Game About Feeding A Black Hole** and similar short-form incrementals. This is not a requirement that every apparent branch is strategically equal. A small number of legal purchases, strong price guidance and many individually modest stat nodes are intentional because they create a pleasant buy -> reveal -> afford -> buy rhythm.

We copy the **progression grammar**, not proprietary art or exact implementation:

- frequent one-purchase stat nodes instead of five-rank buttons;
- damage/power, speed and radius/coverage repeatedly interleaved;
- prices that rise much faster than the displayed percentage gain;
- harder/larger content arriving while the player is improving those stats;
- only a few currently sensible purchases, often making the cheapest node the practical next choice;
- downstream nodes progressively revealed after a purchase;
- occasional qualitative mechanics between ordinary stat steps;
- late economy multipliers, rare/high-value payout bonuses, crits and passive destruction systems.

## Public reference research

The following are public observations used to establish the *shape* of the progression. They are design references, not data files copied from the game.

### Core tree cadence

Public screenshots/discussion show Breaker stats progressing through repeated small upgrades rather than only huge transformations. One player specifically reports a displayed **Breaker Speed of 175%** and comments that only a small number of upgrade nodes are available at once, making the practical behavior "buy the cheapest 1 of the 3 options." The same feedback notes that the tree beyond a node is revealed through purchases.

Source: https://steamcommunity.com/app/3694480/discussions/0/598540696358493689/

Public screenshots observed during the research pass included examples such as:

- Breaker Speed **125% -> 150%** for a low early price;
- Asteroid Density **240% -> 280%** at a much later/higher price;
- Breaker Radius **325% -> 335%** at a still higher price.

The important balance lesson is not the exact currency number. Later upgrades can cost dramatically more while granting a *smaller displayed increment*. The player accepts this because their economy and target scale have also increased.

Additional public screenshot/review references used during research:

- https://store.steampowered.com/app/3694480/A_Game_About_Feeding_A_Black_Hole/
- https://goood.games/game/a-game-about-feeding-a-black-hole
- https://zircoonya.hatenadiary.jp/entry/2026/02/02/000219

### Upgrade families beyond damage / speed / radius

The released game and later modes publicly establish several useful incremental families:

- **Golden Asteroids** / high-value payout upgrades;
- **critical bonus upgrades**;
- electric/forking effects;
- **Radioactive Mode**, described by the developer as giant radioactive clouds that passively destroy objects and create a pseudo-idle mode;
- **Orb Breaker Mode**, where many autonomous orbs bounce around and break asteroids, with seven additional upgrades/icons;
- moon-related buffs, including increased breaker speed;
- object amount/density as an economic/difficulty axis.

Sources:

- https://steamcommunity.com/app/3694480/announcements/
- https://steamdb.info/patchnotes/23825077/
- https://steamdb.info/patchnotes/23687410/
- https://steamdb.info/patchnotes/22417258/
- https://steamcommunity.com/app/3694480/discussions/0/690873479687373659/

A public developer response also says radioactive asteroids were difficult to balance in the main mode. That is a useful warning for our passive destruction systems: they should be late, priced as milestones and measured against actual completion time rather than simply made stronger because they look satisfying.

Source: https://steamcommunity.com/app/3694480/discussions/0/691997051773427333/

## Mapping onto 10 Million Blocks

| Reference-style axis | 10 Million Blocks equivalent |
| --- | --- |
| Breaker Speed | Hover Mining cadence (`Breaker Speed I-V`) |
| Breaker Damage | Manual block hardness damage (`Breaker Power I-V`) |
| Breaker Radius / Size | manual footprint (`Breaker Radius I-III`) |
| Asteroid density / value economy | `Resource Density I-II` |
| Golden object payout | `Golden Veins` for gold/gem payout |
| Critical matter/payout | `Critical Yield I-II` |
| Electric / forking | charged-cloud lightning radius + chain/fork nodes |
| Passive radioactive destruction | `Radioactive Cloud` |
| Autonomous orb destruction | `Orb Breaker` + Orb Breaker Speed |
| Rare large-impact object | meteor frequency + `Supernova Impact` radius |
| Late qualitative breaker mechanic | `Aftershock` penetration |

The current authored tree contains many one-time nodes rather than a small number of repeatable ranks. This is intentional. Saving one rank dictionary is still sufficient because every node remains ordinary data-driven progression.

## Manual progression targets

Hover Mining begins at a 0.5 second base interval (2 actions/sec). The intended speed chain is approximately:

`100% -> 125% -> 150% -> 180% -> 216% -> 248%`

The actual multiplicative chain is:

`1.25 * 1.20 * 1.20 * 1.20 * 1.15 = 2.484x`

That gives an end-demo hover cadence of about **4.97 actions/sec** before considering coverage.

Manual radius progresses:

1. single block;
2. 3x3 plus (up to 5 exposed cells);
3. full 3x3 (up to 9 exposed cells);
4. 5x5 (up to 25 exposed cells);
5. Aftershock adds one additional newly exposed layer rather than merely making another larger square.

Manual power is now real damage against authored block hardness rather than every material being a one-action block. This lets new material tiers create the same "new object is suddenly slow -> buy damage -> it feels good again" loop that short incrementals use.

## Economy target bands

The balance simulator currently uses deliberately conservative mixed-play gross income anchors. These are design estimates to be replaced by playtest medians, not claims about perfect theoretical throughput.

| Stage | Baseline gross resources/sec | Modeled active time | Desired purchase feel |
| --- | ---: | ---: | --- |
| 1^3 | 1 | 2 sec | immediate tutorial |
| 5^3 | 8 | 40 sec | rapid 1-6 sec purchases |
| 10^3 | 16 | 90 sec | roughly 5-15 sec |
| 15^3 | 28 | 170 sec | roughly 10-30 sec |
| 20^3 | 50 | 330 sec | roughly 15-45 sec |
| 40^3 | 105 | 660 sec | roughly 30-120 sec milestones |
| 50^3 | 160 | 660 sec | roughly 45-125 sec capstones |

A default **20% income reserve** is withheld by the simulator to represent physical automation purchases and other player spending. This is deliberately conservative because physical machine prices remain much lower than late permanent skill prices.

Current physical unit prices are loaded from `data/miners/miners.json` by the simulator rather than duplicated in the script.

## Price philosophy

1. **Early nodes can be nearly free.** The first few purchases teach the interaction loop and should fire quickly.
2. **A new world should not instantly purchase its entire visible branch from carry-over currency.** Persistent wallet carry-over is part of the game, so late permanent costs must outrun previous-world leftovers.
3. **Later percentage upgrades may be smaller while prices rise.** This is part of the reference feel and is acceptable.
4. **Permanent upgrades should cost much more than one physical machine.** A global capability is not comparable to placing another local unit.
5. **Special mechanics are economy locks.** Cloud Charger, Radioactive Cloud, Orb Breaker, chain lightning and oversized meteor effects are intentionally expensive milestones.
6. **Fake choice is acceptable.** If three nodes are visible and one is clearly cheapest, that can be the intended path. The important requirement is that buying it changes/reveals something quickly enough to keep momentum.
7. **Do not create a long wait with no parallel action.** A 60-120 second price gate is acceptable in the late demo only while the player has meaningful mining/automation/events to interact with.

## Special-resource gates

`Wide Bore` consumes the authored central red gem in the 15^3 tutorial. The simulator models this gem as becoming obtainable around the middle of that stage rather than granting it at time zero.

The 20^3 generated main world guarantees one green, one blue and one red special gem in its reviewed sparse override. The simulator models those as staggered discoveries. Special inventory persists and special costs are checked before a node is considered legal.

This matters because an affordability simulator that ignores special resources can produce a plausible-looking but impossible cheapest-purchase order.

## Headless balance simulator

Run manually from the repository root:

```bash
python tools/simulate_skill_economy.py
```

Useful variants:

```bash
python tools/simulate_skill_economy.py --reserve 0.30
python tools/simulate_skill_economy.py --fast-warning 3 --slow-warning 90
python tools/simulate_skill_economy.py --no-stage-limit
```

The tool loads the committed:

- skill graph and costs;
- world staging;
- progression order;
- miner/unit prices;
- special costs.

It then follows the intentionally reference-like **cheapest legal node** purchase strategy, carries the wallet between worlds, applies payout/critical multipliers to subsequent income, models timed guaranteed special-resource discoveries and reports remaining locks.

Warnings are heuristics:

- `<2 sec` means a node may be disappearing into purchase spam;
- `>120 sec` means a node deserves scrutiny for a possible dead zone.

Neither automatically means the price is wrong. A cheap reveal node can intentionally be instant; a large late mechanic can intentionally take more than two minutes if other gameplay remains active.

## Playtest calibration loop

1. Play normally on a clean save.
2. Collect the existing Phase Q pacing report.
3. Compute median effective resources/sec for each world from several runs.
4. Replace the simulator's `STAGES` income anchors with those medians.
5. Run the cheapest-node simulation at 20%, 30% and 40% spending reserve.
6. Inspect the purchase order and every timing warning.
7. Change prices in `data/skills/skill_tree.json`, not in the simulator.
8. Repeat until the economy still guides the player strongly but does not produce accidental multi-minute inactivity.

## Current direction

The current implementation intentionally leans *more*, not less, into the reference game's progression feel. The goal is not broad buildcraft in the Steam demo. The goal is a compact sequence of satisfying purchases, visible stat growth, a few tempting adjacent options, and increasingly dramatic mechanics as the cube sizes escalate.
