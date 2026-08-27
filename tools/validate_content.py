#!/usr/bin/env python3
"""Fast repository-level checks for data-driven runtime content."""
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
manual_footprints = {"single", "plus_3", "square_3", "square_5", "square_10"}
known_effects = {
    "add_manual_blocks_per_click",
    "multiply_manual_mining_rate",
    "set_manual_mining_power",
    "set_manual_penetration_depth",
    "set_manual_footprint",
    "unlock_hover_mining",
    "unlock_laser",
    "multiply_laser_manual_charge_rate",
    "multiply_laser_auto_charge_rate",
    "multiply_laser_damage",
    "set_laser_beam_radius",
    "set_laser_duration_seconds",
    "set_laser_cooldown_seconds",
    "unlock_laser_resource_burn",
    "set_laser_resource_cost_per_second",
    "multiply_laser_resource_cost",
    "set_collection_radius_blocks",
    "multiply_collection_rate",
    "unlock_manual_auto_collect",
    "unlock_automation_auto_collect",
    "multiply_resource_yield",
    "multiply_precious_resource_yield",
    "add_critical_yield_chance",
    "set_critical_yield_multiplier",
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
    "unlock_auto_cloud_charger",
    "unlock_radioactive_cloud",
    "multiply_radioactive_cloud_rate",
    "add_radioactive_cloud_radius",
    "unlock_orb_breaker",
    "add_orb_breaker_count",
    "add_orb_breaker_radius",
    "multiply_orb_breaker_rate",
    "multiply_cloud_charge_rate",
    "add_lightning_radius",
    "add_lightning_chain_count",
    "multiply_meteor_spawn_rate",
    "add_meteor_radius",
}

for ident, block in blocks.items():
    asset = block.get("asset_path", "")
    assert asset.startswith("res://"), f"block {ident} has non-res asset path: {asset}"
    assert (ROOT / asset.removeprefix("res://")).exists(), f"block {ident} references missing asset: {asset}"

for ident, miner in miners.items():
    assert miner.get("pattern_id") in patterns, f"miner {ident} references unknown pattern"
    assert int(miner.get("unit_price", 0)) > 0, f"miner {ident} must have a fixed positive unit_price"
    assert float(miner.get("base_rate", 0)) > 0, f"miner {ident} must have positive base_rate"

for ident, skill in skills.items():
    max_rank = int(skill.get("max_rank", 1))
    assert max_rank > 0, f"skill {ident} has invalid max_rank"
    mode = skill.get("purchase_mode", "once")
    assert mode in {"once", "repeatable"}, f"skill {ident} has unknown purchase mode {mode}"
    if mode == "once":
        assert max_rank == 1, f"one-time skill {ident} must have max_rank 1"
    for prerequisite in skill.get("prerequisites", []):
        source_id = prerequisite.get("node_id")
        assert source_id in skills, f"skill {ident} references missing prerequisite {source_id}"
        required = int(prerequisite.get("required_rank", 1))
        assert 1 <= required <= int(skills[source_id].get("max_rank", 1)), f"skill {ident} has invalid prerequisite rank"

    special_ids = set()
    for special_cost in skill.get("special_costs", []):
        resource_id = special_cost.get("resource_id", "")
        amount = int(special_cost.get("amount", 0))
        assert resource_id in blocks, f"skill {ident} special cost references missing resource {resource_id}"
        assert "gem" in set(blocks[resource_id].get("tags", [])), f"skill {ident} special cost {resource_id} is not a gem"
        assert amount > 0, f"skill {ident} special cost {resource_id} must be positive"
        assert resource_id not in special_ids, f"skill {ident} repeats special cost {resource_id}"
        special_ids.add(resource_id)

    for effect in skill.get("effects", []):
        effect_type = effect.get("type")
        string_value = effect.get("string_value", "")
        assert effect_type in known_effects, f"skill {ident} references unknown effect {effect_type}"
        if effect_type == "unlock_miner":
            assert string_value in miners, f"skill {ident} unlocks missing miner {string_value}"
        elif effect_type in {"unlock_pattern", "set_drill_pattern"}:
            assert string_value in patterns, f"skill {ident} references missing pattern {string_value}"
        elif effect_type == "set_manual_footprint":
            assert string_value in manual_footprints, f"skill {ident} references unknown manual footprint {string_value}"

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

