#!/usr/bin/env python3
"""Offline balancing simulator for the authored skill economy.

This is intentionally NOT part of CI. It is a designer tool for answering questions such as:
- how long does the cheapest currently available upgrade take at each world stage?
- which nodes stay locked until a later world because of prerequisites/category staging?
- how much do payout/critical upgrades accelerate later purchases?
- where do we accidentally create <2 second spam or >120 second dead zones?

The model is deliberately transparent rather than pretending to be a perfect player simulation. Baseline
income rates represent a reasonable active+automation play style measured/estimated for each authored
world size. A configurable reserve keeps part of income available for physical automation purchases.
All skill costs/effects/staging are loaded from the actual repository JSON on every run.
"""
from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]


def load(relative: str):
    with (ROOT / relative).open("r", encoding="utf-8") as handle:
        return json.load(handle)


@dataclass(frozen=True)
class StageModel:
    world_id: str
    label: str
    base_resources_per_second: float
    active_seconds: float
    precious_share: float


# Baseline *gross* resource rates before skill-tree payout multipliers. These are intentionally
# conservative mixed-play figures rather than theoretical fully-upgraded surface ceilings. Tune them
# from pacing reports when real playtests exist; the simulator then immediately shows the price impact.
STAGES = [
    StageModel("tutorial_single_block", "1^3", 1.0, 2.0, 0.00),
    StageModel("tutorial_dirt_5", "5^3", 8.0, 40.0, 0.00),
    StageModel("tutorial_lake_core_10", "10^3", 16.0, 90.0, 0.00),
    StageModel("tutorial_trees_gem_15", "15^3", 28.0, 170.0, 0.015),
    StageModel("reference_natural", "20^3", 50.0, 330.0, 0.035),
    StageModel("reference_lakes", "40^3", 105.0, 660.0, 0.045),
    StageModel("reference_ridges", "50^3", 160.0, 660.0, 0.055),
]


def staged(skill: dict, world: dict) -> bool:
    categories = world.get("visibleSkillCategories", [])
    exact = world.get("visibleSkillIds", [])
    if not categories and not exact:
        return True
    return skill.get("category", "") in categories or skill["id"] in exact


def prerequisites_met(skill: dict, purchased: set[str]) -> bool:
    return all(item["node_id"] in purchased for item in skill.get("prerequisites", []))


def next_available(skills: dict[str, dict], world: dict, purchased: set[str]) -> list[dict]:
    return [
        skill for skill in skills.values()
        if skill["id"] not in purchased
        and staged(skill, world)
        and prerequisites_met(skill, purchased)
    ]


def apply_effects(skill: dict, state: dict[str, float]) -> None:
    for effect in skill.get("effects", []):
        kind = effect.get("type")
        value = float(effect.get("value", 0.0))
        if kind == "multiply_resource_yield":
            state["resource"] *= max(0.01, value)
        elif kind == "multiply_precious_resource_yield":
            state["precious"] *= max(0.01, value)
        elif kind == "add_critical_yield_chance":
            state["crit_chance"] = min(0.75, state["crit_chance"] + max(0.0, value))
        elif kind == "set_critical_yield_multiplier":
            state["crit_multiplier"] = max(state["crit_multiplier"], max(1.0, value))


def payout_multiplier(stage: StageModel, state: dict[str, float]) -> float:
    precious_mix = 1.0 + stage.precious_share * (state["precious"] - 1.0)
    crit_expected = 1.0 + state["crit_chance"] * (state["crit_multiplier"] - 1.0)
    return state["resource"] * precious_mix * crit_expected


def format_wait(seconds: float) -> str:
    if seconds < 0.05:
        return "0.0s"
    if seconds < 60.0:
        return f"{seconds:.1f}s"
    return f"{seconds / 60.0:.1f}m"


