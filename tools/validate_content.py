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
    "unlock_auto_cloud_charger",
}

for ident, block in blocks.items():
    asset = block.get("asset_path", "")
    assert asset.startswith("res://"), f"block {ident} has non-res asset path: {asset}"
    disk_path = ROOT / asset.removeprefix("res://")
    assert disk_path.exists(), f"block {ident} references missing asset: {asset}"

for ident, miner in miners.items():
    pattern = miner.get("pattern_id")
    assert pattern in patterns, f"miner {ident} references unknown pattern {pattern}"
    assert int(miner.get("unit_price", 0)) > 0, f"miner {ident} must have a fixed positive unit_price"
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

    special_ids = set()
    for special_cost in skill.get("special_costs", []):
        resource_id = special_cost.get("resource_id", "")
        amount = int(special_cost.get("amount", 0))
        assert resource_id in blocks, f"skill {ident} special cost references missing resource {resource_id}"
        assert "gem" in set(blocks[resource_id].get("tags", [])), (
            f"skill {ident} special cost {resource_id} is not tagged as a gem/special resource"
        )
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

world_override_docs = {}
for world_id, profile in worlds.items():
    assert int(profile.get("worldVersion", 0)) > 0, f"world {world_id} must commit a positive worldVersion"
    assert int(profile.get("generationVersion", 0)) > 0, f"world {world_id} must commit a positive generationVersion"
    assert profile.get("generationMode", "procedural") in {"procedural", "single_block", "solid_cube"}, (
        f"world {world_id} has an unknown generationMode"
    )

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
    assert int(override_doc.get("schemaVersion", 0)) == 1, f"world {world_id} override schema must be 1"
    assert override_doc.get("worldId") == world_id, f"world {world_id} override targets another world"
    assert int(override_doc.get("generationVersion", 0)) == int(profile.get("generationVersion", 0)), (
        f"world {world_id} override generation version does not match its profile"
    )

    seen_coordinates = set()
    for item in override_doc.get("overrides", []):
        coordinate = (int(item.get("x", 0)), int(item.get("y", 0)), int(item.get("z", 0)))
        assert coordinate not in seen_coordinates, f"world {world_id} has duplicate override at {coordinate}"
        seen_coordinates.add(coordinate)
        if item.get("present", True):
            assert item.get("blockId") in blocks, (
                f"world {world_id} override {coordinate} references missing block {item.get('blockId')}"
            )

    seen_features = set()
    for item in override_doc.get("features", []):
        coordinate = (int(item.get("x", 0)), int(item.get("y", 0)), int(item.get("z", 0)))
        normal = (
            int(item.get("normalX", 0)),
            int(item.get("normalY", 1)),
            int(item.get("normalZ", 0)),
        )
        assert coordinate not in seen_features, f"world {world_id} has duplicate feature at {coordinate}"
        seen_features.add(coordinate)
        assert item.get("blockId") == "tree", (
            f"world {world_id} currently supports only semantic authored tree features, got {item.get('blockId')}"
        )
        assert sum(abs(value) for value in normal) == 1, (
            f"world {world_id} feature {coordinate} has non-cardinal normal {normal}"
        )

expected_tutorial_ids = [
    "tutorial_single_block",
    "tutorial_dirt_5",
    "tutorial_lake_core_10",
    "tutorial_trees_gem_15",
]
assert progression_doc["world_ids"][:4] == expected_tutorial_ids, (
    f"expected the four authored tutorial worlds first, got {progression_doc['world_ids'][:4]}"
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
    "manual tutorial must remain 5 x 5 x 5"
)
assert int(manual.get("targetMineableBlocks", 0)) == 125, "5x5 solid tutorial must contain 125 blocks"
assert manual.get("automationAvailable") is False, "manual tutorial must hide automation"
assert manual.get("visibleSkillCategories") == ["manual"], "manual tutorial must expose only the manual skill branch"
assert manual.get("surfaceBlock") == "dirt", "manual tutorial must remain a dirt practice cube"