world_override_docs = {}
for world_id, profile in worlds.items():
    assert int(profile.get("worldVersion", 0)) > 0, f"world {world_id} must commit a positive worldVersion"
    assert int(profile.get("generationVersion", 0)) > 0, f"world {world_id} must commit a positive generationVersion"
    assert profile.get("generationMode", "procedural") in {"procedural", "single_block", "solid_cube"}, f"world {world_id} has unknown generationMode"
    visible_ids = profile.get("visibleSkillIds", [])
    assert len(visible_ids) == len(set(visible_ids)), f"world {world_id} repeats a visibleSkillId"
    for skill_id in visible_ids:
        assert skill_id in skills, f"world {world_id} exposes missing skill id {skill_id}"

    override_file = str(profile.get("overrideFile", "")).strip()
    if not override_file:
        continue
    assert override_file.startswith("res://"), f"world {world_id} overrideFile must be a res:// path"
    override_path = ROOT / override_file.removeprefix("res://")
    assert override_path.exists(), f"world {world_id} references missing override file {override_file}"
    override_doc = load(str(override_path.relative_to(ROOT)))
    world_override_docs[world_id] = override_doc
    assert int(override_doc.get("schemaVersion", 0)) == 1
    assert override_doc.get("worldId") == world_id
    assert int(override_doc.get("generationVersion", 0)) == int(profile.get("generationVersion", 0))

    seen_coordinates = set()
    for item in override_doc.get("overrides", []):
        coordinate = (int(item.get("x", 0)), int(item.get("y", 0)), int(item.get("z", 0)))
        assert coordinate not in seen_coordinates, f"world {world_id} has duplicate override at {coordinate}"
        seen_coordinates.add(coordinate)
        if item.get("present", True):
            assert item.get("blockId") in blocks, f"world {world_id} override {coordinate} references missing block"

    seen_features = set()
    for item in override_doc.get("features", []):
        coordinate = (int(item.get("x", 0)), int(item.get("y", 0)), int(item.get("z", 0)))
        normal = (int(item.get("normalX", 0)), int(item.get("normalY", 1)), int(item.get("normalZ", 0)))
        assert coordinate not in seen_features, f"world {world_id} has duplicate feature at {coordinate}"
        seen_features.add(coordinate)
        assert item.get("blockId") == "tree"
        assert sum(abs(value) for value in normal) == 1

expected_tutorial_ids = ["tutorial_single_block", "tutorial_dirt_5", "tutorial_lake_core_10", "tutorial_trees_gem_15"]
assert progression_doc["world_ids"][:4] == expected_tutorial_ids

