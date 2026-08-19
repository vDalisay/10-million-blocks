using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;
using TenMillionBlocks.World.Storage;

namespace TenMillionBlocks.World;

public sealed class VirtualWorld
{
    public VirtualWorld(WorldProfile profile)
    {
        Profile = profile;
        Source = new ProceduralWorldSource(profile);
        State = new WorldStateStore(profile.ChunkSize);
    }

    public WorldProfile Profile { get; }
    public ProceduralWorldSource Source { get; }
    public WorldStateStore State { get; }
    public long InitialMineableBlocks { get; private set; }
    public long RemainingMineableBlocks => Math.Max(0L, InitialMineableBlocks - State.MinedVoxelCount);

    public int MaxCoordinate => Profile.MaxCoordinate;

    public BlockSample SampleVoxel(Vector3I coordinate)
    {
        if (State.IsMined(coordinate))
        {
            return BlockSample.Empty;
        }

        return Source.SampleVoxel(coordinate);
    }

    public bool IsPresent(Vector3I coordinate) => SampleVoxel(coordinate).Present;

    public bool IsExposed(Vector3I coordinate)
    {
        BlockSample sample = SampleVoxel(coordinate);
        if (!sample.Present)
        {
            return false;
        }

        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            if (!SampleVoxel(coordinate + direction).Present)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryMine(Vector3I coordinate, out BlockSample mined)
        => TryMine(coordinate, requireExposed: true, out mined);

    public bool TryMine(Vector3I coordinate, bool requireExposed, out BlockSample mined)
    {
        mined = SampleVoxel(coordinate);
        if (!mined.Present || !mined.Mineable || (requireExposed && !IsExposed(coordinate)))
        {
            mined = BlockSample.Empty;
            return false;
        }

        return State.MarkMined(coordinate);
    }

    public long CountMineableBlocksExact()
    {
        int max = MaxCoordinate;
        if (max > 96)
        {
            throw new InvalidOperationException(
                $"Exact full-volume counting is intentionally disabled for world '{Profile.Id}' with bound {max}. " +
                "Large worlds must use authored/aggregate counters rather than scanning their address space.");
        }

        long count = 0;
        for (int z = -max; z <= max; z++)
        for (int y = -max; y <= max; y++)
        for (int x = -max; x <= max; x++)
        {
            BlockSample sample = Source.SampleVoxel(new Vector3I(x, y, z));
            if (sample.Present && sample.Mineable)
            {
                count++;
            }
        }

        InitialMineableBlocks = count;
        return count;
    }

    public Aabb GetWorldBounds()
    {
        float spacing = Profile.BlockSpacing;
        float min = (-MaxCoordinate - 0.5f) * spacing;
        float size = (MaxCoordinate * 2 + 1) * spacing;
        return new Aabb(new Vector3(min, min, min), new Vector3(size, size, size));
    }
}
