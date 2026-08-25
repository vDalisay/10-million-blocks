using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.WorldEvents;

public partial class WorldEventController
{
    private const double AutomaticCloudChargeIntervalSeconds = 3.0;
    private const double RadioactiveCloudPulseIntervalSeconds = 6.0;
    private const int RadioactiveCloudRadius = 1;

    private SkillTreeService? _eventSkills;
    private Timer? _cloudChargerTimer;
    private Timer? _radioactiveCloudTimer;

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
        if (!TrySurfaceUnder(_cloud.GlobalPosition, out Vector3I target)) return;

        ApplyCrater(target, RadioactiveCloudRadius);
        SpawnFlash(_view.VoxelToWorld(target), new Color(0.42f, 1.0f, 0.48f), 2.4f);
        RequestPersistence();
    }

    private double EffectiveCloudChargeInterval()
        => AutomaticCloudChargeIntervalSeconds / Math.Max(0.1, _eventSkills?.Derived.CloudChargeRateMultiplier ?? 1.0);

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
        string power = stats.LightningRadiusBonus > 0 || stats.LightningChainCount > 0
            ? $"   |   Lightning R{EffectiveLightningRadius()} / forks {stats.LightningChainCount}"
            : string.Empty;
        string meteor = stats.MeteorRadiusBonus > 0 || stats.MeteorSpawnRateMultiplier > 1.001
            ? $"   |   Meteor R{EffectiveMeteorRadius()} / {EffectiveMeteorRespawnDelay():0}s"
            : string.Empty;
        return charger + radioactive + power + meteor;
    }
}
