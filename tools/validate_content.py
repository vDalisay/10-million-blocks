#!/usr/bin/env python3
"""Fast repository-level checks for data-driven runtime content.

This intentionally duplicates only cross-file invariants that can fail before Godot starts. Runtime
C# validation remains authoritative for richer behavior, but CI should catch missing assets, dangling
skill/miner references and accidental regressions of the one-million real-block renderer direction.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load(relative: str):
    with (ROOT / relative).open("r", encoding="utf-8") as fh:
        return json.load(fh)


def unique_by_id(items, label: str):
    result = {}
    for item in items:
        ident = item.get("id")
        assert ident, f"{label} contains an empty id"
        assert ident not in result, f"duplicate {label} id: {ident}"
        result[ident] = item
    return result


blocks_doc = load("data/blocks/blocks.json")
miners_doc = load("data/miners/miners.json")
skills_doc = load("data/skills/skill_tree.json")
worlds_doc = load("data/worlds/worlds.json")
progression_doc = load("data/progression/world_progression.json")

blocks = unique_by_id(blocks_doc["blocks"], "block")
miners = unique_by_id(miners_doc["miners"], "miner")
skills = unique_by_id(skills_doc["nodes"], "skill")
worlds = unique_by_id(worlds_doc["worlds"], "world")
patterns = {"line", "wide_line", "disc", "surface_strip"}

# Asset existence is otherwise discovered only when BlockAssetRegistry preloads inside Godot.
for ident, block in blocks.items():
    asset = block.get("asset_path", "")
    assert asset.startswith("res://"), f"block {ident} has non-res asset path: {asset}"
    disk_path = ROOT / asset.removeprefix("res://")
    assert disk_path.exists(), f"block {ident} references missing asset: {asset}"

for ident, miner in miners.items():
    pattern = miner.get("pattern_id")
    assert pattern in patterns, f"miner {ident} references unknown pattern {pattern}"
    assert float(miner.get("base_rate", 0)) > 0, f"miner {ident} must have positive base_rate"

for ident, skill in skills.items():
    max_rank = int(skill.get("max_rank", 1))
    assert max_rank > 0, f"skill {ident} has invalid max_rank"
    for prerequisite in skill.get("prerequisites", []):
        source_id = prerequisite.get("node_id")
        assert source_id in skills, f"skill {ident} references missing prerequisite {source_id}"
        required = int(prerequisite.get("required_rank", 1))
        assert 1 <= required <= int(skills[source_id].get("max_rank", 1)), (
            f"skill {ident} requires invalid rank {required} from {source_id}"
        )

    for effect in skill.get("effects", []):
        effect_type = effect.get("type")
        string_value = effect.get("string_value", "")
        if effect_type == "unlock_miner":
            assert string_value in miners, f"skill {ident} unlocks missing miner {string_value}"
        elif effect_type in {"unlock_pattern", "set_drill_pattern"}:
            assert string_value in patterns, f"skill {ident} references missing pattern {string_value}"

# Cycle detection for prerequisite graph.
visiting: set[str] = set()
visited: set[str] = set()


def visit(skill_id: str):
    if skill_id in visited:
        return
    assert skill_id not in visiting, f"skill prerequisite cycle at {skill_id}"
    visiting.add(skill_id)
    for prerequisite in skills[skill_id].get("prerequisites", []):
        visit(prerequisite["node_id"])
    visiting.remove(skill_id)
    visited.add(skill_id)


for skill_id in skills:
    visit(skill_id)

for world_id in progression_doc["world_ids"]:
    assert world_id in worlds, f"progression references missing world {world_id}"

# Product-direction guardrail: future refactors must not silently put the final million-block world
# back onto the old macro-cell presentation.
for world_id in ("stress_1000", "final_target_1m"):
    profile = worlds[world_id]
    assert int(profile.get("targetMineableBlocks", 0)) == 1_000_000, (
        f"{world_id} must keep the exact one-million authoritative target"
    )
    assert profile.get("rendererMode") == "full_surface", (
        f"{world_id} must render real block-scale surface geometry, not the macro proxy"
    )

assert progression_doc["world_ids"][-1] == "final_target_1m", (
    "final_target_1m must remain the configured progression end goal"
)

print(
    f"content validation passed: {len(blocks)} blocks, {len(miners)} miners, "
    f"{len(skills)} skills, {len(worlds)} worlds"
)
