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
worlds = {item["id"]: item for item in worlds_doc["worlds"]}
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

for world_id in tutorial_ids:
    assert worlds[world_id].get("currencyScope") == "tutorial_local", (
        f"tutorial world {world_id} must use an isolated tutorial-local wallet"
    )

for world_id in order[4:]:
    assert worlds[world_id].get("currencyScope", "persistent_main") == "persistent_main", (
        f"post-tutorial world {world_id} must use the persistent main-game wallet"
    )

assert int(worlds["tutorial_single_block"].get("targetMineableBlocks", 0)) == 1
assert int(worlds["tutorial_dirt_5"].get("targetMineableBlocks", 0)) == 125
assert int(worlds["tutorial_lake_core_10"].get("targetMineableBlocks", 0)) == 1000
assert int(worlds["tutorial_trees_gem_15"].get("targetMineableBlocks", 0)) == 3375

assert worlds["reference_natural"].get("visibleSkillIds", []) == [], (
    "active-event automation must not leak into the 20-cube world"
)
for world_id in ("reference_lakes", "reference_ridges"):
    assert worlds[world_id].get("visibleSkillIds", []) == ["cloud_charger_unlock"], (
        f"{world_id} must expose Cloud Charger"
    )

for world_id in ("stress_1000", "final_target_1m"):
    assert int(worlds[world_id].get("targetMineableBlocks", 0)) == 1_000_000, (
        f"{world_id} must keep exactly one million authoritative mineable blocks"
    )

assert "final_target_1m" not in order, "full-release one-million target must remain outside Steam-demo progression"
assert len(order) == len(set(order)), "world progression contains duplicate world ids"
for world_id in order:
    assert world_id in worlds, f"progression references missing world {world_id}"

print(
    f"progression contracts passed: {len(tutorial_ids)} tutorial-local worlds, "
    f"{len(order) - len(tutorial_ids)} persistent progression worlds"
)
