#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise RuntimeError(f"Anchor not found in {path}: {old[:160]!r}")
    write(path, text.replace(old, new, 1))


# -----------------------------------------------------------------------------
# Save/result contract
# -----------------------------------------------------------------------------
replace_once(
    "src/Save/SaveService.cs",
    "    public double ActivePlaySeconds { get; set; }\n    public bool HoverMiningEnabled { get; set; }",
    "    public double ActivePlaySeconds { get; set; }\n"
    "    public bool ClearReached { get; set; }\n"
    "    public double CompletionClearSeconds { get; set; }\n"
    "    public int CompletionScorePercent { get; set; }\n"
    "    public long CompletionBonusResources { get; set; }\n"
    "    public bool CompletionBonusClaimed { get; set; }\n"
    "    public bool HoverMiningEnabled { get; set; }",
)
replace_once(
    "src/Save/SaveService.cs",
    "            world.ActivePlaySeconds = Math.Max(0.0, world.ActivePlaySeconds);\n            legacyLocalCurrency",
    "            world.ActivePlaySeconds = Math.Max(0.0, world.ActivePlaySeconds);\n"
    "            world.CompletionClearSeconds = Math.Max(0.0, world.CompletionClearSeconds);\n"
    "            world.CompletionScorePercent = Math.Clamp(world.CompletionScorePercent, 0, 100);\n"
    "            world.CompletionBonusResources = Math.Max(0L, world.CompletionBonusResources);\n"
    "            if (world.Completed)\n"
    "            {\n"
    "                world.ClearReached = true;\n"
    "                world.CompletionBonusClaimed = true;\n"
    "            }\n"
    "            legacyLocalCurrency",
)

write(
    "src/Progression/CompletionScore.cs",
    r'''using System;

namespace TenMillionBlocks.Progression;

/// <summary>Pure scoring contract for the speed-based end-of-world black-hole bonus.</summary>
public static class CompletionScore
{
    public const int MinimumPercent = 20;
    public const int MaximumPercent = 100;
    public const double StepSeconds = 5.0 * 60.0;

    public static int CalculatePercent(double clearSeconds)
    {
        double safeSeconds = Math.Max(0.0, clearSeconds);
        int fiveMinuteSteps = (int)Math.Floor(safeSeconds / StepSeconds);
        return Math.Max(MinimumPercent, MaximumPercent - fiveMinuteSteps * 10);
    }

    public static long CalculateBonus(long initialBlockCount, int scorePercent)
    {
        long blocks = Math.Max(0L, initialBlockCount);
        int percent = Math.Clamp(scorePercent, MinimumPercent, MaximumPercent);
        double exact = blocks * (percent / 100.0);
        return checked((long)Math.Round(exact, MidpointRounding.AwayFromZero));
    }

    public static long CalculateBonus(long initialBlockCount, double clearSeconds)
        => CalculateBonus(initialBlockCount, CalculatePercent(clearSeconds));
}
''',
)

# -----------------------------------------------------------------------------
# Offline simulation: chronological operations + clear offset
# -----------------------------------------------------------------------------
replace_once(
    "src/Automation/MinerSimulationService.cs",
    "namespace TenMillionBlocks.Automation;\n\npublic partial class MinerSimulationService",
    "namespace TenMillionBlocks.Automation;\n\n"
    "public readonly record struct OfflineProgressResult(\n"
    "    long BlocksRemoved,\n"
    "    double SimulatedSecondsConsumed,\n"
    "    double? SecondsToWorldClear)\n"
    "{\n"
    "    public bool ClearedWorld => SecondsToWorldClear.HasValue;\n"
    "}\n\n"
    "public partial class MinerSimulationService",
)
start = read("src/Automation/MinerSimulationService.cs").index("    public long ApplyOfflineProgress(")
end = read("src/Automation/MinerSimulationService.cs").index("\n    public void ClearMiners()", start)
text = read("src/Automation/MinerSimulationService.cs")
method = r'''    public OfflineProgressResult ApplyOfflineProgress(double elapsedSeconds, long operationCap = 50_000)
    {
        if (elapsedSeconds <= 0.0 || operationCap <= 0 || _miners.Count == 0)
            return default;

        double seconds = Math.Min(elapsedSeconds, 7.0 * 24.0 * 60.0 * 60.0);
        long minedBefore = _mining.TotalMined;
        long operationsLeft = operationCap;

        var initialAccumulators = new Dictionary<long, double>(_miners.Count);
        var rates = new Dictionary<long, double>(_miners.Count);
        var processed = new Dictionary<long, long>(_miners.Count);
        var queue = new PriorityQueue<MinerInstance, (double Due, long Id)>();

        foreach (MinerInstance miner in _miners)
        {
            if (miner.Exhausted) continue;
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            double rate = Math.Max(0.0, definition.BaseRate * EffectiveRateMultiplier(definition));
            if (rate <= 0.0) continue;

            double initial = Math.Max(0.0, miner.WorkAccumulator);
            initialAccumulators[miner.InstanceId] = initial;
            rates[miner.InstanceId] = rate;
            processed[miner.InstanceId] = 0L;
            double firstDue = Math.Max(0.0, (1.0 - initial) / rate);
            if (firstDue <= seconds) queue.Enqueue(miner, (firstDue, miner.InstanceId));
        }

        double? clearAt = null;
        _mining.BeginCurrencyNotificationBatch();
        _deferVisualUpdates = true;
        try
        {
            while (operationsLeft > 0 && queue.TryDequeue(out MinerInstance? miner, out (double Due, long Id) priority))
            {
                double due = priority.Due;
                if (due > seconds) break;
                if (miner.Exhausted || !rates.TryGetValue(miner.InstanceId, out double rate)) continue;

                MinerDefinition definition = _catalog.Get(miner.DefinitionId);
                operationsLeft--;
                processed[miner.InstanceId] = processed[miner.InstanceId] + 1L;
                _ = Advance(miner, definition, emitPresentation: false);

                if (_world.RemainingMineableBlocks == 0)
                {
                    clearAt = due;
                    break;
                }

                if (miner.Exhausted) continue;
                long count = processed[miner.InstanceId];
                double initial = initialAccumulators[miner.InstanceId];
                double nextDue = Math.Max(0.0, ((count + 1.0) - initial) / rate);
                if (nextDue <= seconds) queue.Enqueue(miner, (nextDue, miner.InstanceId));
            }
        }
        finally
        {
            double accrualSeconds = clearAt ?? seconds;
            foreach (MinerInstance miner in _miners)
            {
                if (!rates.TryGetValue(miner.InstanceId, out double rate)) continue;
                double initial = initialAccumulators[miner.InstanceId];
                long count = processed[miner.InstanceId];
                miner.WorkAccumulator = Math.Max(0.0, initial + rate * accrualSeconds - count);
            }

            _deferVisualUpdates = false;
            try
            {
                FlushDeferredVisualUpdates();
            }
            finally
            {
                _mining.EndCurrencyNotificationBatch();
            }
        }

        long mined = _mining.TotalMined - minedBefore;
        if (mined > 0) Changed?.Invoke();
        double consumed = mined > 0 ? clearAt ?? seconds : 0.0;
        return new OfflineProgressResult(mined, consumed, clearAt);
    }
'''
write("src/Automation/MinerSimulationService.cs", text[:start] + method + text[end:])

