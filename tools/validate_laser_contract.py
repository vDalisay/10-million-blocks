#!/usr/bin/env python3
"""Static contract for the late-game Flux Laser feature.

This complements the main C# build by pinning the authored balance/lifecycle invariants that are easy to
accidentally drift while tuning the 70-node tree. Runtime feel/renderer behavior still belongs to local
Godot regression.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"laser contract failed: {message}")


def effect(node: dict, kind: str) -> dict:
    matches = [item for item in node.get("effects", []) if item.get("type") == kind]
    require(len(matches) == 1, f"{node['id']} must contain exactly one {kind} effect")
    return matches[0]


def prereqs(node: dict) -> set[str]:
    return {item["node_id"] for item in node.get("prerequisites", [])}


skill_doc = json.loads(read("data/skills/skill_tree.json"))
nodes = {item["id"]: item for item in skill_doc["nodes"]}

laser_ids = {
    "laser_core",
    "laser_capacitor_1",
    "laser_auto_coupler",
    "laser_wide_lens",
    "laser_cooling",
    "laser_hotter_beam",
    "laser_duration",
    "laser_resource_furnace",
    "laser_resource_efficiency",
}
require(laser_ids <= nodes.keys(), f"missing laser nodes: {sorted(laser_ids - nodes.keys())}")
require(len([node_id for node_id in nodes if node_id.startswith("laser_")]) == 9,
        "initial laser branch must remain exactly nine authored nodes")

core = nodes["laser_core"]
require(prereqs(core) == {"manual_aftershock", "orb_breaker_radius_1"},
        "Flux Laser must remain a late active/autonomous convergence after Aftershock + Orb Breaker Radius")
require(core.get("hide_until_prerequisites_met") is True, "Flux Laser must stay hidden until prerequisites are owned")
require(core.get("purchase_mode") == "once" and int(core.get("max_rank", 0)) == 1,
        "Flux Laser must remain a one-purchase transformation")
require(int(core.get("cost", -1)) == 18_000, "Flux Laser base cost drifted from the reviewed 18,000 target")
require(effect(core, "unlock_laser").get("type") == "unlock_laser", "Flux Laser unlock effect missing")

require(prereqs(nodes["laser_capacitor_1"]) == {"laser_core"}, "Click Capacitor prerequisite drifted")
require(float(effect(nodes["laser_capacitor_1"], "multiply_laser_manual_charge_rate")["value"]) == 1.5,
        "Click Capacitor must remain +50% manual charge")
require(prereqs(nodes["laser_auto_coupler"]) == {"laser_core"}, "Auto Flux Coupler prerequisite drifted")
require(float(effect(nodes["laser_auto_coupler"], "multiply_laser_auto_charge_rate")["value"]) == 2.0,
        "Auto Flux Coupler must remain 2x automatic charge")
require(float(effect(nodes["laser_wide_lens"], "set_laser_beam_radius")["value"]) == 2.0,
        "Wide Lens must continue to select the 5x5 footprint")
require(float(effect(nodes["laser_cooling"], "set_laser_cooldown_seconds")["value"]) == 50.0,
        "Cryo Radiator must remain a 50-second cooldown")
require(float(effect(nodes["laser_hotter_beam"], "multiply_laser_damage")["value"]) == 1.5,
        "Hotter Beam must remain 1.5x damage")
require(float(effect(nodes["laser_duration"], "set_laser_duration_seconds")["value"]) == 7.0,
        "Extended Burn must remain a seven-second natural burst")
require(effect(nodes["laser_resource_furnace"], "unlock_laser_resource_burn")["type"] == "unlock_laser_resource_burn",
        "Resource Furnace unlock effect missing")
require(float(effect(nodes["laser_resource_efficiency"], "multiply_laser_resource_cost")["value"]) == 0.6,
        "Closed-Loop Furnace must remain a 40% resource-cost reduction")

stats = read("src/Skills/SkillTreeService.cs")
def default_number(name: str) -> float:
    match = re.search(rf"\b{name}\s*\{{[^}}]*\}}\s*=\s*([0-9.]+)\s*;", stats)
    require(match is not None, f"could not locate {name} default in SkillDerivedStats")
    return float(match.group(1))

require(default_number("LaserManualChargePerAction") == 0.0125, "manual base charge must remain 1.25% per action")
require(default_number("LaserAutoChargePerAction") == 0.0030, "automatic base charge must remain 0.30% per action")
require(default_number("LaserDamagePerSecond") == 1.0, "base beam damage must remain 1.0 hardness/sec")
require(default_number("LaserDurationSeconds") == 5.0, "base natural burst must remain five seconds")
require(default_number("LaserCooldownSeconds") == 60.0, "base cooldown must remain sixty seconds")
require(default_number("LaserResourceCostPerSecond") == 300.0, "initial overburn cost must remain 300 resources/sec")
require(default_number("LaserManualChargePerAction") > default_number("LaserAutoChargePerAction"),
        "manual actions must charge materially faster than automatic Hover Mining actions")

manual = read("src/Mining/ManualMiningController.cs")
laser = read("src/Mining/LaserMiningController.cs")
require("event Action<bool>? MiningActionPerformed" in manual,
        "manual controller must expose one semantic successful-action event for laser charging")
require("MiningActionPerformed?.Invoke(false)" in manual and "MiningActionPerformed?.Invoke(true)" in manual,
        "physical and Hover Mining actions must be tagged separately")
require("_manual.MiningActionPerformed += OnMiningActionPerformed" in laser,
        "laser must charge from semantic mining actions rather than raw click/frame inference")
require("ManualMiningFootprintKind.Square3" in laser and "ManualMiningFootprintKind.Square5" in laser,
        "laser must retain 3x3 base and 5x5 Wide Lens footprints")
require("_mining.TryMineManual(target, damage)" in laser,
        "laser damage must reuse the authored manual hardness/reward path")
require("private const double DamageTickSeconds = 0.10" in laser,
        "laser gameplay damage cadence must remain bounded at 10 Hz")
require("_mining.TrySpend(due)" in laser,
        "paid overburn must spend authoritative ordinary currency")
require("_overburning ? Math.Max(1.0, _skills.Derived.LaserCooldownSeconds)" in laser,
        "saving during paid overburn must serialize into cooldown")
require("ActiveRemainingForSave => _overburning ? 0.0" in laser,
        "saving during paid overburn must not serialize free active beam time")

all_cs = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "src").rglob("*.cs"))
for field in ("LaserCharge", "LaserCooldownSeconds", "LaserActiveSeconds", "LaserResourceBurnEnabled"):
    require(field in all_cs, f"persistent save/lifecycle field {field} is missing")

plan = read("docs/WORLD_INTRO_AND_BLACK_HOLE_COMPLETION_PLAN.md")
require("# 22. Late-game Flux Laser branch" in plan, "laser implementation/research plan is missing")
require("123,412" in plan and "1,000,000" in plan,
        "exact-count renderer benchmark gates must remain documented")

print("laser contract passed: 9-node branch, reviewed base cycle, explicit active/auto charging, persistence and overburn invariants")
