#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def patch(path: str, old: str, new: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"Anchor missing in {path}: {old[:180]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")

# ManualMiningController publishes exactly one semantic action after a successful physical click
# or Hover Mining tick. Footprint size/block count is intentionally irrelevant to capacitor charge.
patch(
    "src/Mining/ManualMiningController.cs",
    "    public bool HoverMiningEnabled => _hoverMiningEnabled && _skills.Derived.HoverMiningUnlocked;\n\n    public void Initialize(",
    "    public bool HoverMiningEnabled => _hoverMiningEnabled && _skills.Derived.HoverMiningUnlocked;\n"
    "    public event Action<bool>? MiningActionPerformed; // bool = automatic Hover Mining action\n\n"
    "    public void Initialize(",
)
patch(
    "src/Mining/ManualMiningController.cs",
    "        if (actions > 0)\n        {\n            UpdateHover(button.Position, force: true);",
    "        if (actions > 0)\n        {\n            MiningActionPerformed?.Invoke(false);\n            UpdateHover(button.Position, force: true);",
)
patch(
    "src/Mining/ManualMiningController.cs",
    "        if (MineManualTick(_hoverTargets, hoverMining: true, _hoverSurfaceNormal) > 0)\n        {\n            _highlight.PulseMine();",
    "        if (MineManualTick(_hoverTargets, hoverMining: true, _hoverSurfaceNormal) > 0)\n        {\n            MiningActionPerformed?.Invoke(true);\n            _highlight.PulseMine();",
)

# Laser subscribes to the semantic action instead of guessing from input/event frame ordering.
patch(
    "src/Mining/LaserMiningController.cs",
    "    private bool _overburning;\n    private bool _resourceBurnEnabled;\n    private ulong _lastPhysicalClickFrame = ulong.MaxValue;\n    private ulong _lastAutoChargeFrame = ulong.MaxValue;\n",
    "    private bool _overburning;\n    private bool _resourceBurnEnabled;\n",
)
patch(
    "src/Mining/LaserMiningController.cs",
    "        _mining.BlockMined += OnManualMiningObserved;\n        _mining.BlockDamaged += OnManualMiningObserved;\n        _skills.Changed += OnSkillsChanged;",
    "        _manual.MiningActionPerformed += OnMiningActionPerformed;\n        _skills.Changed += OnSkillsChanged;",
)
patch(
    "src/Mining/LaserMiningController.cs",
    "        if (_mining is not null)\n        {\n            _mining.BlockMined -= OnManualMiningObserved;\n            _mining.BlockDamaged -= OnManualMiningObserved;\n        }\n        if (_skills is not null) _skills.Changed -= OnSkillsChanged;",
    "        if (_manual is not null) _manual.MiningActionPerformed -= OnMiningActionPerformed;\n        if (_skills is not null) _skills.Changed -= OnSkillsChanged;",
)

start = '''    public override void _Input(InputEvent @event)\n    {\n        if (@event is not InputEventMouseButton button\n            || button.ButtonIndex != MouseButton.Left\n            || !button.Pressed\n            || !CanCharge()\n            || _manual.HoveredVoxel is null\n            || _manual.PlacementMode\n            || _camera.IsManipulating)\n        {\n            return;\n        }\n\n        _lastPhysicalClickFrame = Engine.GetProcessFrames();\n        AddCharge(_skills.Derived.LaserManualChargePerAction);\n    }\n\n'''
patch("src/Mining/LaserMiningController.cs", start, "")

old_observer = '''    private void OnManualMiningObserved(MiningResult result)\n    {\n        if (!result.Success || result.Source != MiningSource.Manual || !CanCharge()) return;\n        ulong frame = Engine.GetProcessFrames();\n        if (frame == _lastPhysicalClickFrame || frame == _lastAutoChargeFrame) return;\n        _lastAutoChargeFrame = frame;\n        AddCharge(_skills.Derived.LaserAutoChargePerAction);\n    }\n\n'''
new_observer = '''    private void OnMiningActionPerformed(bool automatic)\n    {\n        if (!CanCharge()) return;\n        AddCharge(automatic\n            ? _skills.Derived.LaserAutoChargePerAction\n            : _skills.Derived.LaserManualChargePerAction);\n    }\n\n'''
patch("src/Mining/LaserMiningController.cs", old_observer, new_observer)

# Lock the data contract into repository validation so later content edits cannot silently move the
# laser earlier, change the base 5s/60s rhythm, or remove the paid-burn branch.
validator = ROOT / "tools/validate_content.py"
text = validator.read_text(encoding="utf-8")
anchor = 'assert progression_doc["world_ids"][-1] == "reference_ridges"\nassert "final_target_1m" not in progression_doc["world_ids"]\n'
if anchor not in text:
    raise RuntimeError("laser validation insertion anchor missing")
addition = anchor + '''\n# Very-late Flux Laser contract. It converges the 50-cube manual/event capstones and stays hidden\n# through progressive disclosure until both have actually been purchased.\nlaser = skills["laser_core"]\nassert {p["node_id"] for p in laser.get("prerequisites", [])} == {"manual_aftershock", "orb_breaker_swarm"}\nassert laser.get("hide_until_prerequisites_met") is True\nassert effect_values("laser_core", "unlock_laser") == [None]\nassert effect_values("laser_wide_lens", "set_laser_beam_radius") == [2.0]\nassert effect_values("laser_cooling", "set_laser_cooldown_seconds") == [50.0]\nassert effect_values("laser_hotter_beam", "multiply_laser_damage") == [1.5]\nassert effect_values("laser_duration", "set_laser_duration_seconds") == [7.0]\nassert effect_values("laser_resource_furnace", "set_laser_resource_cost_per_second") == [300.0]\nassert effect_values("laser_furnace_efficiency", "multiply_laser_resource_cost") == [0.6]\n'''
# effect_values is defined later in the validator, so insert these assertions after that helper instead.
# Remove the premature addition and use a late anchor.
late_anchor = 'assert effect_values("drill_hardened_bit", "set_drill_material_tier") == [1.0]\n'
if late_anchor not in text:
    raise RuntimeError("late laser validation anchor missing")
late_addition = '''# Very-late Flux Laser contract. It converges the 50-cube manual/event capstones and stays hidden\n# through progressive disclosure until both have actually been purchased.\nlaser = skills["laser_core"]\nassert {p["node_id"] for p in laser.get("prerequisites", [])} == {"manual_aftershock", "orb_breaker_swarm"}\nassert laser.get("hide_until_prerequisites_met") is True\nassert [e.get("type") for e in laser.get("effects", [])] == ["unlock_laser"]\nassert effect_values("laser_wide_lens", "set_laser_beam_radius") == [2.0]\nassert effect_values("laser_cooling", "set_laser_cooldown_seconds") == [50.0]\nassert effect_values("laser_hotter_beam", "multiply_laser_damage") == [1.5]\nassert effect_values("laser_duration", "set_laser_duration_seconds") == [7.0]\nassert effect_values("laser_resource_furnace", "set_laser_resource_cost_per_second") == [300.0]\nassert effect_values("laser_furnace_efficiency", "multiply_laser_resource_cost") == [0.6]\n\n'''
validator.write_text(text.replace(late_anchor, late_addition + late_anchor, 1), encoding="utf-8")

print("Refined Flux Laser to explicit successful-action charging and added content contracts.")