# -----------------------------------------------------------------------------
# Camera and skill-tree gating
# -----------------------------------------------------------------------------
replace_once(
    "src/Presentation/OrbitCameraController.cs",
    "    private double _mouseIdleSeconds;\n\n    public string ActivePresetName",
    "    private double _mouseIdleSeconds;\n"
    "    private bool _cinematicFocus;\n\n"
    "    public bool InputEnabled { get; set; } = true;\n"
    "    public string ActivePresetName",
)
replace_once(
    "src/Presentation/OrbitCameraController.cs",
    "        Vector3 radial = Transform.Basis.Z.Normalized();\n        float supportRadius = SurfaceRadiusAlong(radial);\n        Vector3 pivot = _pan + radial * supportRadius * _surfaceFocusBlend;\n        Position = pivot;",
    "        Vector3 radial = Transform.Basis.Z.Normalized();\n"
    "        Vector3 pivot;\n"
    "        if (_cinematicFocus)\n"
    "        {\n"
    "            _surfaceFocusBlend = 0.0f;\n"
    "            pivot = _pan;\n"
    "        }\n"
    "        else\n"
    "        {\n"
    "            float supportRadius = SurfaceRadiusAlong(radial);\n"
    "            pivot = _pan + radial * supportRadius * _surfaceFocusBlend;\n"
    "        }\n"
    "        Position = pivot;",
)
replace_once(
    "src/Presentation/OrbitCameraController.cs",
    "    public override void _UnhandledInput(InputEvent @event)\n    {\n        if (@event is InputEventMouseButton button)",
    "    public override void _UnhandledInput(InputEvent @event)\n"
    "    {\n"
    "        if (!InputEnabled) return;\n"
    "        if (@event is InputEventMouseButton button)",
)
replace_once(
    "src/Presentation/OrbitCameraController.cs",
    "    public void Recenter()\n    {\n        _targetPan = Vector3.Zero;\n        ResetIdleOrbit();\n    }",
    "    public void Recenter()\n"
    "    {\n"
    "        _targetPan = Vector3.Zero;\n"
    "        ResetIdleOrbit();\n"
    "    }\n\n"
    "    public void BeginCinematicFocus(Vector3 center, float distance, bool immediate = false)\n"
    "    {\n"
    "        _cinematicFocus = true;\n"
    "        InputEnabled = false;\n"
    "        _targetPan = center;\n"
    "        _targetDistance = Math.Max(1.0f, distance);\n"
    "        ResetIdleOrbit();\n"
    "        if (!immediate) return;\n"
    "        _pan = _targetPan;\n"
    "        _distance = _targetDistance;\n"
    "    }\n\n"
    "    public void EndCinematicFocus(bool restoreInput = true)\n"
    "    {\n"
    "        _cinematicFocus = false;\n"
    "        if (restoreInput) InputEnabled = true;\n"
    "        ResetIdleOrbit();\n"
    "    }",
)

replace_once(
    "src/UI/SkillTreeView.cs",
    "    public bool IsOpen => _root is not null && _root.Visible;",
    "    public bool IsOpen => _root is not null && _root.Visible;\n"
    "    public bool InteractionEnabled { get; set; } = true;",
)
replace_once(
    "src/UI/SkillTreeView.cs",
    "        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;\n\n        if (key.Keycode == Key.K)",
    "        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;\n"
    "        if (!InteractionEnabled && key.Keycode != Key.Escape) return;\n\n"
    "        if (key.Keycode == Key.K)",
)
replace_once(
    "src/UI/SkillTreeView.cs",
    "    public void Open()\n    {\n        _transition?.Kill();",
    "    public void Open()\n"
    "    {\n"
    "        if (!InteractionEnabled) return;\n"
    "        _transition?.Kill();",
)

