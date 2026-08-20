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

public sealed class MiningService
{
    private const int BombHitsRequired = 3;
    private const int BombBlastRadius = 2;

    private readonly VirtualWorld _world;
    private readonly ContentDatabase _content;
    private readonly Dictionary<Vector3I, int> _bombHits = new();

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

    public MiningResult TryMine(Vector3I voxel)
        => TryMine(voxel, MiningSource.Manual, requireExposed: true);

    public MiningResult TryMine(Vector3I voxel, MiningSource source, bool requireExposed)
    {
        BlockSample before = _world.SampleVoxel(voxel);
        if (!before.Present || !before.Mineable || (requireExposed && !_world.IsExposed(voxel)))
        {
            return Failure(voxel, source);
        }

        if (before.BlockId == "bomb")
        {
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

        if (!_world.TryMine(voxel, requireExposed, out BlockSample mined))
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
        CurrencyChanged?.Invoke(Currency);
        return result;
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

        CurrencyChanged?.Invoke(Currency);
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
        CurrencyChanged?.Invoke(Currency);
        return result;
    }

    public bool TrySpend(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Currency < amount) return false;

        Currency -= amount;
        CurrencyChanged?.Invoke(Currency);
        return true;
    }

    public void GrantCurrency(long amount)
    {
        if (amount <= 0) return;
        Currency = checked(Currency + amount);
        CurrencyChanged?.Invoke(Currency);
    }

    public void RestoreCurrency(long amount)
    {
        Currency = Math.Max(0L, amount);
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
