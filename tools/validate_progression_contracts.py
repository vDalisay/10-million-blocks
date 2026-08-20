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
assert order[:4] == tutorial_ids, f"tutorial prefix changed unexpectedly: {order[:4]}"

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

assert len(order) == len(set(order)), "world progression contains duplicate world ids"
for world_id in order:
    assert world_id in worlds, f"progression references missing world {world_id}"

print(
    f"progression contracts passed: {len(tutorial_ids)} tutorial-local worlds, "
    f"{len(order) - len(tutorial_ids)} persistent progression worlds"
)