# -----------------------------------------------------------------------------
# Existing WorldView batched-instance intro wave
# -----------------------------------------------------------------------------
write(
    "src/World/Rendering/WorldView.IntroWave.cs",
    r'''using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly record struct IntroWaveInstance(
        MultiMesh MultiMesh,
        int Index,
        Transform3D BaseTransform,
        float NormalizedScreenX);

    private readonly List<IntroWaveInstance> _introWaveInstances = new();
    private bool _introWavePrepared;

    public int IntroWaveInstanceCount => _introWaveInstances.Count;

    public void PrepareIntroWave(Camera3D camera)
    {
        _introWaveInstances.Clear();
        _introWavePrepared = false;
        if (camera is null || _world is null) return;

        var pending = new List<(MultiMesh Mesh, int Index, Transform3D Transform, float ScreenX)>();
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;

        foreach (Node3D root in _chunkRoots.Values)
        {
            CollectIntroWaveInstances(root, camera, pending, ref minX, ref maxX);
        }

        if (pending.Count == 0) return;
        float width = Math.Max(1.0f, maxX - minX);
        foreach ((MultiMesh mesh, int index, Transform3D transform, float screenX) in pending)
        {
            _introWaveInstances.Add(new IntroWaveInstance(
                mesh,
                index,
                transform,
                Mathf.Clamp((screenX - minX) / width, 0.0f, 1.0f)));
        }
        _introWavePrepared = true;
    }

    public void UpdateIntroWave(double elapsedSeconds)
    {
        if (!_introWavePrepared || _introWaveInstances.Count == 0) return;
        float t = Math.Max(0.0f, (float)elapsedSeconds);
        float spacing = _world.Profile.BlockSpacing;
        const float firstDelay = 0.25f;
        const float delaySpan = 1.90f;
        const float pulseDuration = 0.72f;

        foreach (IntroWaveInstance item in _introWaveInstances)
        {
            float local = (t - (firstDelay + item.NormalizedScreenX * delaySpan)) / pulseDuration;
            float lift = 0.0f;
            if (local > 0.0f && local < 1.0f)
            {
                float wave = MathF.Sin(local * Mathf.Pi);
                lift = MathF.Pow(Math.Max(0.0f, wave), 1.12f) * spacing * 0.56f;
            }

            Transform3D moved = item.BaseTransform;
            moved.Origin += Vector3.Up * lift;
            item.MultiMesh.SetInstanceTransform(item.Index, moved);
        }
    }

    public void ResetIntroWave()
    {
        foreach (IntroWaveInstance item in _introWaveInstances)
            item.MultiMesh.SetInstanceTransform(item.Index, item.BaseTransform);
        _introWaveInstances.Clear();
        _introWavePrepared = false;
    }

    private void CollectIntroWaveInstances(
        Node node,
        Camera3D camera,
        List<(MultiMesh Mesh, int Index, Transform3D Transform, float ScreenX)> pending,
        ref float minX,
        ref float maxX)
    {
        if (node is MultiMeshInstance3D batch && batch.Multimesh is MultiMesh multiMesh)
        {
            int visible = multiMesh.VisibleInstanceCount < 0 ? multiMesh.InstanceCount : multiMesh.VisibleInstanceCount;
            bool treeBatch = batch.Name.ToString().Contains("tree_", StringComparison.OrdinalIgnoreCase);
            for (int index = 0; index < visible; index++)
            {
                Transform3D transform = multiMesh.GetInstanceTransform(index);
                bool include;
                if (treeBatch)
                {
                    Vector3 localUp = transform.Basis.Y;
                    include = localUp.LengthSquared() > 0.0001f && localUp.Normalized().Dot(Vector3.Up) > 0.72f;
                }
                else if (_world.Profile.UsesSingleBlockGenerator)
                {
                    include = true;
                }
                else
                {
                    float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
                    Vector3 origin = transform.Origin;
                    var voxel = new Vector3I(
                        Mathf.RoundToInt(origin.X / spacing),
                        Mathf.RoundToInt(origin.Y / spacing),
                        Mathf.RoundToInt(origin.Z / spacing));
                    BlockSample sample = _world.SampleVoxel(voxel);
                    include = sample.Present && _world.Source.GetOutwardNormal(voxel) == Vector3I.Up;
                }

                if (!include) continue;
                Vector3 global = batch.ToGlobal(transform.Origin);
                if (camera.IsPositionBehind(global)) continue;
                float screenX = camera.UnprojectPosition(global).X;
                minX = Math.Min(minX, screenX);
                maxX = Math.Max(maxX, screenX);
                pending.Add((multiMesh, index, transform, screenX));
            }
        }

        foreach (Node child in node.GetChildren())
            CollectIntroWaveInstances(child, camera, pending, ref minX, ref maxX);
    }
}
''',
)

