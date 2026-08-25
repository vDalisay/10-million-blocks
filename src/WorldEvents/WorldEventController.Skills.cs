using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;

namespace TenMillionBlocks.WorldEvents;

public partial class WorldEventController
{
    private const double AutomaticCloudChargeIntervalSeconds = 3.0;

    private SkillTreeService? _eventSkills;
    private Timer? _cloudChargerTimer;

    /// <summary>
    /// Connects player-bound event upgrades without making WorldEventController own progression state.
    /// The charger only contributes presentation/gameplay input to the existing authoritative cloud
    /// mechanic; ChargeCloud still decides when a strike fires and MiningService still owns removals.
    /// </summary>
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
        bool enabled = _cloudEnabled
            && _eventSkills is not null
            && _eventSkills.Derived.AutoCloudChargerUnlocked;

        if (!enabled)
        {
            _cloudChargerTimer?.Stop();
            RefreshStatus();
            return;
        }

        if (_cloudChargerTimer is null)
        {
            _cloudChargerTimer = new Timer
            {
                Name = "AutomaticCloudCharger",
                WaitTime = AutomaticCloudChargeIntervalSeconds,
                OneShot = false,
                Autostart = false,
            };
            _cloudChargerTimer.Timeout += OnAutomaticCloudCharge;
            AddChild(_cloudChargerTimer);
        }

        if (_cloudChargerTimer.IsStopped()) _cloudChargerTimer.Start();
        RefreshStatus();
    }

    private void OnAutomaticCloudCharge()
    {
        if (_cloud is null
            || _eventSkills is null
            || !_eventSkills.Derived.AutoCloudChargerUnlocked
            || !_cloudEnabled)
        {
            return;
        }

        ChargeCloud();
        RequestPersistence();
    }

    private int EffectiveLightningRadius()
        => Math.Clamp(LightningRadius + (_eventSkills?.Derived.LightningRadiusBonus ?? 0), 1, 10);

    private int EffectiveMeteorRadius()
        => Math.Clamp(MeteorRadius + (_eventSkills?.Derived.MeteorRadiusBonus ?? 0), 1, 12);

    /// <summary>
    /// Electricity upgrades are intentionally literal incremental-game payoffs: the first strike keeps
    /// the existing crater mechanic, then purchased forks jump to nearby exposed terrain and repeat it.
    /// Target selection is deterministic and bounded so saves/replays do not depend on random state.
    /// </summary>
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

    private bool TryFindLightningFork(
        Vector3I origin,
        int craterRadius,
        IReadOnlyList<Vector3I> used,
        int forkIndex,
        out Vector3I best)
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
                if (delta.LengthSquared() <= minDistance * minDistance)
                {
                    overlaps = true;
                    break;
                }
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
            value ^= (uint)voxel.X * 0x85EBCA6Bu;
            value = MixEventHash(value);
            value ^= (uint)voxel.Y * 0xC2B2AE35u;
            value = MixEventHash(value);
            value ^= (uint)voxel.Z * 0x27D4EB2Fu;
            return MixEventHash(value);
        }
    }

    private static uint MixEventHash(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    private string CloudChargerStatus()
    {
        if (_eventSkills is null) return string.Empty;
        SkillDerivedStats stats = _eventSkills.Derived;
        string charger = stats.AutoCloudChargerUnlocked ? "   |   Cloud Charger: AUTO" : string.Empty;
        string power = stats.LightningRadiusBonus > 0 || stats.LightningChainCount > 0
            ? $"   |   Lightning R{EffectiveLightningRadius()} / forks {stats.LightningChainCount}"
            : string.Empty;
        string meteor = stats.MeteorRadiusBonus > 0
            ? $"   |   Meteor R{EffectiveMeteorRadius()}"
            : string.Empty;
        return charger + power + meteor;
    }
}
