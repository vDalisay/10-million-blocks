#!/usr/bin/env python3
"""One-shot authoring helper for the 40^3/50^3 passive incremental branches.

This is intentionally idempotent. It exists only to let the balancing pass update the large authored
JSON graph without hand-copying the entire file through an external editor. Delete after the generated
content has been committed.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKILL_PATH = ROOT / "data/skills/skill_tree.json"
WORLD_PATH = ROOT / "data/worlds/worlds.json"
VALIDATE_CONTENT = ROOT / "tools/validate_content.py"
VALIDATE_PROGRESSION = ROOT / "tools/validate_progression_contracts.py"
THEME_PATH = ROOT / "src/UI/SkillTreeIncrementalTheme.cs"


def load(path: Path):
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def save(path: Path, value) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def node(
    ident: str,
    name: str,
    description: str,
    x: int,
    y: int,
    category: str,
    cost: int,
    prerequisites: list[str],
    effect_type: str,
    effect_value: float,
):
    return {
        "id": ident,
        "display_name": name,
        "description": description,
        "grid_x": x,
        "grid_y": y,
        "category": category,
        "purchase_mode": "once",
        "prerequisites": [
            {"node_id": prerequisite, "required_rank": 1} for prerequisite in prerequisites
        ],
        "hide_until_prerequisites_met": True,
        "cost": cost,
        "max_rank": 1,
        "effects": [{"type": effect_type, "value": effect_value}],
    }


def update_skill_tree() -> bool:
    document = load(SKILL_PATH)
    nodes = document["nodes"]
    existing = {item["id"] for item in nodes}
    additions = [
        node(
            "radioactive_cloud_frequency_1",
            "Radioactive Frequency",
            "Radioactive Clouds pulse 50% faster, cutting the passive corrosion interval from 6.0 seconds to 4.0 seconds.",
            10, 18, "events", 10500,
            ["radioactive_cloud_unlock"],
            "multiply_radioactive_cloud_rate", 1.5,
        ),
        node(
            "radioactive_cloud_radius_1",
            "Radioactive Radius",
            "Increase the passive Radioactive Cloud corrosion radius from 1 to 2 blocks for a much larger pseudo-idle bite.",
            10, 20, "events", 12500,
            ["radioactive_cloud_frequency_1"],
            "add_radioactive_cloud_radius", 1.0,
        ),
        node(
            "orb_breaker_split_1",
            "Orb Split",
            "Spawn a second autonomous Orb Breaker. Both orbs choose separate deterministic routes and mine on every pulse.",
            13, 18, "events", 14000,
            ["orb_breaker_unlock"],
            "add_orb_breaker_count", 1.0,
        ),
        node(
            "orb_breaker_speed_2",
            "Orb Breaker Speed II",
            "Another 35% Orb Breaker speed increase. Combined with the first speed node, pulses arrive about every 1.23 seconds.",
            12, 20, "events", 15000,
            ["orb_breaker_speed_1", "orb_breaker_split_1"],
            "multiply_orb_breaker_rate", 1.35,
        ),
        node(
            "orb_breaker_radius_1",
            "Orb Breaker Radius",
            "Increase every Orb Breaker impact radius from 1 to 2 blocks, making each autonomous pulse substantially wider.",
            13, 21, "events", 17000,
            ["orb_breaker_split_1"],
            "add_orb_breaker_radius", 1.0,
        ),
        node(
            "orb_breaker_swarm",
            "Orb Swarm",
            "Finale capstone: add a third Orb Breaker after completing the split, speed and radius branches.",
            13, 23, "finale", 24000,
            ["orb_breaker_speed_2", "orb_breaker_radius_1"],
            "add_orb_breaker_count", 1.0,
        ),
    ]

    missing = [item for item in additions if item["id"] not in existing]
    if not missing:
        return False

    finale_index = next((i for i, item in enumerate(nodes) if item.get("category") == "finale"), len(nodes))
    normal = [item for item in missing if item["category"] != "finale"]
    finale = [item for item in missing if item["category"] == "finale"]
    nodes[finale_index:finale_index] = normal
    nodes.extend(finale)
    document["content_version"] = max(int(document.get("content_version", 0)) + 1, 17)
    save(SKILL_PATH, document)
    return True


def update_worlds() -> bool:
    document = load(WORLD_PATH)
    changed = False
    for world in document["worlds"]:
        if world.get("id") != "reference_ridges":
            continue
        visible = world.setdefault("visibleSkillIds", [])
        if "orb_breaker_swarm" not in visible:
            visible.append("orb_breaker_swarm")
            changed = True
    if changed:
        save(WORLD_PATH, document)
    return changed


def replace_once(path: Path, old: str, new: str) -> bool:
    text = path.read_text(encoding="utf-8")
    if new in text:
        return False
    if old not in text:
        raise RuntimeError(f"Expected authoring anchor missing from {path}: {old!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    return True


def update_validators_and_theme() -> bool:
    changed = False
    changed |= replace_once(
        VALIDATE_CONTENT,
        '    "unlock_radioactive_cloud",\n    "unlock_orb_breaker",',
        '    "unlock_radioactive_cloud",\n    "multiply_radioactive_cloud_rate",\n    "add_radioactive_cloud_radius",\n    "unlock_orb_breaker",\n    "add_orb_breaker_count",\n    "add_orb_breaker_radius",',
    )
    changed |= replace_once(
        VALIDATE_CONTENT,
        'assert set(finale.get("visibleSkillIds", [])) == {"miner_speed_4", "lightning_chain_2", "meteor_radius_2", "manual_aftershock"}',
        'assert set(finale.get("visibleSkillIds", [])) == {"miner_speed_4", "lightning_chain_2", "meteor_radius_2", "manual_aftershock", "orb_breaker_swarm"}',
    )
    changed |= replace_once(
        VALIDATE_PROGRESSION,
        '    "manual_aftershock",\n}',
        '    "manual_aftershock",\n    "orb_breaker_swarm",\n}',
    )
    changed |= replace_once(
        THEME_PATH,
        '                "unlock_radioactive_cloud" or\n                "unlock_orb_breaker"))',
        '                "unlock_radioactive_cloud" or\n                "unlock_orb_breaker" or\n                "add_orb_breaker_count"))',
    )
    changed |= replace_once(
        THEME_PATH,
        '                "multiply_cloud_charge_rate" or\n                "multiply_orb_breaker_rate" or',
        '                "multiply_cloud_charge_rate" or\n                "multiply_radioactive_cloud_rate" or\n                "add_radioactive_cloud_radius" or\n                "multiply_orb_breaker_rate" or\n                "add_orb_breaker_radius" or',
    )
    changed |= replace_once(
        THEME_PATH,
        '        ["radioactive_cloud_unlock"] = 19,\n        ["orb_breaker_unlock"] = 20,\n        ["orb_breaker_speed_1"] = 20,',
        '        ["radioactive_cloud_unlock"] = 19,\n        ["radioactive_cloud_frequency_1"] = 19,\n        ["radioactive_cloud_radius_1"] = 19,\n        ["orb_breaker_unlock"] = 20,\n        ["orb_breaker_split_1"] = 20,\n        ["orb_breaker_speed_1"] = 20,\n        ["orb_breaker_speed_2"] = 20,\n        ["orb_breaker_radius_1"] = 20,\n        ["orb_breaker_swarm"] = 20,',
    )
    return changed


def main() -> int:
    changed = update_skill_tree()
    changed |= update_worlds()
    changed |= update_validators_and_theme()
    print("passive skill expansion applied" if changed else "passive skill expansion already applied")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