# -----------------------------------------------------------------------------
# GPU end-of-world ceremony
# -----------------------------------------------------------------------------
write(
    "src/Presentation/WorldCompletionCeremony.cs",
    r'''using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.Presentation;

public enum WorldCompletionVisualStage
{
    Implosion,
    BonusScatter,
    BlackHoleSuction,
    Finished,
}

/// <summary>
/// Presentation-only end-of-world effect. The exact bonus count is rendered by GPU particles; no
/// particle has a C# Node, collision body or per-particle process callback.
/// </summary>
public partial class WorldCompletionCeremony : Node3D
{
    private const float ScatterStart = 0.72f;
    private const float BlackHoleStart = 3.15f;
    private const float SuctionStart = 3.55f;
    private const float FinishAt = 6.15f;

    private readonly List<ShaderMaterial> _particleMaterials = new();
    private Camera3D _camera = null!;
    private MeshInstance3D _implosionShell = null!;
    private MeshInstance3D _blackCore = null!;
    private MeshInstance3D _accretion = null!;
    private OmniLight3D _flash = null!;
    private double _elapsed;
    private bool _finished;
    private WorldCompletionVisualStage _stage = WorldCompletionVisualStage.Implosion;
    private bool _reducedMotion;

    public event Action<WorldCompletionVisualStage>? StageChanged;
    public event Action? Completed;
    public long BonusParticleCount { get; private set; }

    public void Initialize(
        WorldProfile profile,
        BlockAssetRegistry assets,
        Camera3D camera,
        Vector3 center,
        long bonusParticles,
        float scatterRadius)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Position = center;
        BonusParticleCount = Math.Max(0L, bonusParticles);
        _reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;
        BuildImplosion(profile.BlockSpacing, scatterRadius);
        BuildBlackHole(profile.BlockSpacing);
        BuildBonusParticles(profile, assets, scatterRadius);
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        double speed = _reducedMotion ? 2.15 : 1.0;
        _elapsed += Math.Max(0.0, delta) * speed;
        float time = (float)_elapsed;

        foreach (ShaderMaterial material in _particleMaterials)
            material.SetShaderParameter("visual_time", time);

        UpdateImplosion(time);
        UpdateBlackHole(time, delta * speed);

        WorldCompletionVisualStage next = time < ScatterStart
            ? WorldCompletionVisualStage.Implosion
            : time < SuctionStart
                ? WorldCompletionVisualStage.BonusScatter
                : time < FinishAt
                    ? WorldCompletionVisualStage.BlackHoleSuction
                    : WorldCompletionVisualStage.Finished;
        if (next != _stage)
        {
            _stage = next;
            StageChanged?.Invoke(next);
        }

        if (time < FinishAt) return;
        _finished = true;
        Completed?.Invoke();
    }

    private void BuildImplosion(float spacing, float scatterRadius)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.38f, 0.92f, 1.0f, 0.30f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.22f, 0.86f, 1.0f),
            EmissionEnergyMultiplier = 4.0f,
        };
        _implosionShell = new MeshInstance3D
        {
            Name = "ImplosionShell",
            Mesh = new SphereMesh
            {
                Radius = Math.Max(spacing * 0.8f, scatterRadius * 0.16f),
                Height = Math.Max(spacing * 1.6f, scatterRadius * 0.32f),
                RadialSegments = 24,
                Rings = 12,
            },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_implosionShell);

        _flash = new OmniLight3D
        {
            Name = "ImplosionFlash",
            LightColor = new Color(0.48f, 0.90f, 1.0f),
            LightEnergy = 0.0f,
            OmniRange = Math.Max(spacing * 8.0f, scatterRadius * 1.25f),
            ShadowEnabled = false,
        };
        AddChild(_flash);
    }

    private void BuildBlackHole(float spacing)
    {
        var coreMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0002f, 0.0004f, 0.0010f, 1.0f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _blackCore = new MeshInstance3D
        {
            Name = "BlackHoleCore",
            Mesh = new SphereMesh
            {
                Radius = spacing * 0.88f,
                Height = spacing * 1.76f,
                RadialSegments = 32,
                Rings = 16,
            },
            MaterialOverride = coreMaterial,
            Scale = Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_blackCore);

        var accretionMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.74f, 1.0f, 0.48f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.12f, 0.68f, 1.0f),
            EmissionEnergyMultiplier = 5.5f,
        };
        _accretion = new MeshInstance3D
        {
            Name = "AccretionGlow",
            Mesh = new SphereMesh
            {
                Radius = spacing * 1.75f,
                Height = spacing * 3.50f,
                RadialSegments = 32,
                Rings = 16,
            },
            MaterialOverride = accretionMaterial,
            Scale = Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_accretion);
    }

    private void BuildBonusParticles(WorldProfile profile, BlockAssetRegistry assets, float scatterRadius)
    {
        if (BonusParticleCount <= 0) return;

        var visuals = new List<string>();
        AddVisual(visuals, profile.SurfaceBlock, assets);
        AddVisual(visuals, profile.SoilBlock, assets);
        AddVisual(visuals, profile.StoneBlock, assets);
        AddVisual(visuals, profile.GoldBlock, assets);
        if (visuals.Count == 0) visuals.Add(profile.StoneBlock);

        long remaining = BonusParticleCount;
        for (int visualIndex = 0; visualIndex < visuals.Count && remaining > 0; visualIndex++)
        {
            int slotsLeft = visuals.Count - visualIndex;
            long share = visualIndex == visuals.Count - 1
                ? remaining
                : Math.Max(1L, remaining / slotsLeft);
            remaining -= share;
            if (share > int.MaxValue)
                throw new InvalidOperationException($"Completion particle emitter exceeds Godot Amount range: {share:N0}.");

            string blockId = visuals[visualIndex];
            var shader = new Shader { Code = ParticleShaderCode };
            var processMaterial = new ShaderMaterial { Shader = shader };
            processMaterial.SetShaderParameter("visual_time", 0.0f);
            processMaterial.SetShaderParameter("scatter_radius", scatterRadius);
            processMaterial.SetShaderParameter("hop_height", Math.Max(profile.BlockSpacing * 1.8f, scatterRadius * 0.18f));
            processMaterial.SetShaderParameter("particle_scale", 0.13f);
            processMaterial.SetShaderParameter("seed_offset", visualIndex * 971.0f + profile.Seed * 0.013f);
            _particleMaterials.Add(processMaterial);

            float extent = scatterRadius * 1.35f + profile.BlockSpacing * 4.0f;
            var particles = new GpuParticles3D
            {
                Name = $"Bonus_{blockId}_{visualIndex}",
                Amount = (int)share,
                Lifetime = 12.0,
                OneShot = false,
                FixedFps = 45,
                Interpolate = true,
                ProcessMaterial = processMaterial,
                DrawPass1 = assets.GetMesh(blockId),
                MaterialOverride = assets.GetMaterialOverride(blockId),
                VisibilityAabb = new Aabb(new Vector3(-extent, -extent, -extent), Vector3.One * extent * 2.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Emitting = true,
            };
            AddChild(particles);
            particles.Restart();
        }
    }

    private static void AddVisual(List<string> visuals, string blockId, BlockAssetRegistry assets)
    {
        if (string.IsNullOrWhiteSpace(blockId) || visuals.Contains(blockId)) return;
        _ = assets.GetMesh(blockId); // fail early if content is invalid
        visuals.Add(blockId);
    }

    private void UpdateImplosion(float time)
    {
        if (time >= ScatterStart)
        {
            _implosionShell.Visible = false;
            _flash.LightEnergy = 0.0f;
            return;
        }

        float t = Mathf.Clamp(time / ScatterStart, 0.0f, 1.0f);
        float collapse = MathF.Pow(1.0f - t, 2.2f);
        _implosionShell.Scale = Vector3.One * Mathf.Lerp(3.6f, 0.04f, 1.0f - collapse);
        _flash.LightEnergy = MathF.Sin(t * Mathf.Pi) * 8.0f;
    }

    private void UpdateBlackHole(float time, double delta)
    {
        if (time < BlackHoleStart)
        {
            _blackCore.Scale = Vector3.Zero;
            _accretion.Scale = Vector3.Zero;
            return;
        }

        float appear = Mathf.Clamp((time - BlackHoleStart) / 0.42f, 0.0f, 1.0f);
        float eased = 1.0f - MathF.Pow(1.0f - appear, 3.0f);
        _blackCore.Scale = Vector3.One * eased;
        _accretion.Scale = new Vector3(1.65f, 0.17f, 1.65f) * eased;
        _accretion.RotateY((float)delta * 2.8f);
        _accretion.RotateZ((float)delta * 0.55f);
    }

    private const string ParticleShaderCode = @"shader_type particles;
render_mode disable_force;

uniform float visual_time = 0.0;
uniform float scatter_radius = 20.0;
uniform float hop_height = 4.0;
uniform float particle_scale = 0.13;
uniform float seed_offset = 0.0;

float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float ease_out_cubic(float x) {
    float q = 1.0 - clamp(x, 0.0, 1.0);
    return 1.0 - q * q * q;
}

void process() {
    float id = float(INDEX) + seed_offset;
    float rnd_a = hash11(id * 1.137 + 3.71);
    float rnd_r = hash11(id * 2.913 + 8.41);
    float rnd_d = hash11(id * 5.171 + 1.91);
    float rnd_s = hash11(id * 7.337 + 6.13);
    float angle = rnd_a * 6.28318530718;
    float radius = scatter_radius * mix(0.13, 1.0, sqrt(rnd_r));
    float delay = rnd_d * 0.34;

    float scatter_t = clamp((visual_time - 0.72 - delay) / 1.75, 0.0, 1.0);
    float scatter_eased = ease_out_cubic(scatter_t);
    float hop = sin(scatter_t * 3.14159265) * hop_height * mix(0.50, 1.0, rnd_s);
    vec3 direction = vec3(cos(angle), 0.0, sin(angle));
    vec3 settled = direction * radius;
    vec3 position = direction * radius * scatter_eased + vec3(0.0, hop, 0.0);

    float suction_t = clamp((visual_time - 3.55 - delay * 0.35) / 2.35, 0.0, 1.0);
    if (suction_t > 0.0) {
        float remaining = pow(max(0.0, 1.0 - suction_t), 2.35);
        float turns = mix(3.2, 7.5, rnd_s);
        float spiral_angle = angle + suction_t * turns * 6.28318530718;
        float spiral_radius = radius * remaining;
        position = vec3(cos(spiral_angle) * spiral_radius,
                        sin(spiral_angle * 1.7 + rnd_d * 6.2831853) * spiral_radius * 0.10,
                        sin(spiral_angle) * spiral_radius);
    } else if (scatter_t >= 1.0) {
        position = settled;
    }

    float appear = step(0.72 + delay, visual_time);
    float vanish = 1.0 - smoothstep(0.78, 1.0, suction_t);
    float scale = particle_scale * appear * vanish;
    float spin = visual_time * mix(1.0, 3.5, rnd_s) + rnd_a * 6.2831853;
    float c = cos(spin);
    float s = sin(spin);

    TRANSFORM[0] = vec4(c * scale, 0.0, -s * scale, 0.0);
    TRANSFORM[1] = vec4(0.0, scale, 0.0, 0.0);
    TRANSFORM[2] = vec4(s * scale, 0.0, c * scale, 0.0);
    TRANSFORM[3] = vec4(position, 1.0);
    VELOCITY = vec3(0.0);
}";
}
''',
)

