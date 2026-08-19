using System;
using Godot;
using TenMillionBlocks.Content;
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
    MiningSource Source);

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
    private readonly VirtualWorld _world;
    private readonly ContentDatabase _content;

    public MiningService(VirtualWorld world, ContentDatabase content)
    {
        _world = world;
        _content = content;
    }

    public event Action<MiningResult>? BlockMined;
    public event Action<BulkMiningResult>? BulkMined;
    public event Action<long>? CurrencyChanged;

    public long TotalMined => _world.State.MinedVoxelCount;
    public long Remaining => _world.RemainingMineableBlocks;
    public long Currency { get; private set; }

    public MiningResult TryMine(Vector3I voxel)
        => TryMine(voxel, MiningSource.Manual, requireExposed: true);

    public MiningResult TryMine(Vector3I voxel, MiningSource source, bool requireExposed)
    {
        BlockSample before = _world.SampleVoxel(voxel);
        if (!before.Present || !before.Mineable || (requireExposed && !_world.IsExposed(voxel)))
        {
            return new MiningResult(false, voxel, string.Empty, 0L, TotalMined, Remaining, source);
        }

        if (!_world.TryMine(voxel, requireExposed, out BlockSample mined))
        {
            return new MiningResult(false, voxel, string.Empty, 0L, TotalMined, Remaining, source);
        }

        BlockDefinition definition = _content.GetBlock(mined.BlockId);
        long reward = definition.BaseValue;
        Currency = checked(Currency + reward);

        var result = new MiningResult(true, voxel, mined.BlockId, reward, TotalMined, Remaining, source);
        BlockMined?.Invoke(result);
        CurrencyChanged?.Invoke(Currency);
        return result;
    }

    /// <summary>
    /// Hierarchical mining path for large worlds. One region aggregate replaces potentially millions
    /// of per-block state/events while preserving exact 64-bit mined/remaining accounting.
    /// </summary>
    public BulkMiningResult TryExhaustRegion(RegionCoord region, MiningSource source)
    {
        if (!_world.TryExhaustRegion(region, out long blocksMined))
        {
            return new BulkMiningResult(false, region, 0L, 0L, TotalMined, Remaining, source);
        }

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
}