def main() -> int:
    parser = argparse.ArgumentParser(description="Simulate authored skill-tree affordability by world stage.")
    parser.add_argument(
        "--reserve",
        type=float,
        default=0.20,
        help="Fraction of earned resources reserved for physical automation/other spending (default: 0.20).",
    )
    parser.add_argument(
        "--fast-warning",
        type=float,
        default=2.0,
        help="Flag purchases affordable in fewer than this many seconds (default: 2).",
    )
    parser.add_argument(
        "--slow-warning",
        type=float,
        default=120.0,
        help="Flag purchases requiring more than this many seconds (default: 120).",
    )
    parser.add_argument(
        "--no-stage-limit",
        action="store_true",
        help="Ignore authored stage time budgets and keep buying until no staged prerequisite-ready nodes remain.",
    )
    args = parser.parse_args()

    reserve = min(0.90, max(0.0, args.reserve))
    skill_doc = load("data/skills/skill_tree.json")
    world_doc = load("data/worlds/worlds.json")
    progression_doc = load("data/progression/world_progression.json")
    skills = {item["id"]: item for item in skill_doc["nodes"]}
    worlds = {item["id"]: item for item in world_doc["worlds"]}

    expected_order = [stage.world_id for stage in STAGES]
    actual_order = progression_doc["world_ids"]
    if actual_order != expected_order:
        raise SystemExit(f"Simulator stage table is stale. Expected {expected_order}, content has {actual_order}.")

    purchased: set[str] = set()
    wallet = 0.0
    total_elapsed = 0.0
    total_spent = 0.0
    state = {
        "resource": 1.0,
        "precious": 1.0,
        "crit_chance": 0.0,
        "crit_multiplier": 2.0,
    }
    warning_count = 0

    print("10 Million Blocks - skill economy simulation")
    print(f"strategy=cheapest-ready  reserve={reserve:.0%}  fast<{args.fast_warning:.0f}s  slow>{args.slow_warning:.0f}s")
    print("baseline rates are gross mixed-play estimates; payout/critical skills modify them dynamically\n")

    for stage in STAGES:
        world = worlds[stage.world_id]
        stage_elapsed = 0.0
        stage_start_wallet = wallet
        stage_start_purchased = len(purchased)
        print(f"=== {stage.label}  {world['displayName']} ===")
        print(
            f"base={stage.base_resources_per_second:.1f}/s  stage_budget={stage.active_seconds:.0f}s  "
            f"precious_mix={stage.precious_share:.1%}  wallet_in={wallet:.1f}"
        )

        while True:
            available = next_available(skills, world, purchased)
            if not available:
                break

            # Intentional fake-choice model: pick the cheapest currently legal node, then repeat. This
            # approximates the reference game's strong cost-guided path rather than optimizing a build.
            available.sort(key=lambda node: (int(node.get("cost", 0)), node["id"]))
            skill = available[0]
            cost = float(skill.get("cost", 0))
            multiplier = payout_multiplier(stage, state)
            gross_rate = stage.base_resources_per_second * multiplier
            skill_rate = gross_rate * (1.0 - reserve)
            if skill_rate <= 0.0:
                raise SystemExit("Effective skill-income rate reached zero.")

            wait = max(0.0, cost - wallet) / skill_rate
            if not args.no_stage_limit and stage_elapsed + wait > stage.active_seconds:
                # Spend the rest of this world's modeled active time earning toward the lock, then carry
                # that persistent wallet forward. This is exactly the cross-world economy behavior.
                remaining = max(0.0, stage.active_seconds - stage_elapsed)
                wallet += remaining * skill_rate
                total_elapsed += remaining
                stage_elapsed += remaining
                print(
                    f"  LOCK -> {skill['display_name']} ({cost:.0f}) needs {format_wait(wait)}; "
                    f"stage ends with {wallet:.1f} resources"
                )
                break

            wallet += wait * skill_rate
            stage_elapsed += wait
            total_elapsed += wait
            wallet -= cost
            total_spent += cost
            purchased.add(skill["id"])
            apply_effects(skill, state)

            marker = ""
            if wait < args.fast_warning:
                marker = "  [FAST]"
                warning_count += 1
            elif wait > args.slow_warning:
                marker = "  [SLOW]"
                warning_count += 1

            next_multiplier = payout_multiplier(stage, state)
            print(
                f"  {total_elapsed:7.1f}s  {skill['display_name']:<25} cost={cost:>7.0f}  "
                f"wait={format_wait(wait):>6}  wallet={wallet:>8.1f}  income={gross_rate:>7.1f}/s"
                f" -> x{next_multiplier:.3f}{marker}"
            )

            if not args.no_stage_limit and stage_elapsed >= stage.active_seconds:
                break

        if not args.no_stage_limit and stage_elapsed < stage.active_seconds:
            remaining = stage.active_seconds - stage_elapsed
            gross_rate = stage.base_resources_per_second * payout_multiplier(stage, state)
            wallet += remaining * gross_rate * (1.0 - reserve)
            total_elapsed += remaining
            stage_elapsed += remaining

        print(
            f"  stage_out: +{len(purchased) - stage_start_purchased} skills, "
            f"wallet {stage_start_wallet:.1f}->{wallet:.1f}, elapsed={stage_elapsed:.1f}s\n"
        )

    remaining = [skill for skill in skills.values() if skill["id"] not in purchased]
    print("=== SUMMARY ===")
    print(f"purchased={len(purchased)}/{len(skills)}  spent={total_spent:.0f}  wallet={wallet:.0f}  modeled_time={total_elapsed / 60.0:.1f}m")
    print(
        f"payout: global=x{state['resource']:.3f} precious=x{state['precious']:.3f} "
        f"crit={state['crit_chance']:.1%}@x{state['crit_multiplier']:.1f}"
    )
    print(f"timing_warnings={warning_count}")
    if remaining:
        print("unbought:")
        for skill in sorted(remaining, key=lambda item: (item.get("grid_y", 0), item.get("cost", 0))):
            missing = [p["node_id"] for p in skill.get("prerequisites", []) if p["node_id"] not in purchased]
            print(f"  - {skill['display_name']} ({skill['cost']})" + (f" requires {', '.join(missing)}" if missing else ""))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