# -----------------------------------------------------------------------------
# Authoritative GameRoot lifecycle + completion transaction
# -----------------------------------------------------------------------------
write(
    "src/App/GameRoot.WorldCeremony.cs",
    r'''using System;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Progression;
using TenMillionBlocks.Save;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private enum WorldRunPhase
    {
        PreparingWorld,
        IntroLocked,
        Playing,
        CompletionLocked,
        Implosion,
        BonusScatter,
        BlackHoleSuction,
        Results,
    }

    private const double WorldIntroDurationSeconds = 3.0;

    private WorldRunPhase _runPhase = WorldRunPhase.PreparingWorld;
    private double _introElapsed;
    private double _activePlaySeconds;
    private bool _clearReached;
    private double _completionClearSeconds;
    private int _completionScorePercent;
    private long _completionBonusResources;
    private bool _completionBonusClaimed;
    private bool _loadedCompletedWorld;
    private WorldCompletionCeremony? _completionCeremony;

    private void ResetWorldRunLifecycle()
    {
        _worldView?.ResetIntroWave();
        if (_completionCeremony is not null && GodotObject.IsInstanceValid(_completionCeremony))
        {
            _completionCeremony.QueueFree();
        }
        _completionCeremony = null;
        _camera?.EndCinematicFocus(restoreInput: true);
        _runPhase = WorldRunPhase.PreparingWorld;
        _introElapsed = 0.0;
        _activePlaySeconds = 0.0;
        _clearReached = false;
        _completionClearSeconds = 0.0;
        _completionScorePercent = 0;
        _completionBonusResources = 0L;
        _completionBonusClaimed = false;
        _loadedCompletedWorld = false;
    }

    private void InitializeWorldRunLifecycle(WorldSaveData? savedWorld, OfflineProgressResult offline)
    {
        _runPhase = WorldRunPhase.PreparingWorld;
        _introElapsed = 0.0;
        _activePlaySeconds = Math.Max(0.0, savedWorld?.ActivePlaySeconds ?? 0.0);
        _clearReached = savedWorld?.ClearReached ?? false;
        _completionClearSeconds = Math.Max(0.0, savedWorld?.CompletionClearSeconds ?? 0.0);
        _completionScorePercent = Math.Clamp(savedWorld?.CompletionScorePercent ?? 0, 0, 100);
        _completionBonusResources = Math.Max(0L, savedWorld?.CompletionBonusResources ?? 0L);
        _completionBonusClaimed = savedWorld?.CompletionBonusClaimed ?? false;
        _loadedCompletedWorld = savedWorld?.Completed ?? false;

        if (offline.BlocksRemoved > 0)
        {
            _activePlaySeconds += Math.Max(0.0, offline.SimulatedSecondsConsumed);
            if (offline.ClearedWorld && !_clearReached)
            {
                _completionClearSeconds = _activePlaySeconds;
                _completionScorePercent = CompletionScore.CalculatePercent(_completionClearSeconds);
                _completionBonusResources = CompletionScore.CalculateBonus(_world?.InitialMineableBlocks ?? 0L, _completionScorePercent);
                _clearReached = true;
            }
        }

        SetGameplayInteractionEnabled(false);
    }

    private void ProcessWorldRun(double delta)
    {
        if (!_sessionPersists || _world is null || _worldView is null) return;

        switch (_runPhase)
        {
            case WorldRunPhase.PreparingWorld:
                if (!_worldView.InitialPresentationReady || WorldLoadingScreen.IsActive) return;

                if (_loadedCompletedWorld)
                {
                    _runPhase = WorldRunPhase.Results;
                    ShowCompletion(debugPreview: false);
                    return;
                }

                if (_world.RemainingMineableBlocks == 0)
                {
                    if (!_clearReached) FreezeCompletionResultAndSave();
                    BeginCompletionCinematic();
                    return;
                }

                _camera.InputEnabled = false;
                _worldView.PrepareIntroWave(_camera.Camera);
                _introElapsed = 0.0;
                _runPhase = WorldRunPhase.IntroLocked;
                return;

            case WorldRunPhase.IntroLocked:
                _introElapsed += Math.Max(0.0, delta);
                _worldView.UpdateIntroWave(_introElapsed);
                if (_introElapsed < WorldIntroDurationSeconds) return;
                _worldView.ResetIntroWave();
                _runPhase = WorldRunPhase.Playing;
                SetGameplayInteractionEnabled(true);
                return;

            case WorldRunPhase.Playing:
                _activePlaySeconds += Math.Max(0.0, delta);
                _autosaveDirty = true;
                return;
        }
    }

    private void SetGameplayInteractionEnabled(bool enabled)
    {
        if (_manualMining is not null) _manualMining.InputEnabled = enabled;
        if (_placement is not null) _placement.InputEnabled = enabled && (_world?.Profile.AutomationAvailable ?? false);
        if (_miners is not null) _miners.ProcessMode = enabled ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (_worldEvents is not null) _worldEvents.ProcessMode = enabled ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (_skillTree is not null)
        {
            if (!enabled) _skillTree.Close();
            _skillTree.InteractionEnabled = enabled;
        }
        if (_camera is not null) _camera.InputEnabled = enabled;
    }

    private void FreezeCompletionResultAndSave()
    {
        if (_world is null || _clearReached) return;
        _runPhase = WorldRunPhase.CompletionLocked;
        SetGameplayInteractionEnabled(false);
        _clearReached = true;
        _completionClearSeconds = Math.Max(0.0, _activePlaySeconds);
        _completionScorePercent = CompletionScore.CalculatePercent(_completionClearSeconds);
        _completionBonusResources = CompletionScore.CalculateBonus(_world.InitialMineableBlocks, _completionScorePercent);
        _completionBonusClaimed = false;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        GD.Print($"Clear frozen at {_completionClearSeconds:0.00}s: {_completionScorePercent}% => {_completionBonusResources:N0} bonus resources.");
    }

    private void BeginCompletionCinematic()
    {
        if (_world is null || _worldView is null || _mining is null || _sessionRoot is null || _completionCeremony is not null) return;
        _runPhase = WorldRunPhase.CompletionLocked;
        SetGameplayInteractionEnabled(false);
        _resourceCollection?.CollectAllPending();

        Aabb bounds = _world.GetWorldBounds();
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
        float worldRadius = Math.Max(spacing * 2.0f, bounds.Size.Length() * 0.5f);
        float scatterRadius = Math.Max(spacing * 4.0f, Math.Min(worldRadius * 0.58f, spacing * 20.0f));
        float cameraDistance = Math.Max(scatterRadius * 2.7f, worldRadius * 1.55f);
        _camera.BeginCinematicFocus(center, cameraDistance, immediate: false);

        _completionCeremony = new WorldCompletionCeremony { Name = "WorldCompletionCeremony" };
        _completionCeremony.Initialize(
            _world.Profile,
            _assets,
            _camera.Camera,
            center,
            _completionBonusResources,
            scatterRadius);
        _completionCeremony.StageChanged += OnCompletionVisualStageChanged;
        _completionCeremony.Completed += CommitCompletionRewardAndShowResults;
        _sessionRoot.AddChild(_completionCeremony);
    }

    private void OnCompletionVisualStageChanged(WorldCompletionVisualStage stage)
    {
        _runPhase = stage switch
        {
            WorldCompletionVisualStage.Implosion => WorldRunPhase.Implosion,
            WorldCompletionVisualStage.BonusScatter => WorldRunPhase.BonusScatter,
            WorldCompletionVisualStage.BlackHoleSuction => WorldRunPhase.BlackHoleSuction,
            _ => WorldRunPhase.CompletionLocked,
        };
    }

    private void CommitCompletionRewardAndShowResults()
    {
        if (_world is null || _mining is null || _completionBonusClaimed) return;

        _completionBonusClaimed = true;
        if (_completionBonusResources > 0) _mining.GrantCurrency(_completionBonusResources);

        WorldProfile? next = _progression.NextProfile();
        _save.CompletedWorldIds.Add(_world.Profile.Id);
        if (next is not null) _save.UnlockedWorldIds.Add(next.Id);
        _loadedCompletedWorld = true;
        _runPhase = WorldRunPhase.Results;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        ShowCompletion(debugPreview: false);
    }

    private static string FormatClearTime(double seconds)
    {
        int total = Math.Max(0, (int)Math.Floor(seconds));
        int minutes = total / 60;
        int remainder = total % 60;
        return $"{minutes:00}:{remainder:00}";
    }
}
''',
)

