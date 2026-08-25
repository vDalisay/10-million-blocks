using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.WorldEvents;

public partial class WorldEventController
{
    private const double AutomaticCloudChargeIntervalSeconds = 3.0;
    private const double RadioactiveCloudPulseIntervalSeconds = 6.0;
    private const int RadioactiveCloudRadius = 1;
    private const int RadioactiveSurfaceSearchDepth = 128;
    private const double OrbBreakerIntervalSeconds = 2.5;
    private const int OrbBreakerRadius = 1;

    private SkillTreeService? _eventSkills;
    private Timer? _cloudChargerTimer;
    private Timer? _radioactiveCloudTimer;
    private Timer? _orbBreakerTimer;
    private Node3D? _breakerOrb;
    private float _breakerOrbPhase;

    public void AttachSkills(SkillTreeService skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (ReferenceEquals(_eventSkills, skills))
        {
            RefreshCloudCharger();
            return;
        }

        if (_eventSkills is not null) _eventSkills.Changed -= RefreshCloudCharger;
        _eventSkills = skills;
        _eventSkills.Changed += RefreshCloudCharger;
        RefreshCloudCharger();
    }

    private void RefreshCloudCharger()
    {
        bool chargerEnabled = _cloudEnabled && _eventSkills is not null && _eventSkills.Derived.AutoCloudChargerUnlocked;
        if (chargerEnabled)
        {
            if (_cloudChargerTimer is null)
            {
                _cloudChargerTimer = new Timer
                {
                    Name = "AutomaticCloudCharger",
                    OneShot = false,
                    Autostart = false,
                };
                _cloudChargerTimer.Timeout += OnAutomaticCloudCharge;
                AddChild(_cloudChargerTimer);
            }

            _cloudChargerTimer.WaitTime = EffectiveCloudChargeInterval();
            if (_cloudChargerTimer.IsStopped()) _cloudChargerTimer.Start();
        }
        else
        {
            _cloudChargerTimer?.Stop();
        }

        bool radioactiveEnabled = _cloudEnabled && _eventSkills is not null && _eventSkills.Derived.RadioactiveCloudUnlocked;
        if (radioactiveEnabled)
        {
            if (_radioactiveCloudTimer is null)
            {
                _radioactiveCloudTimer = new Timer
                {
                    Name = "RadioactiveCloudPulse",
                    WaitTime = RadioactiveCloudPulseIntervalSeconds,
                    OneShot = false,
                    Autostart = false,
                };
                _radioactiveCloudTimer.Timeout += OnRadioactiveCloudPulse;
                AddChild(_radioactiveCloudTimer);
            }

            if (_radioactiveCloudTimer.IsStopped()) _radioactiveCloudTimer.Start();
        }
        else
        {
            _radioactiveCloudTimer?.Stop();
        }

        bool orbEnabled = _eventSkills is not null && _eventSkills.Derived.OrbBreakerUnlocked;
        if (orbEnabled)
        {
            if (_breakerOrb is null)
            {
                _breakerOrbPhase = DeterministicPhase(_world.Profile.Seed + 2609);
                _breakerOrb = BuildBreakerOrb();
                _breakerOrb.GlobalPosition = OrbitPosition(_breakerOrbPhase, 1.15f, -0.04f);
                AddChild(_breakerOrb);
            }

            if (_orbBreakerTimer is null)
            {
                _orbBreakerTimer = new Timer
                {
                    Name = "OrbBreakerPulse",
                    OneShot = false,
                    Autostart = false,
                };
                _orbBreakerTimer.Timeout += OnOrbBreakerPulse;
                AddChild(_orbBreakerTimer);
            }

            _orbBreakerTimer.WaitTime = EffectiveOrbBreakerInterval();
            if (_orbBreakerTimer.IsStopped()) _orbBreakerTimer.Start();
            _breakerOrb.Visible = true;
        }
        else
        {
            _orbBreakerTimer?.Stop();
            if (_breakerOrb is not null) _breakerOrb.Visible = false;
        }

        _meteorCooldown = Math.Min(_meteorCooldown, EffectiveMeteorRespawnDelay());
        RefreshStatus();
    }

