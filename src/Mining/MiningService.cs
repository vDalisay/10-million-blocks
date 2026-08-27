using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Economy;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Mining;

public enum MiningSource
{
    Manual,
    Laser,
    Automated,
    Offline,
    WorldEvent,
    Debug,
}

public readonly record struct MiningResult(
    bool Success,
    Vector3I Voxel,
    string BlockId,
    long Reward,
    long TotalMined,
    long Remaining,
    MiningSource Source,
    long BlocksRemoved = 1L,
    bool Removed = true,
    int EffectRadius = 0,
    int DamageStage = 0,
    int DamageRequired = 0);

public readonly record struct BulkMiningResult(
    bool Success,
    RegionCoord Region,
    long BlocksMined,
    long Reward,
    long TotalMined,
    long Remaining,
    MiningSource Source);

public readonly record struct AreaMiningResult(
    bool Success,
    Vector3I Center,
    int Radius,
    long BlocksMined,
    long Reward,
    long TotalMined,
    long Remaining,
    MiningSource Source,
    IReadOnlyList<Vector3I> RemovedVoxels);

public sealed class MiningService
{
    private const int BombHitsRequired = 3;
    private const int BombBlastRadius = 2;
    private const int DamageDisplayScale = 100;
    private const uint RewardRoundSalt = 0x6D2B79F5u;
    private const uint CriticalSalt = 0xA511E9B3u;

    private readonly VirtualWorld _world;
    private readonly ContentDatabase _content;
    private readonly Dictionary<Vector3I, int> _bombHits = new();
    private readonly Dictionary<Vector3I, double> _hardnessDamage = new();
    private int _currencyNotificationBatchDepth;
    private bool _currencyNotificationPending;

    // SkillTreeService pushes these derived values after every restore/purchase. Keeping payout policy
    // in MiningService means manual, automation, explosions, meteors and offline mining all use one
    // authoritative economy instead of each feature remembering to apply an upgrade itself.
    private double _resourceYieldMultiplier = 1.0;
    private double _preciousResourceYieldMultiplier = 1.0;
    private double _criticalYieldChance;
    private double _criticalYieldMultiplier = 2.0;

    public MiningService(VirtualWorld world, ContentDatabase content)
        : this(world, content, new SpecialResourceInventory())
    {
    }