# GameRoot process/build/completion integration.
replace_once(
    "src/App/GameRoot.cs",
    "    public override void _Process(double delta)\n    {\n        if (!_autosaveDirty || _world is null) return;",
    "    public override void _Process(double delta)\n"
    "    {\n"
    "        ProcessWorldRun(delta);\n"
    "        if (!_autosaveDirty || _world is null) return;",
)
replace_once(
    "src/App/GameRoot.cs",
    "        TearDownWorldSession();\n        _sessionPersists = persistSession;",
    "        TearDownWorldSession();\n"
    "        ResetWorldRunLifecycle();\n"
    "        _sessionPersists = persistSession;",
)
# Replace offline block and final TryCompleteWorld call.
old_offline = '''        if (profile.AutomationAvailable && persistSession && applyOfflineProgress && savedWorld is not null && _loadedSaveTimestamp > 0)\n        {\n            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();\n            double elapsed = Math.Max(0L, now - _loadedSaveTimestamp);\n            long offlineMined = _miners.ApplyOfflineProgress(elapsed);\n            if (offlineMined > 0)\n            {\n                GD.Print($"Applied {offlineMined:N0} exact offline mining operations after {elapsed:0} seconds away.");\n                MarkAutosaveDirty();\n            }\n        }\n\n        TryCompleteWorld();'''
new_offline = '''        OfflineProgressResult offlineProgress = default;\n        if (profile.AutomationAvailable && persistSession && applyOfflineProgress && savedWorld is not null && _loadedSaveTimestamp > 0)\n        {\n            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();\n            double elapsed = Math.Max(0L, now - _loadedSaveTimestamp);\n            offlineProgress = _miners.ApplyOfflineProgress(elapsed);\n            if (offlineProgress.BlocksRemoved > 0)\n            {\n                string clearSuffix = offlineProgress.SecondsToWorldClear is double clearAt\n                    ? $"; world cleared at +{clearAt:0.00}s"\n                    : string.Empty;\n                GD.Print($"Applied {offlineProgress.BlocksRemoved:N0} exact offline mining operations after {elapsed:0} seconds away{clearSuffix}.");\n                MarkAutosaveDirty();\n            }\n        }\n\n        InitializeWorldRunLifecycle(savedWorld, offlineProgress);'''
replace_once("src/App/GameRoot.cs", old_offline, new_offline)