    private void OnAutomaticCloudCharge()
    {
        if (_cloud is null || _eventSkills is null || !_eventSkills.Derived.AutoCloudChargerUnlocked || !_cloudEnabled) return;
        ChargeCloud();
        RequestPersistence();
    }

    private void OnRadioactiveCloudPulse()
    {
        if (_cloud is null || _eventSkills is null || !_eventSkills.Derived.RadioactiveCloudUnlocked || !_cloudEnabled) return;
        if (!TryCurrentSurfaceUnder(_cloud.GlobalPosition, out Vector3I target)) return;

        ApplyCrater(target, RadioactiveCloudRadius);
        SpawnFlash(_view.VoxelToWorld(target), new Color(0.42f, 1.0f, 0.48f), 2.4f);
        RequestPersistence();
    }

    private void OnOrbBreakerPulse()
    {
        if (_breakerOrb is null || _eventSkills is null || !_eventSkills.Derived.OrbBreakerUnlocked) return;

        _breakerOrbPhase = Mathf.Wrap(_breakerOrbPhase + 0.82f, 0.0f, Mathf.Tau);
        Vector3 destination = OrbitPosition(_breakerOrbPhase, 1.15f, -0.04f);
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _breakerOrb.GlobalPosition = destination;
        }
        else
        {
            Tween tween = CreateTween();
            tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            tween.TweenProperty(_breakerOrb, "global_position", destination, Math.Min(0.42, EffectiveOrbBreakerInterval() * 0.35));
        }

