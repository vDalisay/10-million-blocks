#!/usr/bin/env python3
"""Fast repository-level checks for data-driven runtime content.

This intentionally duplicates only cross-file invariants that can fail before Godot starts. Runtime
C# validation remains authoritative for richer behavior, but CI should catch missing assets, dangling
skill/miner references and progression/generator contract regressions before the game boots.
"""
from __future__ import annotations

import json
import math
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
manual_footprints = {"single", "plus_3", "square_3", "square_10"}
known_effects = {
    "add_manual_blocks_per_click",
    "multiply_manual_mining_rate",
    "set_manual_footprint",
    "unlock_hover_mining",
    "multiply_miner_rate",
    "multiply_shovel_rate",
    "unlock_miner",
    "unlock_pattern",
    "set_drill_pattern",
    "set_drill_material_tier",
    "set_miner_pattern_width",
    "set_shovel_height_tolerance",
    "set_shovel_search_radius",
    "unlock_resource_filter",
}

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
        assert effect_type in known_effects, f"skill {ident} references unknown effect {effect_type}"
        if effect_type == "unlock_miner":
            assert string_value in miners, f"skill {ident} unlocks missing miner {string_value}"
        elif effect_type in {"unlock_pattern", "set_drill_pattern"}:
            assert string_value in patterns, f"skill {ident} references missing pattern {string_value}"
        elif effect_type == "set_manual_footprint":
            assert string_value in manual_footprints, (
                f"skill {ident} references unknown manual footprint {string_value}"
            )

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

for world_id, profile in worlds.items():
    assert int(profile.get("generationVersion", 0)) > 0, (
        f"world {world_id} must commit a positive generationVersion"
    )
    assert profile.get("generationMode", "procedural") in {"procedural", "single_block", "solid_cube"}, (
        f"world {world_id} has an unknown generationMode"
    )

tutorial_ids = progression_doc["world_ids"][:2]
assert tutorial_ids == ["tutorial_single_block", "tutorial_dirt_5"], (
    f"expected the first two tutorial worlds, got {tutorial_ids}"
)

single = worlds["tutorial_single_block"]
assert single.get("generationMode") == "single_block", "opening tutorial must use single_block generation"
assert int(single.get("targetMineableBlocks", 0)) == 1, "opening tutorial must target exactly one block"
assert [int(single.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [1, 1, 1], (
    "opening tutorial must remain 1 x 1 x 1"
)
assert single.get("skillTreeAvailable") is False, "opening tutorial must hide the skill tree"
assert single.get("automationAvailable") is False, "opening tutorial must hide automation"

manual = worlds["tutorial_dirt_5"]
assert manual.get("generationMode") == "solid_cube", "5x5 tutorial must use deterministic solid_cube generation"
assert [int(manual.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [5, 5, 5], (
    "manual tutorial must remain 5 x 5 x 5 while its initial lesson is being validated"
)
assert int(manual.get("targetMineableBlocks", 0)) == 125, "5x5 solid tutorial must contain 125 blocks"
assert manual.get("automationAvailable") is False, "manual tutorial must hide automation"
assert manual.get("visibleSkillCategories") == ["manual"], "manual tutorial must expose only the manual skill branch"
assert manual.get("surfaceBlock") == "dirt", "manual tutorial must remain a dirt practice cube"

# Provisional authored worlds stay available after the two implemented tutorial slices until their
# reviewed replacements are authored. Keeping them explicit avoids accidentally jumping to the finale.
early_worlds = progression_doc["world_ids"][2:5]
assert early_worlds == ["reference_natural", "reference_lakes", "reference_ridges"], (
    f"expected provisional authored bridge worlds after tutorials, got {early_worlds}"
)
for world_id in early_worlds:
    profile = worlds[world_id]
    assert profile.get("rendererMode", "eager") != "full_surface", (
        f"early-game world {world_id} must stay on the normal authored-scale renderer"
    )
    assert max(
        int(profile.get("logicalWidth", 0)),
        int(profile.get("logicalHeight", 0)),
        int(profile.get("logicalDepth", 0)),
    ) <= 48, f"early-game world {world_id} unexpectedly grew into a stress-scale profile"
    assert str(profile.get("introText", "")).strip(), (
        f"early-game world {world_id} needs authored introText explaining its gameplay role"
    )

for world_id in ("stress_1000", "final_target_1m"):
    profile = worlds[world_id]
    assert int(profile.get("targetMineableBlocks", 0)) == 1_000_000, (
        f"{world_id} must keep the exact one-million authoritative target"
    )
    assert profile.get("rendererMode") == "full_surface", (
        f"{world_id} must render real block-scale surface geometry, not the macro proxy"
    )

    max_coordinate = math.ceil(
        float(profile.get("baseRadius", 0))
        + float(profile.get("terrainAmplitude", 0))
        + float(profile.get("detailAmplitude", 0))
        + max(0.0, float(profile.get("seaLevelOffset", 0)))
        + 3.0
    )
    assert int(miners["line_miner"].get("range", 0)) > max_coordinate * 2 + 1, (
        "primary Drill safety range must exceed the full physical diameter so normal termination "
        f"comes from the world boundary ({world_id} requires > {max_coordinate * 2 + 1})"
    )

assert progression_doc["world_ids"][-1] == "final_target_1m", (
    "final_target_1m must remain the configured progression end goal"
)

for shovel_surface in ("grass", "dirt_grass", "dirt"):
    assert "sand" in set(blocks[shovel_surface].get("tags", [])), (
        f"{shovel_surface} must remain valid shovel terrain via the sand tag"
    )
assert blocks["grass"].get("asset_path") == blocks["dirt_grass"].get("asset_path"), (
    "grass terrain should keep the dirt-backed grass mesh so exposed side/interior faces remain soil"
)
assert miners["line_miner"].get("tool_class") == "drill", "line_miner must remain the primary Drill"


def effect_values(skill_id: str, effect_type: str):
    return [effect.get("value") for effect in skills[skill_id].get("effects", []) if effect.get("type") == effect_type]


def effect_strings(skill_id: str, effect_type: str):
    return [effect.get("string_value", "") for effect in skills[skill_id].get("effects", []) if effect.get("type") == effect_type]


assert effect_values("drill_hardened_bit", "set_drill_material_tier") == [1.0], (
    "Hardened Bit must establish Drill material tier 1"
)
assert effect_values("drill_ore_bit", "set_drill_material_tier") == [2.0], (
    "Ore-Cutting Bit must establish Drill material tier 2"
)
assert effect_strings("manual_2x", "set_manual_footprint") == ["plus_3"], (
    "first manual area upgrade must be the 3x3 plus footprint"
)
assert effect_strings("manual_3x", "set_manual_footprint") == ["square_3"], (
    "second manual area upgrade must be the full 3x3 footprint"
)
assert any(effect.get("type") == "unlock_hover_mining" for effect in skills["hover_mining_unlock"].get("effects", [])), (
    "hover_mining_unlock must expose the no-button hover mining mode"
)

print(
    f"content validation passed: {len(blocks)} blocks, {len(miners)} miners, "
    f"{len(skills)} skills, {len(worlds)} worlds"
)