lake = worlds["tutorial_lake_core_10"]
assert lake.get("generationMode") == "solid_cube", "10x10 tutorial must use an authored solid base"
assert [int(lake.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [10, 10, 10], (
    "lake/core tutorial must remain 10 x 10 x 10"
)
assert int(lake.get("targetMineableBlocks", 0)) == 1000, "10x10 tutorial must retain 1000 physical mineable cells"
assert lake.get("automationAvailable") is True, "lake/core tutorial must introduce Powered Shovel automation"
assert lake.get("visibleSkillCategories") == ["manual", "shovel"], (
    "lake/core tutorial must reveal only manual and shovel branches"
)
lake_overrides = world_override_docs["tutorial_lake_core_10"]["overrides"]
lake_water = [item for item in lake_overrides if item.get("blockId") in {"water", "water_shallow", "water_deep"}]
lake_stone = [item for item in lake_overrides if item.get("blockId") in {"stone", "stone_dark"}]
assert len(lake_water) == 16, f"lake/core tutorial must keep one authored 4x4 lake surface, got {len(lake_water)} water cells"
assert len(lake_stone) == 64, f"lake/core tutorial must keep the authored 4x4x4 stone core, got {len(lake_stone)} stone cells"

trees_gem = worlds["tutorial_trees_gem_15"]
assert trees_gem.get("generationMode") == "solid_cube", "15x15 tutorial must use an authored solid base"
assert [int(trees_gem.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")] == [15, 15, 15], (
    "trees/gem tutorial must remain 15 x 15 x 15"
)
assert int(trees_gem.get("targetMineableBlocks", 0)) == 3375, (
    "15x15 tutorial overrides replace cells and must retain exactly 3375 physical mineable blocks"
)
assert trees_gem.get("visibleSkillCategories") == ["manual", "shovel", "automation", "drill", "patterns"], (
    "15x15 tutorial must teach tree obstruction before Forest Cutter is introduced in the first main world"
)
trees_doc = world_override_docs["tutorial_trees_gem_15"]
trees_overrides = trees_doc["overrides"]
red_gems = [item for item in trees_overrides if item.get("blockId") == "gem_red"]
assert len(red_gems) == 1, f"15x15 tutorial must contain exactly one red gem, got {len(red_gems)}"
assert (red_gems[0]["x"], red_gems[0]["y"], red_gems[0]["z"]) == (0, 0, 0), (
    "15x15 tutorial red gem must replace the exact center voxel"
)
tutorial3_water = [item for item in trees_overrides if item.get("blockId") in {"water", "water_shallow", "water_deep"}]
assert len(tutorial3_water) == 25, f"15x15 tutorial must keep its authored 5x5 lake, got {len(tutorial3_water)} water cells"
assert len(trees_doc.get("features", [])) == 8, "15x15 tutorial must keep eight authored tree blockers"

# Reviewed Steam-demo world scale. The legacy reference IDs are deliberately retained as stable save
# keys while their profiles now represent the 20/40/50-cube post-tutorial sequence.
demo_worlds = progression_doc["world_ids"][4:]
assert demo_worlds == ["reference_natural", "reference_lakes", "reference_ridges"], (
    f"expected reviewed 20/40/50 demo worlds after tutorials, got {demo_worlds}"
)
expected_dimensions = {
    "reference_natural": 20,
    "reference_lakes": 40,
    "reference_ridges": 50,
}
for world_id, dimension in expected_dimensions.items():
    profile = worlds[world_id]
    actual_dimensions = [int(profile.get(axis, 0)) for axis in ("logicalWidth", "logicalHeight", "logicalDepth")]
    assert actual_dimensions == [dimension, dimension, dimension], (
        f"{world_id} must remain {dimension}^3 metadata, got {actual_dimensions}"
    )
    assert profile.get("rendererMode", "eager") != "full_surface", (
        f"demo world {world_id} must stay on the normal authored-scale renderer"
    )
    assert profile.get("currencyScope", "persistent_main") == "persistent_main", (
        f"demo world {world_id} must use the persistent main wallet"
    )
    assert str(profile.get("introText", "")).strip(), f"demo world {world_id} needs authored introText"

reference_natural = worlds["reference_natural"]
assert reference_natural.get("visibleSkillIds", []) == [], (
    "20-cube main world must not expose active-event automation before weather is introduced"
)
assert "forest" in reference_natural.get("visibleSkillCategories", []), (
    "Forest Cutter must first become visible in the 20-cube main world after the 15-cube tree-obstruction lesson"
)
assert int(reference_natural.get("worldVersion", 0)) == 3, (
    "20-cube special-resource content change must remain versioned as worldVersion 3"
)
assert reference_natural.get("overrideFile") == "res://data/worlds/overrides/reference_natural_v3.json", (
    "20-cube main world must keep its reviewed version-matched sparse special-resource override"
)
reference_overrides = world_override_docs["reference_natural"]["overrides"]
reference_gems = [item for item in reference_overrides if item.get("blockId") in {"gem_green", "gem_blue", "gem_red"}]
assert len(reference_gems) == 3, (
    f"20-cube main world must guarantee three authored special gems, got {len(reference_gems)}"
)
assert {item.get("blockId") for item in reference_gems} == {"gem_green", "gem_blue", "gem_red"}, (
    "20-cube main world must guarantee one of each currently supported special gem color"
)

for world_id in ("reference_lakes", "reference_ridges"):
    assert worlds[world_id].get("visibleSkillIds", []) == ["cloud_charger_unlock"], (
        f"{world_id} must expose the late-game Cloud Charger as an exact staged skill"
    )

assert progression_doc["world_ids"][-1] == "reference_ridges", (
    "the Steam demo must end after the reviewed 50-cube finale"
)
assert "final_target_1m" not in progression_doc["world_ids"], (
    "the one-million full-release destination must not be reachable in the Steam demo progression"
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

for shovel_surface in ("grass", "dirt_grass", "dirt"):
    assert "sand" in set(blocks[shovel_surface].get("tags", [])), (
        f"{shovel_surface} must remain valid shovel terrain via the sand tag"
    )
assert blocks["grass"].get("asset_path") == "res://Assets/gltf/grass.gltf", (
    "outer cube seams must use the uniform grass mesh so outward corner faces do not expose soil"
)
assert blocks["dirt_grass"].get("asset_path") == "res://Assets/gltf/dirt_with_grass.gltf", (
    "natural terrain ledges must keep the dirt-backed grass mesh for exposed soil sides"
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
assert skills["wide_bore_unlock"].get("special_costs") == [{"resource_id": "gem_red", "amount": 1}], (
    "Wide Bore must consume exactly one central red gem"
)
assert effect_strings("wide_bore_unlock", "set_drill_pattern") == ["wide_line"], (
    "Wide Bore must transform the primary Drill pattern class-wide"
)
assert effect_values("wide_bore_unlock", "set_miner_pattern_width") == [3.0], (
    "Wide Bore must keep the 3x3 cutter width"
)
assert skills["axe_unlock"].get("category") == "forest", (
    "Forest Cutter must stay in its dedicated branch"
)
assert skills["axe_unlock"].get("prerequisites", []) == [], (
    "Forest Cutter must be independently stageable when the 20-cube world reveals its branch"
)
assert skills["cloud_charger_unlock"].get("category") == "events", (
    "Cloud Charger must stay in the late-game events branch"
)
assert any(
    effect.get("type") == "unlock_auto_cloud_charger"
    for effect in skills["cloud_charger_unlock"].get("effects", [])
), "Cloud Charger must enable automatic cloud charging"
assert skills["cloud_charger_unlock"].get("prerequisites") == [
    {"node_id": "pickaxe_unlock", "required_rank": 1, "route": [{"grid_x": 7, "grid_y": 3}]}
], "Cloud Charger must remain downstream of the main-game Rock Breaker branch"

print(
    f"content validation passed: {len(blocks)} blocks, {len(miners)} miners, "
    f"{len(skills)} skills, {len(worlds)} worlds"
)
