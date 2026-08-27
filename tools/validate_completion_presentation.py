#!/usr/bin/env python3
"""Static contract for the exact-count end-of-world completion presentation.

The runtime renderer still requires local Godot/GPU profiling. This contract protects the architecture
that keeps that renderer cheap and deterministic: one shared compiled particle shader, exact instance
counts, C#-owned timing uniforms, and presentation-only resource count-up.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "src/Presentation/WorldCompletionCeremony.cs").read_text(encoding="utf-8")
root = (ROOT / "src/App/GameRoot.WorldCeremony.cs").read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"completion presentation contract failed: {message}")


for constant in (
    "ScatterStart",
    "ScatterDuration",
    "MaxScatterDelay",
    "BlackHoleStart",
    "SuctionStart",
    "SuctionDuration",
    "SuctionDelayFactor",
    "FinishAt",
):
    require(f"private const float {constant}" in source, f"missing C# timing constant {constant}")

for uniform in (
    "scatter_start",
    "scatter_duration",
    "max_scatter_delay",
    "suction_start",
    "suction_duration",
    "suction_delay_factor",
):
    require(f'SetShaderParameter("{uniform}"' in source, f"{uniform} is not supplied from C#")
    require(f"uniform float {uniform}" in source, f"shader uniform {uniform} is missing")

require(source.count("new Shader { Code = ParticleShaderCode }") == 1,
        "completion particle buckets must share one compiled Shader resource")
require("new ShaderMaterial { Shader = sharedParticleShader }" in source,
        "per-bucket materials must reference the shared particle shader")
require("Amount = (int)share" in source,
        "GPU emitter amount must remain the exact numerical bonus share")
require("BonusParticleCount = Math.Max(0L, bonusParticles)" in source,
        "bonus particle count must remain exact and non-negative")
require("step(scatter_start + delay, visual_time)" in source,
        "particle appearance must use the C#-supplied scatter start")
require("step(0.72 + delay, visual_time)" not in source,
        "shader must not duplicate the old hard-coded scatter start")
require("UpdateResourceCounter(time)" in source and "visualBonus" in source,
        "completion must retain the presentation-only resource count-up")
require("_completionCeremony.Completed += CommitCompletionRewardAndShowResults" in root,
        "authoritative reward commit must remain outside the particle renderer")
require("_resourceCollection?.ResolveAllForCompletion();" in root,
        "completion must use the compressed pending-pickup settlement path")

print("completion presentation contract passed: exact-count GPU field, shared shader, centralized timings, guarded reward boundary")