# ShowCompletion becomes presentation-only; authoritative completion lives in WorldCeremony partial.
text = read("src/App/GameRoot.cs")
start = text.index("    private void ShowCompletion(bool debugPreview)")
end = text.index("\n    private void OnContinueRequested()", start)
replacement = r'''    private void ShowCompletion(bool debugPreview)
    {
        if (!_sessionPersists || _world is null || _mining is null || _manualMining is null || _miners is null || _placement is null) return;

        _completionShown = true;
        SetGameplayInteractionEnabled(false);
        WorldProfile? next = _progression.NextProfile();

        double clearSeconds = debugPreview ? _activePlaySeconds : _completionClearSeconds;
        int scorePercent = debugPreview
            ? CompletionScore.CalculatePercent(clearSeconds)
            : _completionScorePercent;
        long bonus = debugPreview
            ? CompletionScore.CalculateBonus(_world.InitialMineableBlocks, scorePercent)
            : _completionBonusResources;

        _completionView.ShowCompletion(
            _world.Profile,
            next,
            _mining.TotalMined,
            _mining.Currency,
            _manualBlocksThisWorld,
            _automatedBlocksThisWorld,
            clearSeconds,
            scorePercent,
            bonus,
            replayAvailable: !debugPreview && ReplayAvailableForCurrentWorld());

        if (debugPreview)
            GD.Print("DEBUG: showing completion results preview without changing progression or granting the bonus.");
    }
'''
write("src/App/GameRoot.cs", text[:start] + replacement + text[end:])

# Persist lifecycle fields with the world snapshot.
replace_once(
    "src/App/GameRoot.cs",
    "            AutomatedBlocksMined = _automatedBlocksThisWorld,\n            HoverMiningEnabled = _manualMining.HoverMiningEnabled,",
    "            AutomatedBlocksMined = _automatedBlocksThisWorld,\n"
    "            ActivePlaySeconds = _activePlaySeconds,\n"
    "            ClearReached = _clearReached || (previous?.ClearReached ?? false),\n"
    "            CompletionClearSeconds = _clearReached ? _completionClearSeconds : previous?.CompletionClearSeconds ?? 0.0,\n"
    "            CompletionScorePercent = _clearReached ? _completionScorePercent : previous?.CompletionScorePercent ?? 0,\n"
    "            CompletionBonusResources = _clearReached ? _completionBonusResources : previous?.CompletionBonusResources ?? 0L,\n"
    "            CompletionBonusClaimed = _completionBonusClaimed || (previous?.CompletionBonusClaimed ?? false),\n"
    "            HoverMiningEnabled = _manualMining.HoverMiningEnabled,",
)

# Resource completion no longer jumps straight to results.
write(
    "src/App/GameRoot.ResourceCollection.cs",
    r'''using TenMillionBlocks.Collection;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private ResourceCollectionField? _resourceCollection;

    private void OnPendingCollectionChanged()
    {
        MarkAutosaveDirty();
        TryCompleteWorld();
    }

    private void TryCompleteWorld()
    {
        if (!_sessionPersists || _world is null || _world.RemainingMineableBlocks != 0 || _completionShown) return;
        if (_runPhase != WorldRunPhase.Playing) return;

        _resourceCollection?.CollectAllPending();
        if ((_resourceCollection?.PendingCount ?? 0) != 0 || _runPhase != WorldRunPhase.Playing) return;

        FreezeCompletionResultAndSave();
        BeginCompletionCinematic();
    }
}
''',
)