single = worlds["tutorial_single_block"]
assert single.get("generationMode") == "single_block"
assert int(single.get("targetMineableBlocks", 0)) == 1
assert [int(single.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [1, 1, 1]
assert single.get("skillTreeAvailable") is False and single.get("automationAvailable") is False

manual = worlds["tutorial_dirt_5"]
assert manual.get("generationMode") == "solid_cube"
assert [int(manual.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [5, 5, 5]
assert int(manual.get("targetMineableBlocks", 0)) == 125
assert manual.get("automationAvailable") is False
assert manual.get("visibleSkillCategories") == ["manual"]
assert manual.get("surfaceBlock") == "dirt"

lake = worlds["tutorial_lake_core_10"]
assert lake.get("generationMode") == "solid_cube"
assert [int(lake.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [10, 10, 10]
assert int(lake.get("targetMineableBlocks", 0)) == 1000
assert lake.get("automationAvailable") is True
assert lake.get("visibleSkillCategories") == ["manual", "shovel"]
lake_overrides = world_override_docs["tutorial_lake_core_10"]["overrides"]
lake_water = [item for item in lake_overrides if item.get("blockId") in {"water", "water_shallow", "water_deep"}]
lake_stone = [item for item in lake_overrides if item.get("blockId") in {"stone", "stone_dark"}]
assert len(lake_water) == 16
assert len(lake_stone) == 64

trees_gem = worlds["tutorial_trees_gem_15"]
assert trees_gem.get("generationMode") == "solid_cube"
assert [int(trees_gem.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [15, 15, 15]
assert int(trees_gem.get("targetMineableBlocks", 0)) == 3375
assert trees_gem.get("visibleSkillCategories") == ["manual", "shovel", "automation", "drill", "patterns"]
trees_doc = world_override_docs["tutorial_trees_gem_15"]
trees_overrides = trees_doc["overrides"]
red_gems = [item for item in trees_overrides if item.get("blockId") == "gem_red"]
assert len(red_gems) == 1 and (red_gems[0]["x"], red_gems[0]["y"], red_gems[0]["z"]) == (0, 0, 0)
assert len([item for item in trees_overrides if item.get("blockId") in {"water", "water_shallow", "water_deep"}]) == 25
assert len(trees_doc.get("features", [])) == 8

demo_worlds = progression_doc["world_ids"][4:]
assert demo_worlds == ["reference_natural", "reference_lakes", "reference_ridges"]
expected_dimensions = {"reference_natural": 20, "reference_lakes": 40, "reference_ridges": 50}
for world_id, dimension in expected_dimensions.items():
    profile = worlds[world_id]
    actual = [int(profile.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")]
    assert actual == [dimension, dimension, dimension]
    assert profile.get("rendererMode", "eager") != "full_surface"
    assert profile.get("currencyScope", "persistent_main") == "persistent_main"
    assert str(profile.get("introText", "")).strip()

reference_natural = worlds["reference_natural"]
assert reference_natural.get("visibleSkillIds", []) == []
assert "forest" in reference_natural.get("visibleSkillCategories", [])
assert "events" not in reference_natural.get("visibleSkillCategories", [])
assert int(reference_natural.get("worldVersion", 0)) == 3
assert reference_natural.get("overrideFile") == "res://data/worlds/overrides/reference_natural_v3.json"
reference_overrides = world_override_docs["reference_natural"]["overrides"]
reference_gems = [item for item in reference_overrides if item.get("blockId") in {"gem_green", "gem_blue", "gem_red"}]
assert len(reference_gems) == 3
assert {item.get("blockId") for item in reference_gems} == {"gem_green", "gem_blue", "gem_red"}

storm = worlds["reference_lakes"]
finale = worlds["reference_ridges"]
assert "events" in storm.get("visibleSkillCategories", []) and storm.get("visibleSkillIds", []) == []
assert "events" in finale.get("visibleSkillCategories", [])
assert set(finale.get("visibleSkillIds", [])) == {"miner_speed_4", "lightning_chain_2", "meteor_radius_2", "manual_aftershock", "orb_breaker_swarm"}
assert progression_doc["world_ids"][-1] == "reference_ridges"
assert "final_target_1m" not in progression_doc["world_ids"]

for world_id in ("stress_1000", "final_target_1m"):
    profile = worlds[world_id]
    assert int(profile.get("targetMineableBlocks", 0)) == 1_000_000
    assert profile.get("rendererMode") == "full_surface"
    max_coordinate = math.ceil(float(profile.get("baseRadius", 0)) + float(profile.get("terrainAmplitude", 0)) + float(profile.get("detailAmplitude", 0)) + max(0.0, float(profile.get("seaLevelOffset", 0))) + 3.0)
    assert int(miners["line_miner"].get("range", 0)) > max_coordinate * 2 + 1

for shovel_surface in ("grass", "dirt_grass", "dirt"):
    assert "sand" in set(blocks[shovel_surface].get("tags", []))
assert blocks["grass"].get("asset_path") == "res://Assets/gltf/grass.gltf"
assert blocks["dirt_grass"].get("asset_path") == "res://Assets/gltf/dirt_with_grass.gltf"
assert miners["line_miner"].get("tool_class") == "drill"


def effect_values(skill_id: str, effect_type: str):
    return [effect.get("value") for effect in skills[skill_id].get("effects", []) if effect.get("type") == effect_type]


def effect_strings(skill_id: str, effect_type: str):
    return [effect.get("string_value", "") for effect in skills[skill_id].get("effects", []) if effect.get("type") == effect_type]


# Very-late Flux Laser contract. It converges the 50-cube manual/event capstones and stays hidden
# through progressive disclosure until both have actually been purchased.
laser = skills["laser_core"]
assert {p["node_id"] for p in laser.get("prerequisites", [])} == {"manual_aftershock", "orb_breaker_radius_1"}
assert laser.get("hide_until_prerequisites_met") is True
assert [e.get("type") for e in laser.get("effects", [])] == ["unlock_laser"]
assert effect_values("laser_wide_lens", "set_laser_beam_radius") == [2.0]
assert effect_values("laser_cooling", "set_laser_cooldown_seconds") == [50.0]
assert effect_values("laser_hotter_beam", "multiply_laser_damage") == [1.5]
assert effect_values("laser_duration", "set_laser_duration_seconds") == [7.0]
assert effect_values("laser_resource_furnace", "set_laser_resource_cost_per_second") == [300.0]
assert effect_values("laser_furnace_efficiency", "multiply_laser_resource_cost") == [0.6]

assert effect_values("drill_hardened_bit", "set_drill_material_tier") == [1.0]
assert effect_values("drill_ore_bit", "set_drill_material_tier") == [2.0]
assert effect_values("drill_gem_bit", "set_drill_material_tier") == [3.0]
assert effect_strings("manual_2x", "set_manual_footprint") == ["plus_3"]
assert effect_strings("manual_3x", "set_manual_footprint") == ["square_3"]
assert effect_strings("manual_5x", "set_manual_footprint") == ["square_5"]
assert effect_values("manual_power_1", "set_manual_mining_power") == [1.5]
assert effect_values("manual_power_5", "set_manual_mining_power") == [4.0]
assert any(effect.get("type") == "unlock_hover_mining" for effect in skills["hover_mining_unlock"].get("effects", []))
assert skills["wide_bore_unlock"].get("special_costs") == [{"resource_id": "gem_red", "amount": 1}]
assert effect_strings("wide_bore_unlock", "set_drill_pattern") == ["wide_line"]
assert effect_values("wide_bore_unlock", "set_miner_pattern_width") == [3.0]
assert skills["axe_unlock"].get("category") == "forest" and skills["axe_unlock"].get("prerequisites", []) == []
assert skills["cloud_charger_unlock"].get("category") == "events"
assert any(effect.get("type") == "unlock_auto_cloud_charger" for effect in skills["cloud_charger_unlock"].get("effects", []))
assert any(effect.get("type") == "unlock_radioactive_cloud" for effect in skills["radioactive_cloud_unlock"].get("effects", []))
assert any(effect.get("type") == "unlock_orb_breaker" for effect in skills["orb_breaker_unlock"].get("effects", []))
assert effect_values("orb_breaker_speed_1", "multiply_orb_breaker_rate") == [1.5]
assert skills["radioactive_cloud_unlock"].get("prerequisites") == [{"node_id": "cloud_charger_unlock", "required_rank": 1}]
assert skills["orb_breaker_unlock"].get("prerequisites") == [{"node_id": "radioactive_cloud_unlock", "required_rank": 1}]
assert skills["cloud_charger_unlock"].get("prerequisites") == [{"node_id": "pickaxe_unlock", "required_rank": 1}]
assert effect_values("lightning_chain_1", "add_lightning_chain_count") == [1.0]
assert effect_values("meteor_radius_2", "add_meteor_radius") == [2.0]
assert effect_values("resource_density_1", "multiply_resource_yield") == [1.25]
assert effect_values("resource_density_2", "multiply_resource_yield") == [1.5]
assert effect_values("precious_yield_1", "multiply_precious_resource_yield") == [2.0]
assert len(skills) >= 49, "expanded incremental tree should retain many small individual purchases"

print(f"content validation passed: {len(blocks)} blocks, {len(miners)} miners, {len(skills)} skills, {len(worlds)} worlds")