    public MiningService(
        VirtualWorld world,
        ContentDatabase content,
        SpecialResourceInventory specialResources)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        SpecialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));
    }

    public event Action<MiningResult>? BlockMined;
    public event Action<MiningResult>? BlockDamaged;
    public event Action<BulkMiningResult>? BulkMined;
    public event Action<long>? CurrencyChanged;

    public long TotalMined => _world.State.MinedVoxelCount;
    public long Remaining => _world.RemainingMineableBlocks;
    public long Currency { get; private set; }
    public SpecialResourceInventory SpecialResources { get; }

    public BlockDefinition GetBlockDefinition(string blockId) => _content.GetBlock(blockId);

    internal void ConfigureProgressionEconomy(
        double resourceYieldMultiplier,
        double preciousResourceYieldMultiplier,
        double criticalYieldChance,
        double criticalYieldMultiplier)
    {
        _resourceYieldMultiplier = Math.Max(0.01, resourceYieldMultiplier);
        _preciousResourceYieldMultiplier = Math.Max(0.01, preciousResourceYieldMultiplier);
        _criticalYieldChance = Math.Clamp(criticalYieldChance, 0.0, 0.75);
        _criticalYieldMultiplier = Math.Max(1.0, criticalYieldMultiplier);
    }

    /// <summary>
    /// Defers CurrencyChanged fan-out while a caller performs a bounded group of authoritative mining
    /// operations. Currency itself still changes immediately and BlockMined remains per-block; only the
    /// redundant observer notification is coalesced. Nested batches are supported so manual footprints,
    /// wide drills and the frame scheduler can compose safely.
    /// </summary>
    internal void BeginCurrencyNotificationBatch()
    {
        _currencyNotificationBatchDepth++;
    }

    internal void EndCurrencyNotificationBatch()
    {
        if (_currencyNotificationBatchDepth <= 0)
        {
            throw new InvalidOperationException("Currency notification batch ended without a matching begin.");
        }

        _currencyNotificationBatchDepth--;
        if (_currencyNotificationBatchDepth == 0 && _currencyNotificationPending)
        {
            _currencyNotificationPending = false;
            CurrencyChanged?.Invoke(Currency);
        }
    }

    public MiningResult TryMine(Vector3I voxel)
        => TryMine(voxel, MiningSource.Manual, requireExposed: true);

    /// <summary>
    /// Player-manual mining uses authored block hardness as health. This gives the skill tree a real
    /// damage axis: early dirt still disappears immediately, while stone/ore/gems require repeated
    /// actions until the corresponding Breaker Power upgrade catches up. Automation retains its own
    /// material/rate rules and therefore does not inherit this manual damage gate.
    /// </summary>
    public MiningResult TryMineManual(Vector3I voxel, double damage)
        => TryMineWithHardness(voxel, damage, MiningSource.Manual, preserveManualBombClicks: true);

    /// <summary>
    /// Flux Laser damage shares the same authored hardness state as manual mining, but has its own
    /// source identity. It must not masquerade as a physical click for tutorials/telemetry, and unstable
    /// blocks accumulate continuous beam damage instead of treating every 10 Hz damage tick as a click.
    /// </summary>
    public MiningResult TryMineLaser(Vector3I voxel, double damage)
        => TryMineWithHardness(voxel, damage, MiningSource.Laser, preserveManualBombClicks: false);

    private MiningResult TryMineWithHardness(
        Vector3I voxel,
        double damage,
        MiningSource source,
        bool preserveManualBombClicks)
    {
        BlockSample before = _world.SampleVoxel(voxel);
        if (!before.Present || !before.Mineable)
        {
            _hardnessDamage.Remove(voxel);
            return Failure(voxel, source);
        }

        // Physical clicks deliberately retain the authored three-hit unstable-block anticipation. The
        // laser instead uses the normal hardness accumulator and detonates only after enough beam damage.
        if (preserveManualBombClicks && before.BlockId == "bomb")
        {
            return TryMine(voxel, before, source, requireExposed: true);
        }

        if (!_world.IsExposed(voxel, before))
        {
            return Failure(voxel, source);
        }

        BlockDefinition definition = _content.GetBlock(before.BlockId);
        double hardness = Math.Max(0.01, definition.Hardness);
        double applied = Math.Max(0.01, damage);
        double accumulated = _hardnessDamage.GetValueOrDefault(voxel) + applied;
        if (accumulated + 1e-9 < hardness)
        {
            _hardnessDamage[voxel] = accumulated;
            var damaged = new MiningResult(
                true,
                voxel,
                before.BlockId,
                0L,
                TotalMined,
                Remaining,
                source,
                BlocksRemoved: 0L,
                Removed: false,
                DamageStage: Math.Clamp((int)Math.Ceiling(accumulated * DamageDisplayScale), 1, int.MaxValue),
                DamageRequired: Math.Clamp((int)Math.Ceiling(hardness * DamageDisplayScale), 1, int.MaxValue));
            BlockDamaged?.Invoke(damaged);
            return damaged;
        }

        _hardnessDamage.Remove(voxel);
        return TryMine(voxel, before, source, requireExposed: true);
    }

    public MiningResult TryMine(Vector3I voxel, MiningSource source, bool requireExposed)
        => TryMine(voxel, _world.SampleVoxel(voxel), source, requireExposed);

    /// <summary>
    /// Hot-path overload for callers such as automation that have already sampled a candidate to
    /// inspect material/tags. The world still owns the authoritative mutation and exposure check.
    /// </summary>
    internal MiningResult TryMine(
        Vector3I voxel,
        BlockSample before,
        MiningSource source,
        bool requireExposed)
    {
        if (!before.Present || !before.Mineable)
        {
            return Failure(voxel, source);
        }

        if (before.BlockId == "bomb")
        {
            // Bombs don't pass through the ordinary TryMine mutation until they detonate, so preserve
            // the exposure gate here. Ordinary blocks let VirtualWorld perform this check exactly once.
            if (requireExposed && !_world.IsExposed(voxel, before))
            {
                return Failure(voxel, source);
            }

            // Manual mining gets the requested multi-hit anticipation. Automation detonates an
            // unstable block on contact rather than stepping past a half-damaged bomb.
            if (source == MiningSource.Manual)
            {
                int hits = _bombHits.GetValueOrDefault(voxel) + 1;
                if (hits < BombHitsRequired)
                {
                    _bombHits[voxel] = hits;
                    var damaged = new MiningResult(
                        true,
                        voxel,
                        before.BlockId,
                        0L,
                        TotalMined,
                        Remaining,
                        source,
                        BlocksRemoved: 0L,
                        Removed: false,
                        DamageStage: hits,
                        DamageRequired: BombHitsRequired);
                    BlockDamaged?.Invoke(damaged);
                    return damaged;
                }
            }

            return Detonate(voxel, source);
        }

        // We already sampled this voxel above to inspect mineability/special behavior. Reuse that
        // authoritative sample and let VirtualWorld perform the exposure test exactly once against the
        // six neighbours before mutation.
        if (!_world.TryMine(voxel, before, requireExposed, out BlockSample mined))
        {
            return Failure(voxel, source);
        }

        _hardnessDamage.Remove(voxel);
        BlockDefinition definition = _content.GetBlock(mined.BlockId);
        long reward = CalculateReward(definition, voxel);
        Currency = checked(Currency + reward);
        CreditSpecialResource(definition, mined.BlockId);

        var result = new MiningResult(
            true,
            voxel,
            mined.BlockId,
            reward,
            TotalMined,
            Remaining,
            source,
            BlocksRemoved: 1L,
            Removed: true);
        BlockMined?.Invoke(result);
        NotifyCurrencyChanged();
        return result;
    }

    /// <summary>
    /// Authoritative bounded area removal used by lightning, meteors and future world events. It does
    /// not shortcut through aggregate region accounting: every accepted voxel is removed through
    /// VirtualWorld, credited once, and emitted as BlockMined so saves/replays/statistics observe the
    /// exact same mutation stream as manual and automation mining.
    /// </summary>
    public AreaMiningResult TryMineCrater(
        Vector3I center,
        int radius,
        MiningSource source = MiningSource.WorldEvent)
    {
        if (radius < 0 || radius > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "World-event crater radius must be between 0 and 12.");
        }

        var removedVoxels = new List<Vector3I>();
        long totalReward = 0L;
        int radiusSquared = radius * radius;

        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y + z * z > radiusSquared) continue;

            Vector3I candidate = center + new Vector3I(x, y, z);
            if (!_world.TryMine(candidate, requireExposed: false, out BlockSample mined)) continue;

            _bombHits.Remove(candidate);
            _hardnessDamage.Remove(candidate);
            BlockDefinition definition = _content.GetBlock(mined.BlockId);
            long reward = CalculateReward(definition, candidate);
            Currency = checked(Currency + reward);
            totalReward = checked(totalReward + reward);
            CreditSpecialResource(definition, mined.BlockId);
            removedVoxels.Add(candidate);

            BlockMined?.Invoke(new MiningResult(
                true,
                candidate,
                mined.BlockId,
                reward,
                TotalMined,
                Remaining,
                source,
                BlocksRemoved: 1L,
                Removed: true,
                EffectRadius: radius));
        }

        if (removedVoxels.Count > 0)
        {
            NotifyCurrencyChanged();
        }

        return new AreaMiningResult(
            removedVoxels.Count > 0,
            center,
            radius,
            removedVoxels.Count,
            totalReward,
            TotalMined,
            Remaining,
            source,
            removedVoxels);
    }

    private MiningResult Detonate(Vector3I center, MiningSource source)
    {
        _bombHits.Remove(center);
        _hardnessDamage.Remove(center);
        long totalReward = 0L;
        long removed = 0L;
        int radiusSquared = BombBlastRadius * BombBlastRadius;

        // This is deliberately bounded (radius 2). We still mine through VirtualWorld so authored
        // counters, region quotas, sparse state, special-resource credit and completion all remain
        // authoritative. Every successfully removed voxel gets exactly one accounting pass here.
        for (int z = -BombBlastRadius; z <= BombBlastRadius; z++)
        for (int y = -BombBlastRadius; y <= BombBlastRadius; y++)
        for (int x = -BombBlastRadius; x <= BombBlastRadius; x++)
        {
            if (x * x + y * y + z * z > radiusSquared) continue;

            Vector3I candidate = center + new Vector3I(x, y, z);
            if (!_world.TryMine(candidate, requireExposed: false, out BlockSample mined)) continue;

            _bombHits.Remove(candidate);
            _hardnessDamage.Remove(candidate);
            BlockDefinition definition = _content.GetBlock(mined.BlockId);
            long reward = CalculateReward(definition, candidate);
            totalReward = checked(totalReward + reward);
            Currency = checked(Currency + reward);
            CreditSpecialResource(definition, mined.BlockId);
            removed++;

            BlockMined?.Invoke(new MiningResult(
                true,
                candidate,
                mined.BlockId,
                reward,
                TotalMined,
                Remaining,
                source,
                BlocksRemoved: 1L,
                Removed: true));
        }

        if (removed <= 0)
        {
            return Failure(center, source);
        }

        NotifyCurrencyChanged();
        return new MiningResult(
            true,
            center,
            "bomb",
            totalReward,
            TotalMined,
            Remaining,
            source,
            BlocksRemoved: removed,
            Removed: true,
            EffectRadius: BombBlastRadius,
            DamageStage: BombHitsRequired,
            DamageRequired: BombHitsRequired);
    }

    public BulkMiningResult TryExhaustRegion(RegionCoord region, MiningSource source)
    {
        if (!_world.TryExhaustRegion(region, out long blocksMined))
        {
            return new BulkMiningResult(false, region, 0L, 0L, TotalMined, Remaining, source);
        }

        // Giant-world aggregate regions do not retain per-block material identity. Apply the global
        // expected payout/critical value, but not the precious-material bonus, which requires an exact
        // authored block id. Demo worlds with gems stay on exact voxel paths.
        double expectedCritical = 1.0 + _criticalYieldChance * (_criticalYieldMultiplier - 1.0);
        double scaledPerBlock = Math.Max(0.0, _world.Profile.AggregateRewardPerBlock * _resourceYieldMultiplier * expectedCritical);
        long reward = checked((long)Math.Round(blocksMined * scaledPerBlock, MidpointRounding.AwayFromZero));
        Currency = checked(Currency + reward);
        var result = new BulkMiningResult(true, region, blocksMined, reward, TotalMined, Remaining, source);
        BulkMined?.Invoke(result);
        NotifyCurrencyChanged();
        return result;
    }

    public bool TrySpend(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Currency < amount) return false;

        Currency -= amount;
        NotifyCurrencyChanged();
        return true;
    }

    public void GrantCurrency(long amount)
    {
        if (amount <= 0) return;
        Currency = checked(Currency + amount);
        NotifyCurrencyChanged();
    }

    public void RestoreCurrency(long amount)
    {
        Currency = Math.Max(0L, amount);
        NotifyCurrencyChanged();
    }

    private long CalculateReward(BlockDefinition definition, Vector3I voxel)
    {
        if (definition.BaseValue <= 0) return 0L;

        double multiplier = _resourceYieldMultiplier;
        if (definition.Tags.Contains("gold") || definition.Tags.Contains("gem"))
        {
            multiplier *= _preciousResourceYieldMultiplier;
        }

        double scaled = Math.Max(0.0, definition.BaseValue * multiplier);
        long whole = checked((long)Math.Floor(scaled));
        double fraction = scaled - whole;
        if (fraction > 0.0 && UnitHash(voxel, RewardRoundSalt) < fraction)
        {
            whole = checked(whole + 1L);
        }

        if (whole > 0L && _criticalYieldChance > 0.0 && UnitHash(voxel, CriticalSalt) < _criticalYieldChance)
        {
            whole = checked((long)Math.Max(1.0, Math.Round(whole * _criticalYieldMultiplier, MidpointRounding.AwayFromZero)));
        }
        return whole;
    }

    private double UnitHash(Vector3I voxel, uint salt)
    {
        unchecked
        {
            uint hash = (uint)_world.Profile.Seed ^ salt;
            hash ^= (uint)voxel.X * 0x9E3779B1u;
            hash = Mix(hash);
            hash ^= (uint)voxel.Y * 0x85EBCA77u;
            hash = Mix(hash);
            hash ^= (uint)voxel.Z * 0xC2B2AE3Du;
            hash = Mix(hash);
            return (hash & 0x00FFFFFFu) / 16777216.0;
        }
    }

    private static uint Mix(uint value)
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

    private void NotifyCurrencyChanged()
    {
        if (_currencyNotificationBatchDepth > 0)
        {
            _currencyNotificationPending = true;
            return;
        }
        CurrencyChanged?.Invoke(Currency);
    }

    private void CreditSpecialResource(BlockDefinition definition, string blockId)
    {
        if (!definition.Tags.Contains("gem")) return;
        SpecialResources.Grant(blockId, 1L);
    }

    private MiningResult Failure(Vector3I voxel, MiningSource source)
        => new(
            false,
            voxel,
            string.Empty,
            0L,
            TotalMined,
            Remaining,
            source,
            BlocksRemoved: 0L,
            Removed: false);
}
