#!/usr/bin/env python3
"""Offline balancing simulator for the authored skill economy.

This is intentionally NOT part of normal CI. It is a designer tool for answering questions such as:
- how long does the cheapest currently available upgrade take at each world stage?
- which nodes stay locked until a later world because of prerequisites/category staging?
- when do gem/special-resource gates actually become purchasable?
- how much do payout/critical upgrades accelerate later purchases?
- are permanent-skill prices still meaningful compared with physical automation prices?
- where do we accidentally create <2 second spam or >120 second dead zones?

The model is deliberately transparent rather than pretending to be a perfect player simulation. Baseline
income rates represent a reasonable active+automation play style for each authored world size. All skill
costs/effects/staging, miner unit prices and special costs are loaded from the actual repository JSON.
"""
from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load(relative: str):
    with (ROOT / relative).open("r", encoding="utf-8") as handle:
        return json.load(handle)


@dataclass(frozen=True)
class SpecialGrant:
    at_seconds: float
    resource_id: str
    amount: int


@dataclass(frozen=True)
class StageModel:
    world_id: str
    label: str
    base_resources_per_second: float
    active_seconds: float
    precious_share: float
    special_grants: tuple[SpecialGrant, ...] = ()


# Gross mixed-play rates before payout multipliers. Keep these conservative: they are not theoretical
# fully-upgraded surface ceilings. Replace them with medians from pacing reports as local playtests grow.
# Special grants model when the authored guaranteed gems are likely to be reached, rather than handing
# them to the cheapest-node solver at the first frame of a world.
STAGES = [
    StageModel("tutorial_single_block", "1^3", 1.0, 2.0, 0.00),
    StageModel("tutorial_dirt_5", "5^3", 8.0, 40.0, 0.00),
    StageModel("tutorial_lake_core_10", "10^3", 16.0, 90.0, 0.00),
    StageModel(
        "tutorial_trees_gem_15", "15^3", 28.0, 170.0, 0.015,
        (SpecialGrant(85.0, "gem_red", 1),),
    ),
    StageModel(
        "reference_natural", "20^3", 50.0, 330.0, 0.035,
        (
            SpecialGrant(105.0, "gem_green", 1),
            SpecialGrant(165.0, "gem_blue", 1),
            SpecialGrant(225.0, "gem_red", 1),
        ),
    ),
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


def special_affordable(skill: dict, special: dict[str, int]) -> bool:
    return all(special.get(cost["resource_id"], 0) >= int(cost["amount"]) for cost in skill.get("special_costs", []))


def next_available(skills: dict[str, dict], world: dict, purchased: set[str], special: dict[str, int]) -> list[dict]:
    return [
        skill for skill in skills.values()
        if skill["id"] not in purchased
        and staged(skill, world)
        and prerequisites_met(skill, purchased)
        and special_affordable(skill, special)
    ]


def spend_special(skill: dict, special: dict[str, int]) -> None:
    for cost in skill.get("special_costs", []):
        resource_id = cost["resource_id"]
        amount = int(cost["amount"])
        if special.get(resource_id, 0) < amount:
            raise RuntimeError(f"special resource race while purchasing {skill['id']}: {resource_id}")
        special[resource_id] -= amount


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
        elif kind == "multiply_manual_mining_rate":
            state["manual_rate"] *= max(0.01, value)
        elif kind == "multiply_miner_rate":
            state["automation_rate"] *= max(0.01, value)
        elif kind == "multiply_shovel_rate":
            state["shovel_rate"] *= max(0.01, value)
        elif kind == "multiply_cloud_charge_rate":
            state["cloud_rate"] *= max(0.01, value)
        elif kind == "multiply_meteor_spawn_rate":
            state["meteor_rate"] *= max(0.01, value)
        elif kind == "multiply_orb_breaker_rate":
            state["orb_rate"] *= max(0.01, value)


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


def format_special(special: dict[str, int]) -> str:
    active = [f"{key}={value}" for key, value in sorted(special.items()) if value > 0]
    return ", ".join(active) if active else "none"


def grant_due(stage: StageModel, stage_elapsed: float, granted: set[tuple[str, float]], special: dict[str, int]) -> list[str]:
    messages: list[str] = []
    for grant in stage.special_grants:
        key = (grant.resource_id, grant.at_seconds)
        if key in granted or stage_elapsed + 1e-9 < grant.at_seconds:
            continue
        granted.add(key)
        special[grant.resource_id] = special.get(grant.resource_id, 0) + grant.amount
        messages.append(f"+{grant.amount} {grant.resource_id}")
    return messages


def next_grant_time(stage: StageModel, stage_elapsed: float, granted: set[tuple[str, float]]) -> float | None:
    future = [
        grant.at_seconds for grant in stage.special_grants
        if (grant.resource_id, grant.at_seconds) not in granted and grant.at_seconds > stage_elapsed + 1e-9
    ]
    return min(future) if future else None


def main() -> int:
    parser = argparse.ArgumentParser(description="Simulate authored skill-tree affordability by world stage.")
    parser.add_argument(
        "--reserve",
        type=float,
        default=0.20,
        help="Fraction of earned resources reserved for physical automation/other spending (default: 0.20).",
    )
    parser.add_argument("--fast-warning", type=float, default=2.0, help="Flag purchases faster than this many seconds.")
    parser.add_argument("--slow-warning", type=float, default=120.0, help="Flag purchases slower than this many seconds.")
    parser.add_argument(
        "--no-stage-limit",
        action="store_true",
        help="Ignore authored stage time budgets and keep buying until no staged legal nodes remain.",
    )
    args = parser.parse_args()

    reserve = min(0.90, max(0.0, args.reserve))
    skill_doc = load("data/skills/skill_tree.json")
    world_doc = load("data/worlds/worlds.json")
    progression_doc = load("data/progression/world_progression.json")
    miner_doc = load("data/miners/miners.json")
    skills = {item["id"]: item for item in skill_doc["nodes"]}
    worlds = {item["id"]: item for item in world_doc["worlds"]}
    miners = {item["id"]: item for item in miner_doc["miners"]}

    expected_order = [stage.world_id for stage in STAGES]
    actual_order = progression_doc["world_ids"]
    if actual_order != expected_order:
        raise SystemExit(f"Simulator stage table is stale. Expected {expected_order}, content has {actual_order}.")

    purchased: set[str] = set()
    wallet = 0.0
    total_elapsed = 0.0
    total_spent = 0.0
    special: dict[str, int] = {}
    state = {
        "resource": 1.0,
        "precious": 1.0,
        "crit_chance": 0.0,
        "crit_multiplier": 2.0,
        "manual_rate": 1.0,
        "automation_rate": 1.0,
        "shovel_rate": 1.0,
        "cloud_rate": 1.0,
        "meteor_rate": 1.0,
        "orb_rate": 1.0,
    }
    warning_count = 0

    unit_prices = {miner_id: int(item["unit_price"]) for miner_id, item in miners.items()}
    ordinary_unit_prices = [price for miner_id, price in unit_prices.items() if miner_id != "wide_bore_miner"]

    print("10 Million Blocks - skill economy simulation")
    print(f"strategy=cheapest-ready  reserve={reserve:.0%}  fast<{args.fast_warning:.0f}s  slow>{args.slow_warning:.0f}s")
    print("baseline rates are gross mixed-play estimates; payout/critical skills modify them dynamically")
    print(
        "physical automation unit prices: "
        + ", ".join(f"{miners[mid]['display_name']}={price}" for mid, price in unit_prices.items() if mid != "wide_bore_miner")
        + f" (median-scale={sorted(ordinary_unit_prices)[len(ordinary_unit_prices)//2]})\n"
    )

    for stage in STAGES:
        world = worlds[stage.world_id]
        stage_elapsed = 0.0
        stage_start_wallet = wallet
        stage_start_purchased = len(purchased)
        granted: set[tuple[str, float]] = set()
        print(f"=== {stage.label}  {world['displayName']} ===")
        print(
            f"base={stage.base_resources_per_second:.1f}/s  stage_budget={stage.active_seconds:.0f}s  "
            f"precious_mix={stage.precious_share:.1%}  wallet_in={wallet:.1f}  special={format_special(special)}"
        )

        while True:
            for message in grant_due(stage, stage_elapsed, granted, special):
                print(f"  {total_elapsed:7.1f}s  SPECIAL {'':<17} {message}; inventory={format_special(special)}")

            available = next_available(skills, world, purchased, special)
            if not available:
                next_grant = next_grant_time(stage, stage_elapsed, granted)
                if next_grant is not None and (args.no_stage_limit or next_grant <= stage.active_seconds):
                    advance = max(0.0, next_grant - stage_elapsed)
                    gross_rate = stage.base_resources_per_second * payout_multiplier(stage, state)
                    wallet += advance * gross_rate * (1.0 - reserve)
                    stage_elapsed += advance
                    total_elapsed += advance
                    continue
                break

            # Intentional fake-choice model: buy the cheapest currently legal node. This approximates the
            # reference game's strong price-guided path rather than trying to optimize a build order.
            available.sort(key=lambda node: (int(node.get("cost", 0)), node["id"]))
            skill = available[0]
            cost = float(skill.get("cost", 0))
            multiplier = payout_multiplier(stage, state)
            gross_rate = stage.base_resources_per_second * multiplier
            skill_rate = gross_rate * (1.0 - reserve)
            if skill_rate <= 0.0:
                raise SystemExit("Effective skill-income rate reached zero.")

            wait = max(0.0, cost - wallet) / skill_rate
            next_grant = next_grant_time(stage, stage_elapsed, granted)
            if next_grant is not None and next_grant < stage_elapsed + wait:
                advance = next_grant - stage_elapsed
                wallet += advance * skill_rate
                stage_elapsed += advance
                total_elapsed += advance
                continue

            if not args.no_stage_limit and stage_elapsed + wait > stage.active_seconds:
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
            spend_special(skill, special)
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
            special_suffix = f"  special={format_special(special)}" if skill.get("special_costs") else ""
            print(
                f"  {total_elapsed:7.1f}s  {skill['display_name']:<25} cost={cost:>7.0f}  "
                f"wait={format_wait(wait):>6}  wallet={wallet:>8.1f}  income={gross_rate:>7.1f}/s"
                f" -> payout x{next_multiplier:.3f}{special_suffix}{marker}"
            )

            if not args.no_stage_limit and stage_elapsed >= stage.active_seconds:
                break

        if not args.no_stage_limit and stage_elapsed < stage.active_seconds:
            remaining = stage.active_seconds - stage_elapsed
            gross_rate = stage.base_resources_per_second * payout_multiplier(stage, state)
            wallet += remaining * gross_rate * (1.0 - reserve)
            total_elapsed += remaining
            stage_elapsed += remaining
            for message in grant_due(stage, stage_elapsed, granted, special):
                print(f"  {total_elapsed:7.1f}s  SPECIAL {'':<17} {message}; inventory={format_special(special)}")

        special_locked = [
            skill for skill in skills.values()
            if skill["id"] not in purchased
            and staged(skill, world)
            and prerequisites_met(skill, purchased)
            and skill.get("special_costs")
            and not special_affordable(skill, special)
        ]
        if special_locked:
            print("  special locks: " + ", ".join(skill["display_name"] for skill in special_locked))

        print(
            f"  stage_out: +{len(purchased) - stage_start_purchased} skills, "
            f"wallet {stage_start_wallet:.1f}->{wallet:.1f}, special={format_special(special)}, elapsed={stage_elapsed:.1f}s\n"
        )

    remaining = [skill for skill in skills.values() if skill["id"] not in purchased]
    print("=== SUMMARY ===")
    print(
        f"purchased={len(purchased)}/{len(skills)}  spent={total_spent:.0f}  wallet={wallet:.0f}  "
        f"modeled_time={total_elapsed / 60.0:.1f}m"
    )
    print(
        f"payout: global=x{state['resource']:.3f} precious=x{state['precious']:.3f} "
        f"crit={state['crit_chance']:.1%}@x{state['crit_multiplier']:.1f}"
    )
    print(
        f"throughput stats: manual=x{state['manual_rate']:.3f} automation=x{state['automation_rate']:.3f} "
        f"shovel=x{state['shovel_rate']:.3f} cloud=x{state['cloud_rate']:.3f} "
        f"meteor=x{state['meteor_rate']:.3f} orb=x{state['orb_rate']:.3f}"
    )
    print(f"special={format_special(special)}  timing_warnings={warning_count}")
    if remaining:
        print("unbought:")
        for skill in sorted(remaining, key=lambda item: (item.get("grid_y", 0), item.get("cost", 0))):
            missing = [p["node_id"] for p in skill.get("prerequisites", []) if p["node_id"] not in purchased]
            special_missing = [
                f"{cost['resource_id']} {special.get(cost['resource_id'], 0)}/{cost['amount']}"
                for cost in skill.get("special_costs", [])
                if special.get(cost["resource_id"], 0) < int(cost["amount"])
            ]
            reason_parts = []
            if missing:
                reason_parts.append("requires " + ", ".join(missing))
            if special_missing:
                reason_parts.append("special " + ", ".join(special_missing))
            print(f"  - {skill['display_name']} ({skill['cost']})" + (" " + "; ".join(reason_parts) if reason_parts else ""))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
