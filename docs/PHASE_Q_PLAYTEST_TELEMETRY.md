# Phase Q pacing and polish playtest telemetry

Phase Q deliberately does not guess final resource costs, automation prices or rare-resource pacing from static code. Those values should be tuned from real playtests. Debug builds now collect the objective measurements from the progression plan locally so balancing decisions can be based on evidence rather than world size or intuition alone.

## What is recorded

A persistent authored-world session creates a debug-only `PacingTelemetryRecorder`. It does not send data anywhere and does not participate in mining, currency, saves or replay authority.

The report records:

- active time in the currently loaded world session;
- total/manual/automated/other-source mined blocks;
- first automation placement time when the session begins without a physical automation;
- automation units at session start/end and maximum unit count;
- placements, actionable stops and relocations;
- resources at report time;
- final skill ranks and special-resource balances;
- skill-rank change timeline;
- semantic tutorial/world-event counts;
- longest observed gap between player-action/decision signals.

The action-gap signal resets on manual mining, a skill change, automation placement/relocation, and deliberate lightning/meteor interactions. Passive automation progress and a machine stopping do **not** reset it. This is intentionally conservative: a 60–120 second gap is a flag for review, not proof that the player was bored.

Reports are written under:

`user://pacing_reports`

A completed world writes a `*_completed.txt` report. Leaving an incomplete world after meaningful activity writes a `*_left_world.txt` partial report.

## Analyze several runs

Use the dependency-free analyzer from the repository root:

```text
python tools/analyze_pacing_reports.py <report-file-or-directory> [...]
```

It produces a compact Markdown-style summary by world, including average session duration, longest action gap, manual share, ending resources, stop/relocation totals and a separate list of completed runs. It also flags observed action gaps of at least 60 seconds.

The parser is forward-compatible with additional `key=value` telemetry fields: unknown fields are ignored.

## How to use the numbers

Use several clean runs before changing costs. In particular:

- **first automation arrives too late**: inspect early resource income and unlock cost before increasing manual mining power;
- **automation is purchased immediately with no tradeoff**: consider its price relative to competing skills, not just a flat global nerf;
- **many stops but almost no relocations**: inspect whether the attention/move flow is understandable before assuming the machine is badly balanced;
- **very high manual share after automation is established**: determine whether automation is too weak, too specialized, or simply deployed too late;
- **very low manual share immediately after an unlock**: check whether active systems and manual upgrades still create useful decisions;
- **large ending resource surplus**: compare which desirable purchases were still available before reducing all rewards;
- **60–120+ second action gaps**: inspect the matching playtest context for waiting/grind. Do not automatically solve it by making the next cube smaller or larger.

Special-resource placement should be tuned alongside the upgrade graph: a gem is only meaningfully rare relative to when its consuming transformation becomes desirable.

## What remains subjective

The recorder cannot decide:

- whether an upgrade felt mandatory or optional;
- whether a gap was relaxing, boring or intentional;
- whether the tutorial wording was clear;
- whether clouds, meteors, effects and the final 50³ world feel visually polished;
- whether the current renderer has any local visual gap/cavity issue.

Those remain local playtest/art-review judgments. The telemetry exists to make the numerical part of Phase Q easier to tune after those runs.