# -----------------------------------------------------------------------------
# Results UI: score/time/bonus are primary
# -----------------------------------------------------------------------------
replace_once(
    "src/UI/WorldCompleteView.cs",
    "        long manualBlocks,\n        long automatedBlocks,\n        bool replayAvailable)",
    "        long manualBlocks,\n"
    "        long automatedBlocks,\n"
    "        double clearSeconds,\n"
    "        int scorePercent,\n"
    "        long bonusResources,\n"
    "        bool replayAvailable)",
)
replace_once(
    "src/UI/WorldCompleteView.cs",
    "        _stats.Text = $\"{blocksMined:N0} BLOCKS REMOVED\\n{sourceLine}\\n{resources:N0} RESOURCES AVAILABLE\";",
    "        int totalSeconds = Math.Max(0, (int)Math.Floor(clearSeconds));\n"
    "        string clearTime = $\"{totalSeconds / 60:00}:{totalSeconds % 60:00}\";\n"
    "        _stats.Text =\n"
    "            $\"CLEAR TIME   {clearTime}\\n\" +\n"
    "            $\"SPEED SCORE  {Math.Clamp(scorePercent, 0, 100)}%\\n\" +\n"
    "            $\"BLACK HOLE BONUS   +{Math.Max(0L, bonusResources):N0}\\n\" +\n"
    "            $\"TOTAL RESOURCES    {resources:N0}\\n\\n\" +\n"
    "            $\"{blocksMined:N0} BLOCKS REMOVED\\n{sourceLine}\";",
)

# -----------------------------------------------------------------------------
# Score boundary contract and CI hook
# -----------------------------------------------------------------------------
write(
    "tools/completion_contract/CompletionContract.csproj",
    '''<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net8.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n  <ItemGroup>\n    <Compile Include="../../src/Progression/CompletionScore.cs" Link="CompletionScore.cs" />\n  </ItemGroup>\n</Project>\n''',
)
write(
    "tools/completion_contract/Program.cs",
    r'''using TenMillionBlocks.Progression;

static void Expect(double seconds, int expected)
{
    int actual = CompletionScore.CalculatePercent(seconds);
    if (actual != expected) throw new InvalidOperationException($"{seconds}s => {actual}% (expected {expected}%).");
}

Expect(0, 100);
Expect(299.999, 100);
Expect(300, 90);
Expect(599.999, 90);
Expect(600, 80);
Expect(899.999, 80);
Expect(900, 70);
Expect(1199.999, 70);
Expect(1200, 60);
Expect(1499.999, 60);
Expect(1500, 50);
Expect(1799.999, 50);
Expect(1800, 40);
Expect(2099.999, 40);
Expect(2100, 30);
Expect(2399.999, 30);
Expect(2400, 20);
Expect(99999, 20);

if (CompletionScore.CalculateBonus(10_000, 70) != 7_000)
    throw new InvalidOperationException("10,000 blocks at 70% must award 7,000.");
if (CompletionScore.CalculateBonus(6_824, 20) != 1_365)
    throw new InvalidOperationException("6,824 blocks at 20% must round to 1,365.");

Console.WriteLine("Completion score contract passed.");
''',
)
replace_once(
    ".github/workflows/build.yml",
    "      - name: Validate replay codec\n        run: dotnet run --project tools/replay_contract/ReplayContract.csproj --configuration Release",
    "      - name: Validate replay codec\n"
    "        run: dotnet run --project tools/replay_contract/ReplayContract.csproj --configuration Release\n\n"
    "      - name: Validate completion score\n"
    "        run: dotnet run --project tools/completion_contract/CompletionContract.csproj --configuration Release",
)

# Documentation checkpoint.
replace_once(
    "docs/WORLD_INTRO_AND_BLACK_HOLE_COMPLETION_PLAN.md",
    "Status: **planning / implementation handoff only**",
    "Status: **implemented on `codex/future-world-progression`; local Godot visual/performance validation required**",
)
status = read("docs/IMPLEMENTATION_STATUS.md")
marker = "## World ceremony / black-hole completion"
if marker not in status:
    status += r'''

---

## World ceremony / black-hole completion

Implemented on the active branch:

- every playable world load is interaction-locked until its initial presentation is ready and a three-second top-surface wave completes;
- the wave uses the real currently saved `WorldView` batches and travels screen-left to screen-right from the locked initial camera;
- Esc pause remains available once the loading overlay has dismissed, and SceneTree pause freezes the ceremony naturally;
- per-world `ActivePlaySeconds` is authoritative/persistent and advances only during active gameplay;
- offline automation is simulated chronologically and reports an exact clear offset when it finishes a world;
- final-block removal freezes and saves clear time/score/bonus before the cinematic begins;
- speed score loses 10 percentage points every five minutes from 100% to a 20% floor;
- outstanding ordinary pickups resolve before the completion presentation;
- completion recenters/locks the camera, implodes at the old cube center, emits exactly one GPU particle per bonus resource, scatters them radially, then spawns a black-hole visual and spirals them inward;
- the exact bonus is granted once as a single authoritative currency transaction after suction;
- pending/claimed completion state is saved so a crash cannot reroll or duplicate a reward;
- results now lead with clear time, speed score and black-hole bonus before Continue/Replay;
- CI includes score-boundary checks at every five-minute threshold.

Remaining gate: run the 20³/40³/50³ ceremony locally and profile the exact-count GPU field, including a future one-million-particle stress pass, then tune only presentation constants if necessary.
'''
    write("docs/IMPLEMENTATION_STATUS.md", status)

print("Applied world intro wave + black-hole completion implementation.")
