#!/usr/bin/env python3
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

skills_path = ROOT / "data/skills/skill_tree.json"
doc = json.loads(skills_path.read_text(encoding="utf-8"))
by_id = {node["id"]: node for node in doc["nodes"]}
laser = by_id["laser_core"]
laser["prerequisites"] = [
    {"node_id": "manual_aftershock", "required_rank": 1},
    {"node_id": "orb_breaker_radius_1", "required_rank": 1},
]
laser["description"] = (
    "Very-late active/idle capstone. After Aftershock and Orb Breaker Radius converge, manual clicks "
    "and Hover Mining charge a capacitor; at full charge a wide cursor laser fires automatically for "
    "5 seconds at 1.0 block-damage per second, then locks into a 60-second cooldown."
)
skills_path.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")

validator = ROOT / "tools/validate_content.py"
text = validator.read_text(encoding="utf-8")
old = 'assert {p["node_id"] for p in laser.get("prerequisites", [])} == {"manual_aftershock", "orb_breaker_swarm"}'
new = 'assert {p["node_id"] for p in laser.get("prerequisites", [])} == {"manual_aftershock", "orb_breaker_radius_1"}'
if old not in text:
    raise RuntimeError("Laser prerequisite validation anchor missing")
validator.write_text(text.replace(old, new, 1), encoding="utf-8")

plan_path = ROOT / "docs/WORLD_INTRO_AND_BLACK_HOLE_COMPLETION_PLAN.md"
plan = plan_path.read_text(encoding="utf-8")
plan = plan.replace(
    "The first unlock is deliberately late and depends on both `manual_aftershock` and\n"
    "`orb_breaker_swarm`, making it a convergence capstone between active manual mining and autonomous\n"
    "late-game systems.",
    "The first unlock is deliberately late and depends on both `manual_aftershock` and\n"
    "`orb_breaker_radius_1`, making it a convergence capstone between active manual mining and autonomous\n"
    "late-game systems. The authored 20%-reserve pacing model puts that gate late in the 50³ finale but\n"
    "still leaves enough modeled play time to buy, charge and fire the base laser before the demo clear.\n"
    "Requiring Orb Swarm instead was rejected because simulation made the laser legal only about two\n"
    "seconds before the modeled world end, making the feature effectively unreachable."
)
plan_path.write_text(plan, encoding="utf-8")

print("Moved Flux Laser gate from Orb Swarm to Orb Breaker Radius while retaining Aftershock.")