        if (!TryCurrentSurfaceUnder(destination, out Vector3I target)) return;
        ApplyCrater(target, OrbBreakerRadius);
        SpawnFlash(_view.VoxelToWorld(target), new Color(0.46f, 0.82f, 1.0f), 2.1f);
        RequestPersistence();
    }

    /// <summary>
    /// The generation source knows the original outer shell, while passive systems need the current
    /// excavation front. Start from the authored outer surface and walk inward along that face until an
    /// authoritative present voxel is found. This keeps idle mining advancing instead of repeatedly
    /// pulsing a coordinate that an earlier pass already removed.
    /// </summary>
    private bool TryCurrentSurfaceUnder(Vector3 worldPosition, out Vector3I voxel)
    {
        voxel = default;
        if (!TrySurfaceUnder(worldPosition, out Vector3I originalSurface)) return false;

        Vector3I outward = DominantNormal(originalSurface);
        for (int depth = 0; depth < RadioactiveSurfaceSearchDepth; depth++)
        {
            Vector3I candidate = originalSurface - outward * depth;
            BlockSample sample = _world.SampleVoxel(candidate);
            if (!sample.Present || !sample.Mineable) continue;
            voxel = candidate;
            return true;
        }

        return false;
    }

    private Node3D BuildBreakerOrb()
    {
        float radius = MathF.Max(0.34f, _world.Profile.BlockSpacing * 0.26f);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.52f, 0.82f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.28f, 0.68f, 1.0f),
            EmissionEnergyMultiplier = 2.1f,
            Roughness = 0.32f,
        };
        var root = new Node3D { Name = "BreakerOrb" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = radius,
                Height = radius * 2.0f,
                RadialSegments = 12,
                Rings = 8,
                Material = material,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    private double EffectiveCloudChargeInterval()
        => AutomaticCloudChargeIntervalSeconds / Math.Max(0.1, _eventSkills?.Derived.CloudChargeRateMultiplier ?? 1.0);

    private double EffectiveOrbBreakerInterval()
        => OrbBreakerIntervalSeconds / Math.Max(0.1, _eventSkills?.Derived.OrbBreakerRateMultiplier ?? 1.0);

    private double EffectiveMeteorRespawnDelay()
        => MeteorRespawnDelay / Math.Max(0.1, _eventSkills?.Derived.MeteorSpawnRateMultiplier ?? 1.0);

    private int EffectiveLightningRadius()
        => Math.Clamp(LightningRadius + (_eventSkills?.Derived.LightningRadiusBonus ?? 0), 1, 10);

    private int EffectiveMeteorRadius()
        => Math.Clamp(MeteorRadius + (_eventSkills?.Derived.MeteorRadiusBonus ?? 0), 1, 12);

    private void ApplyLightning(Vector3I target)
    {
        int radius = EffectiveLightningRadius();
        ApplyCrater(target, radius);
        SpawnFlash(_view.VoxelToWorld(target), new Color(0.76f, 0.88f, 1.0f), 8.0f);

        int chains = Math.Clamp(_eventSkills?.Derived.LightningChainCount ?? 0, 0, 4);
        if (chains <= 0) return;

        var used = new List<Vector3I>(chains + 1) { target };
        Vector3I current = target;
        for (int index = 0; index < chains; index++)
        {
            if (!TryFindLightningFork(current, radius, used, index, out Vector3I fork)) break;
            used.Add(fork);
            current = fork;
            ApplyCrater(fork, radius);
            SpawnFlash(_view.VoxelToWorld(fork), new Color(0.64f, 0.82f, 1.0f), 6.5f);
        }
    }

    private bool TryFindLightningFork(Vector3I origin, int craterRadius, IReadOnlyList<Vector3I> used, int forkIndex, out Vector3I best)
    {
        int minDistance = Math.Max(3, craterRadius + 2);
        int maxDistance = Math.Min(12, minDistance + 6);
        uint bestScore = uint.MaxValue;
        best = default;
        bool found = false;

        for (int z = -maxDistance; z <= maxDistance; z++)
        for (int y = -maxDistance; y <= maxDistance; y++)
        for (int x = -maxDistance; x <= maxDistance; x++)
        {
            int manhattan = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
            if (manhattan < minDistance || manhattan > maxDistance) continue;
            Vector3I candidate = origin + new Vector3I(x, y, z);
            BlockSample sample = _world.SampleVoxel(candidate);
            if (!sample.Present || !sample.Mineable || !_world.IsExposed(candidate, sample)) continue;

            bool overlaps = false;
            foreach (Vector3I previous in used)
            {
                Vector3I delta = candidate - previous;
                if (delta.LengthSquared() <= minDistance * minDistance) { overlaps = true; break; }
            }
            if (overlaps) continue;

            uint score = EventHash(candidate, unchecked((uint)(forkIndex + 1) * 0x9E3779B9u));
            if (score >= bestScore) continue;
            bestScore = score;
            best = candidate;
            found = true;
        }
        return found;
    }

    private uint EventHash(Vector3I voxel, uint salt)
    {
        unchecked
        {
            uint value = (uint)_world.Profile.Seed ^ salt;
            value ^= (uint)voxel.X * 0x85EBCA6Bu; value = MixEventHash(value);
            value ^= (uint)voxel.Y * 0xC2B2AE35u; value = MixEventHash(value);
            value ^= (uint)voxel.Z * 0x27D4EB2Fu;
            return MixEventHash(value);
        }
    }

    private static uint MixEventHash(uint value)
    {
        unchecked
        {
            value ^= value >> 16; value *= 0x7FEB352Du;
            value ^= value >> 15; value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    private string CloudChargerStatus()
    {
        if (_eventSkills is null) return string.Empty;
        SkillDerivedStats stats = _eventSkills.Derived;
        string charger = stats.AutoCloudChargerUnlocked
            ? $"   |   Cloud AUTO {EffectiveCloudChargeInterval():0.0}s"
            : string.Empty;
        string radioactive = stats.RadioactiveCloudUnlocked
            ? $"   |   Radioactive AUTO {RadioactiveCloudPulseIntervalSeconds:0.0}s"
            : string.Empty;
        string orb = stats.OrbBreakerUnlocked
            ? $"   |   Orb Breaker {EffectiveOrbBreakerInterval():0.0}s"
            : string.Empty;
        string power = stats.LightningRadiusBonus > 0 || stats.LightningChainCount > 0
            ? $"   |   Lightning R{EffectiveLightningRadius()} / forks {stats.LightningChainCount}"
            : string.Empty;
        string meteor = stats.MeteorRadiusBonus > 0 || stats.MeteorSpawnRateMultiplier > 1.001
            ? $"   |   Meteor R{EffectiveMeteorRadius()} / {EffectiveMeteorRespawnDelay():0}s"
            : string.Empty;
        return charger + radioactive + orb + power + meteor;
    }
}
