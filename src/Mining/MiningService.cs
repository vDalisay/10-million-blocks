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

    private readonly VirtualWorld _world;
    private readonly ContentDatabase _content;
    private readonly Dictionary<Vector3I, int> _bombHits = new();
    private int _currencyNotificationBatchDepth;
    private bool _currencyNotificationPending;

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

        BlockDefinition definition = _content.GetBlock(mined.BlockId);
        long reward = definition.BaseValue;
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
            BlockDefinition definition = _content.GetBlock(mined.BlockId);
            long reward = definition.BaseValue;
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
            BlockDefinition definition = _content.GetBlock(mined.BlockId);
            long reward = definition.BaseValue;
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

        // Region aggregation is only a giant-world optimization. Demo worlds that contain authored
        // special resources stay on exact voxel mining paths, because an aggregate region does not
        // retain enough identity information to award a gem exactly once.
        long reward = checked(blocksMined * _world.Profile.AggregateRewardPerBlock);
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
