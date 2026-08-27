using System;
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
        ArgumentNullException.ThrowIfNull(camera);

        // The particle shader scatters in local X/Z and uses local Y for the hop. Align those axes to
        // camera-right / camera-up / camera-forward so the radial field reads as a true circle on-screen
        // regardless of the player's world rotation. The hop comes slightly toward the viewer, which
        // gives the fake ballistic motion depth without requiring physics.
        Basis cameraBasis = camera.GlobalTransform.Basis.Orthonormalized();
        Basis presentationBasis = new Basis(
            cameraBasis.X,
            -cameraBasis.Z,
            cameraBasis.Y).Orthonormalized();
        GlobalTransform = new Transform3D(presentationBasis, center);

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
                VisibilityAabb = new Aabb(
                    new Vector3(-extent, -extent, -extent),
                    Vector3.One * extent * 2.0f),
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
