#!/usr/bin/env python3
"""Small cross-world checks for progression contracts that should fail before Godot boots."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load(relative: str):
    with (ROOT / relative).open("r", encoding="utf-8") as handle:
        return json.load(handle)


worlds_doc = load("data/worlds/worlds.json")
progression_doc = load("data/progression/world_progression.json")
skills_doc = load("data/skills/skill_tree.json")
worlds = {item["id"]: item for item in worlds_doc["worlds"]}
skills = {item["id"]: item for item in skills_doc["nodes"]}
order = progression_doc["world_ids"]

tutorial_ids = [
    "tutorial_single_block",
    "tutorial_dirt_5",
    "tutorial_lake_core_10",
    "tutorial_trees_gem_15",
]
expected_order = tutorial_ids + ["reference_natural", "reference_lakes", "reference_ridges"]
assert order == expected_order, f"reviewed Steam-demo world order changed unexpectedly: {order}"

expected_dimensions = {
    "tutorial_single_block": 1,
    "tutorial_dirt_5": 5,
    "tutorial_lake_core_10": 10,
    "tutorial_trees_gem_15": 15,
    "reference_natural": 20,
    "reference_lakes": 40,
    "reference_ridges": 50,
}
for world_id, dimension in expected_dimensions.items():
    profile = worlds[world_id]
    actual = [int(profile.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")]
    assert actual == [dimension, dimension, dimension], (
        f"{world_id} must remain {dimension} x {dimension} x {dimension}, got {actual}"
    )

for world_id, profile in worlds.items():
    assert int(profile.get("worldVersion", 1)) > 0, f"{world_id} must have a positive worldVersion"
    assert int(profile.get("generationVersion", 0)) > 0, f"{world_id} must have a positive generationVersion"
    scope = profile.get("currencyScope", "persistent_main")
    assert scope in {"tutorial_local", "persistent_main"}, f"{world_id} has unknown currencyScope {scope!r}"

# Ordinary resources follow the player from the first tutorial through the finale. Skill prices are
# intentionally balanced against this persistent wallet rather than being reset per world.
for world_id in order:
    assert worlds[world_id].get("currencyScope", "persistent_main") == "persistent_main", (
        f"Steam-demo world {world_id} must use the persistent ordinary-resource wallet"
    )

assert int(worlds["tutorial_single_block"].get("targetMineableBlocks", 0)) == 1
assert int(worlds["tutorial_dirt_5"].get("targetMineableBlocks", 0)) == 125
assert int(worlds["tutorial_lake_core_10"].get("targetMineableBlocks", 0)) == 1000
assert int(worlds["tutorial_trees_gem_15"].get("targetMineableBlocks", 0)) == 3375

# Tutorials deliberately expose only the mechanic families being taught at that size.
assert worlds["tutorial_dirt_5"].get("visibleSkillCategories", []) == ["manual"]
assert worlds["tutorial_lake_core_10"].get("visibleSkillCategories", []) == ["manual", "shovel"]
assert worlds["tutorial_trees_gem_15"].get("visibleSkillCategories", []) == [
    "manual", "shovel", "automation", "drill", "patterns"
], "15-cube tutorial must keep Forest Cutter hidden while teaching tree obstruction"
assert "forest" not in worlds["tutorial_trees_gem_15"].get("visibleSkillCategories", [])

# The 20-cube generated world introduces the normal full mining/economy toolset but no weather-event
# progression yet. The 40-cube world opens the entire events family; the 50-cube finale adds a small
# exact-ID capstone set whose category stays hidden everywhere else.
reference_natural = worlds["reference_natural"]
storm = worlds["reference_lakes"]
finale = worlds["reference_ridges"]

assert "forest" in reference_natural.get("visibleSkillCategories", []), (
    "Forest Cutter must first become available in the 20-cube main world"
)
assert "events" not in reference_natural.get("visibleSkillCategories", []), (
    "active-event progression must not leak into the 20-cube world"
)
assert reference_natural.get("visibleSkillIds", []) == []
assert int(reference_natural.get("worldVersion", 0)) == 3
assert reference_natural.get("overrideFile") == "res://data/worlds/overrides/reference_natural_v3.json"

assert "events" in storm.get("visibleSkillCategories", []), (
    "40-cube Stormfront must reveal the event-upgrade family"
)
assert storm.get("visibleSkillIds", []) == [], (
    "40-cube should expose events through its category rather than leaking 50-cube capstone IDs"
)

expected_finale_ids = {
    "miner_speed_4",
    "lightning_chain_2",
    "meteor_radius_2",
    "manual_aftershock",
    "orb_breaker_swarm",
}
assert "events" in finale.get("visibleSkillCategories", [])
assert set(finale.get("visibleSkillIds", [])) == expected_finale_ids, (
    f"50-cube finale must expose exactly the capstone nodes, got {finale.get('visibleSkillIds', [])}"
)

# The two newest literal-reference adaptations belong to the 40-cube era through normal category
# staging and prerequisite disclosure, not the 20-cube world or exact-ID finale gate.
assert skills["radioactive_cloud_unlock"].get("category") == "events"
assert skills["radioactive_cloud_unlock"].get("prerequisites") == [
    {"node_id": "cloud_charger_unlock", "required_rank": 1}
]
assert skills["drill_gem_bit"].get("category") == "drill"
assert {item["node_id"] for item in skills["drill_gem_bit"].get("prerequisites", [])} == {
    "drill_ore_bit", "precious_yield_1"
}
assert "events" not in reference_natural.get("visibleSkillCategories", [])
assert "events" in storm.get("visibleSkillCategories", [])

for world_id in ("stress_1000", "final_target_1m"):
    assert int(worlds[world_id].get("targetMineableBlocks", 0)) == 1_000_000, (
        f"{world_id} must keep exactly one million authoritative mineable blocks"
    )

assert "final_target_1m" not in order, "full-release one-million target must remain outside Steam-demo progression"
assert len(order) == len(set(order)), "world progression contains duplicate world ids"
for world_id in order:
    assert world_id in worlds, f"progression references missing world {world_id}"

print(
    f"progression contracts passed: {len(order)} Steam-demo worlds share one persistent ordinary-resource wallet; "
    f"events begin at 40-cube and {len(expected_finale_ids)} capstones stage at 50-cube"
)
