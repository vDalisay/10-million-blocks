using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Mining;

public readonly record struct MiningResult(
    bool Success,
    Vector3I Voxel,
    string BlockId,
    long Reward,
    long TotalMined,
    long Remaining);

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

    public long TotalMined => _world.State.MinedVoxelCount;
    public long Remaining => _world.RemainingMineableBlocks;
    public long Currency { get; private set; }
    public int ManualBlocksPerClick { get; private set; } = 1;

    public MiningResult TryMine(Vector3I voxel)
    {
        BlockSample before = _world.SampleVoxel(voxel);
        if (!before.Present || !before.Mineable || !_world.IsExposed(voxel))
        {
            return new MiningResult(false, voxel, string.Empty, 0L, TotalMined, Remaining);
        }

        if (!_world.TryMine(voxel, out BlockSample mined))
        {
            return new MiningResult(false, voxel, string.Empty, 0L, TotalMined, Remaining);
        }

        BlockDefinition definition = _content.GetBlock(mined.BlockId);
        long reward = definition.BaseValue;
        Currency += reward;

        var result = new MiningResult(true, voxel, mined.BlockId, reward, TotalMined, Remaining);
        BlockMined?.Invoke(result);
        return result;
    }
}
