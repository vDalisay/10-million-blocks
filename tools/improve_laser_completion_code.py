#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")

def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)

# 1) Make the manual-action signal type-safe instead of encoding semantics in a bool.
path = "src/Mining/ManualMiningController.cs"
text = read(path)
text = replace_once(
    text,
    "namespace TenMillionBlocks.Mining;\n\npublic partial class ManualMiningController : Node3D",
    "namespace TenMillionBlocks.Mining;\n\npublic enum ManualMiningActionKind\n{\n    PhysicalClick,\n    HoverAutomatic,\n}\n\npublic partial class ManualMiningController : Node3D",
    "manual action enum")
text = replace_once(
    text,
    "public event Action<bool>? MiningActionPerformed; // bool = automatic Hover Mining action",
    "public event Action<ManualMiningActionKind>? MiningActionPerformed;",
    "manual action event")
text = replace_once(text, "MiningActionPerformed?.Invoke(false);", "MiningActionPerformed?.Invoke(ManualMiningActionKind.PhysicalClick);", "physical action event")
text = replace_once(text, "MiningActionPerformed?.Invoke(true);", "MiningActionPerformed?.Invoke(ManualMiningActionKind.HoverAutomatic);", "hover action event")
write(path, text)

# 2) Give laser removals their own source and share the authored hardness accumulator without
# pretending the 10 Hz beam ticks are physical clicks. This also makes unstable blocks respect their
# authored hardness under the beam while preserving the existing three-click manual anticipation.
path = "src/Mining/MiningService.cs"
text = read(path)
text = replace_once(text, "    Manual,\n    Automated,", "    Manual,\n    Laser,\n    Automated,", "laser mining source")
text = text.replace("_manualDamage", "_hardnessDamage")
pattern = re.compile(
    r"    public MiningResult TryMineManual\(Vector3I voxel, double damage\)\n    \{.*?\n    \}\n\n    public MiningResult TryMine\(Vector3I voxel, MiningSource source, bool requireExposed\)",
    re.S,
)
replacement = '''    public MiningResult TryMineManual(Vector3I voxel, double damage)\n        => TryMineWithHardness(voxel, damage, MiningSource.Manual, preserveManualBombClicks: true);\n\n    /// <summary>\n    /// Flux Laser damage shares the same authored hardness state as manual mining, but has its own\n    /// source identity. It must not masquerade as a physical click for tutorials/telemetry, and unstable\n    /// blocks accumulate continuous beam damage instead of treating every 10 Hz damage tick as a click.\n    /// </summary>\n    public MiningResult TryMineLaser(Vector3I voxel, double damage)\n        => TryMineWithHardness(voxel, damage, MiningSource.Laser, preserveManualBombClicks: false);\n\n    private MiningResult TryMineWithHardness(\n        Vector3I voxel,\n        double damage,\n        MiningSource source,\n        bool preserveManualBombClicks)\n    {\n        BlockSample before = _world.SampleVoxel(voxel);\n        if (!before.Present || !before.Mineable)\n        {\n            _hardnessDamage.Remove(voxel);\n            return Failure(voxel, source);\n        }\n\n        // Physical clicks deliberately retain the authored three-hit unstable-block anticipation. The\n        // laser instead uses the normal hardness accumulator and detonates only after enough beam damage.\n        if (preserveManualBombClicks && before.BlockId == "bomb")\n        {\n            return TryMine(voxel, before, source, requireExposed: true);\n        }\n\n        if (!_world.IsExposed(voxel, before))\n        {\n            return Failure(voxel, source);\n        }\n\n        BlockDefinition definition = _content.GetBlock(before.BlockId);\n        double hardness = Math.Max(0.01, definition.Hardness);\n        double applied = Math.Max(0.01, damage);\n        double accumulated = _hardnessDamage.GetValueOrDefault(voxel) + applied;\n        if (accumulated + 1e-9 < hardness)\n        {\n            _hardnessDamage[voxel] = accumulated;\n            var damaged = new MiningResult(\n                true,\n                voxel,\n                before.BlockId,\n                0L,\n                TotalMined,\n                Remaining,\n                source,\n                BlocksRemoved: 0L,\n                Removed: false,\n                DamageStage: Math.Clamp((int)Math.Ceiling(accumulated * DamageDisplayScale), 1, int.MaxValue),\n                DamageRequired: Math.Clamp((int)Math.Ceiling(hardness * DamageDisplayScale), 1, int.MaxValue));\n            BlockDamaged?.Invoke(damaged);\n            return damaged;\n        }\n\n        _hardnessDamage.Remove(voxel);\n        return TryMine(voxel, before, source, requireExposed: true);\n    }\n\n    public MiningResult TryMine(Vector3I voxel, MiningSource source, bool requireExposed)'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise RuntimeError(f"hardness mining refactor: expected one method block, found {count}")
write(path, text)

# 3) Laser controller consumes the typed action signal and the dedicated hardness/source path.
path = "src/Mining/LaserMiningController.cs"
text = read(path)
text = replace_once(
    text,
    "private void OnMiningActionPerformed(bool automatic)\n    {\n        if (!CanCharge()) return;\n        AddCharge(automatic\n            ? _skills.Derived.LaserAutoChargePerAction\n            : _skills.Derived.LaserManualChargePerAction);\n    }",
    "private void OnMiningActionPerformed(ManualMiningActionKind kind)\n    {\n        if (!CanCharge()) return;\n        AddCharge(kind == ManualMiningActionKind.HoverAutomatic\n            ? _skills.Derived.LaserAutoChargePerAction\n            : _skills.Derived.LaserManualChargePerAction);\n    }",
    "laser action handler")
text = replace_once(text, "MiningResult result = _mining.TryMineManual(target, damage);", "MiningResult result = _mining.TryMineLaser(target, damage);", "laser hardness call")
text = text.replace("ShowBeam(hitPoint, normal);", "ShowBeam(hitPoint);")
text = replace_once(text, "private void ShowBeam(Vector3 target, Vector3I surfaceNormal)", "private void ShowBeam(Vector3 target)", "unused beam normal")
write(path, text)

# 4) Laser pickups follow personal collection rules, but retain a distinct authoritative source.
path = "src/Collection/ResourceCollectionField.cs"
text = read(path)
old = '''        bool automated = result.Source == MiningSource.Automated;\n        bool manual = result.Source == MiningSource.Manual;\n        if (!manual && !automated) return;\n\n        bool autoCollect = manual\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect)\n        {\n            Vector2 source = ProjectCollectionSource(result.Voxel, manual);'''
new = '''        bool automated = result.Source == MiningSource.Automated;\n        bool personal = result.Source is MiningSource.Manual or MiningSource.Laser;\n        if (!personal && !automated) return;\n\n        bool autoCollect = personal\n            ? _skills.Derived.ManualAutoCollectUnlocked\n            : _skills.Derived.AutomationAutoCollectUnlocked;\n        if (autoCollect)\n        {\n            Vector2 source = ProjectCollectionSource(result.Voxel, personal);'''
text = replace_once(text, old, new, "laser pickup source")
write(path, text)

# 5) Keep existing completion accounting compatible: laser removals remain player-driven in the summary,
# while telemetry no longer treats every laser block removal as a fresh player decision.
path = "src/App/GameRoot.cs"
text = read(path)
text = replace_once(
    text,
    "else if (result.Source == MiningSource.Manual) _manualBlocksThisWorld++;",
    "else if (result.Source is MiningSource.Manual or MiningSource.Laser) _manualBlocksThisWorld++;",
    "completion source accounting")
write(path, text)

path = "src/Diagnostics/PacingTelemetryRecorder.cs"
text = read(path)
old = '''        else if (result.Source == MiningSource.Manual)\n        {\n            _sessionManualBlocks++;\n            MarkDecision();\n        }'''
new = '''        else if (result.Source is MiningSource.Manual or MiningSource.Laser)\n        {\n            _sessionManualBlocks++;\n            // A physical click is a fresh player decision. Laser removals are consequences of an\n            // already-started mode and must not reset the action-gap metric every damage tick.\n            if (result.Source == MiningSource.Manual) MarkDecision();\n        }'''
text = replace_once(text, old, new, "pacing laser accounting")
write(path, text)

path = "src/Replay/ReplayModel.cs"
text = read(path)
text = replace_once(
    text,
    "MiningSource.Manual => ReplayMiningSource.Manual,",
    "MiningSource.Manual or MiningSource.Laser => ReplayMiningSource.Manual,",
    "replay laser mapping")
write(path, text)

# 6) Completion GPU particles share one compiled shader and receive all ceremony timings as uniforms.
# This removes four identical shader compilations and prevents C#/shader timing constants drifting apart.
path = "src/Presentation/WorldCompletionCeremony.cs"
text = read(path)
text = replace_once(
    text,
    "    private const float ScatterStart = 0.72f;\n    private const float BlackHoleStart = 3.15f;\n    private const float SuctionStart = 3.55f;\n    private const float FinishAt = 6.15f;",
    "    private const float ScatterStart = 0.72f;\n    private const float ScatterDuration = 1.75f;\n    private const float MaxScatterDelay = 0.34f;\n    private const float BlackHoleStart = 3.15f;\n    private const float SuctionStart = 3.55f;\n    private const float SuctionDuration = 2.35f;\n    private const float SuctionDelayFactor = 0.35f;\n    private const float FinishAt = 6.15f;",
    "completion timing constants")
text = replace_once(
    text,
    "        long remaining = BonusParticleCount;\n        for (int visualIndex = 0; visualIndex < visuals.Count && remaining > 0; visualIndex++)",
    "        var sharedParticleShader = new Shader { Code = ParticleShaderCode };\n        long remaining = BonusParticleCount;\n        for (int visualIndex = 0; visualIndex < visuals.Count && remaining > 0; visualIndex++)",
    "shared completion shader")
text = replace_once(
    text,
    "            var shader = new Shader { Code = ParticleShaderCode };\n            var processMaterial = new ShaderMaterial { Shader = shader };\n            processMaterial.SetShaderParameter(\"visual_time\", 0.0f);\n            processMaterial.SetShaderParameter(\"scatter_radius\", scatterRadius);",
    "            var processMaterial = new ShaderMaterial { Shader = sharedParticleShader };\n            processMaterial.SetShaderParameter(\"visual_time\", 0.0f);\n            processMaterial.SetShaderParameter(\"scatter_start\", ScatterStart);\n            processMaterial.SetShaderParameter(\"scatter_duration\", ScatterDuration);\n            processMaterial.SetShaderParameter(\"max_scatter_delay\", MaxScatterDelay);\n            processMaterial.SetShaderParameter(\"suction_start\", SuctionStart);\n            processMaterial.SetShaderParameter(\"suction_duration\", SuctionDuration);\n            processMaterial.SetShaderParameter(\"suction_delay_factor\", SuctionDelayFactor);\n            processMaterial.SetShaderParameter(\"scatter_radius\", scatterRadius);",
    "completion shader uniforms")
text = replace_once(
    text,
    "uniform float visual_time = 0.0;\nuniform float scatter_radius = 20.0;",
    "uniform float visual_time = 0.0;\nuniform float scatter_start = 0.72;\nuniform float scatter_duration = 1.75;\nuniform float max_scatter_delay = 0.34;\nuniform float suction_start = 3.55;\nuniform float suction_duration = 2.35;\nuniform float suction_delay_factor = 0.35;\nuniform float scatter_radius = 20.0;",
    "shader timing uniforms")
text = replace_once(text, "float delay = rnd_d * 0.34;", "float delay = rnd_d * max_scatter_delay;", "shader scatter delay")
text = replace_once(text, "float scatter_t = clamp((visual_time - 0.72 - delay) / 1.75, 0.0, 1.0);", "float scatter_t = clamp((visual_time - scatter_start - delay) / scatter_duration, 0.0, 1.0);", "shader scatter timing")
text = replace_once(text, "float suction_t = clamp((visual_time - 3.55 - delay * 0.35) / 2.35, 0.0, 1.0);", "float suction_t = clamp((visual_time - suction_start - delay * suction_delay_factor) / suction_duration, 0.0, 1.0);", "shader suction timing")
write(path, text)

# 7) Strengthen the static laser contract around these architecture invariants.
path = "tools/validate_laser_contract.py"
text = read(path)
text = text.replace(
    'require("event Action<ManualMiningActionKind>? MiningActionPerformed" in manual,',
    'require("event Action<ManualMiningActionKind>? MiningActionPerformed" in manual,') if "event Action<ManualMiningActionKind>? MiningActionPerformed" in text else text
text = text.replace(
    'require("event Action<bool>? MiningActionPerformed" in manual,\n        "manual controller must expose one semantic successful-action event for laser charging")',
    'require("event Action<ManualMiningActionKind>? MiningActionPerformed" in manual,\n        "manual controller must expose a typed semantic successful-action event for laser charging")')
text = text.replace(
    'require("MiningActionPerformed?.Invoke(false)" in manual and "MiningActionPerformed?.Invoke(true)" in manual,\n        "physical and Hover Mining actions must be tagged separately")',
    'require("ManualMiningActionKind.PhysicalClick" in manual and "ManualMiningActionKind.HoverAutomatic" in manual,\n        "physical and Hover Mining actions must use distinct typed action kinds")')
text = text.replace(
    'require("_mining.TryMineManual(target, damage)" in laser,\n        "laser damage must reuse the authored manual hardness/reward path")',
    'require("_mining.TryMineLaser(target, damage)" in laser,\n        "laser damage must reuse authored hardness through its dedicated source path")')
anchor = 'require("private const double DamageTickSeconds = 0.10" in laser,\n        "laser gameplay damage cadence must remain bounded at 10 Hz")\n'
addition = '''require("MiningSource.Laser" in read("src/Mining/MiningService.cs"),\n        "laser removals must retain a dedicated authoritative source identity")\nrequire("MiningSource.Manual or MiningSource.Laser" in read("src/Replay/ReplayModel.cs"),\n        "laser replay removals must remain visually grouped with player-driven mining")\nrequire("MiningSource.Manual or MiningSource.Laser" in read("src/Collection/ResourceCollectionField.cs"),\n        "laser rewards must follow personal pickup/auto-collect policy")\n'''
if addition not in text:
    text = replace_once(text, anchor, anchor + addition, "laser contract architecture checks")
write(path, text)

print("Applied laser/completion code-quality pass.")
