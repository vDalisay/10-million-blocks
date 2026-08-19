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
        State = new WorldStateStore(profile.ChunkSize, profile.RegionSizeInChunks);
    }

    public WorldProfile Profile { get; }
    public ProceduralWorldSource Source { get; }
    public WorldStateStore State { get; }
    public long InitialMineableBlocks { get; private set; }
    public long RemainingMineableBlocks => Math.Max(0L, InitialMineableBlocks - State.MinedVoxelCount);

    public int MaxCoordinate => Profile.MaxCoordinate;
    public int MinChunkCoordinate => VoxelMath.FloorDiv(-MaxCoordinate, Profile.ChunkSize);
    public int MaxChunkCoordinate => VoxelMath.FloorDiv(MaxCoordinate, Profile.ChunkSize);
    public int MinRegionCoordinate => VoxelMath.FloorDiv(MinChunkCoordinate, Profile.RegionSizeInChunks);
    public int MaxRegionCoordinate => VoxelMath.FloorDiv(MaxChunkCoordinate, Profile.RegionSizeInChunks);
    public long RegionAxisCount => (long)MaxRegionCoordinate - MinRegionCoordinate + 1L;
    public long TotalLogicalRegionCount => checked(checked(RegionAxisCount * RegionAxisCount) * RegionAxisCount);

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

        RegionCoord region = RegionForVoxel(coordinate);
        long regionQuota = Profile.TargetMineableBlocks > 0 ? GetRegionQuota(region) : 0L;
        long beforeInRegion = 0L;
        if (regionQuota > 0)
        {
            beforeInRegion = State.GetSparseMinedCountInRegion(region);
            if (beforeInRegion >= regionQuota)
            {
                State.MarkRegionExhausted(region, regionQuota);
                mined = BlockSample.Empty;
                return false;
            }
        }

        if (!State.MarkMined(coordinate))
        {
            mined = BlockSample.Empty;
            return false;
        }

        // Once a large-world region reaches its authored quota, compact it immediately to a single
        // aggregate marker. The global count does not change during compaction.
        if (regionQuota > 0 && beforeInRegion + 1L >= regionQuota)
        {
            State.MarkRegionExhausted(region, regionQuota);
        }

        return true;
    }

    /// <summary>
    /// Initializes the authoritative total without forcing large profiles to enumerate their volume.
    /// Small worlds remain exact scans; large profiles use the authored logical target.
    /// </summary>
    public long InitializeMineableBlockCount()
    {
        if (Profile.TargetMineableBlocks > 0)
        {
            InitialMineableBlocks = Profile.TargetMineableBlocks;
            return InitialMineableBlocks;
        }

        return CountMineableBlocksExact();
    }

    public long CountMineableBlocksExact()
    {
        int max = MaxCoordinate;
        if (max > Profile.StreamingThresholdMaxCoordinate)
        {
            throw new InvalidOperationException(
                $"Exact full-volume counting is intentionally disabled for world '{Profile.Id}' with bound {max}. " +
                "Large worlds must use target_mineable_blocks and region aggregates rather than scanning their address space.");
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

    public RegionCoord RegionForVoxel(Vector3I voxel)
        => RegionCoord.FromChunk(ChunkCoord.FromVoxel(voxel, Profile.ChunkSize), Profile.RegionSizeInChunks);

    public bool IsRegionInBounds(RegionCoord region)
        => region.X >= MinRegionCoordinate && region.X <= MaxRegionCoordinate
            && region.Y >= MinRegionCoordinate && region.Y <= MaxRegionCoordinate
            && region.Z >= MinRegionCoordinate && region.Z <= MaxRegionCoordinate;

    /// <summary>
    /// Deterministically partitions the authored logical total over the region address space. The
    /// quotient/remainder partition sums exactly to InitialMineableBlocks without allocating an entry
    /// for every region, even for the million-scale validation profile.
    /// </summary>
    public long GetRegionQuota(RegionCoord region)
    {
        if (!IsRegionInBounds(region) || InitialMineableBlocks <= 0)
        {
            return 0L;
        }

        long axis = RegionAxisCount;
        long x = (long)region.X - MinRegionCoordinate;
        long y = (long)region.Y - MinRegionCoordinate;
        long z = (long)region.Z - MinRegionCoordinate;
        long index = checked(checked(x * axis + y) * axis + z);
        long regionCount = TotalLogicalRegionCount;
        long quotient = InitialMineableBlocks / regionCount;
        long remainder = InitialMineableBlocks % regionCount;
        return quotient + (index < remainder ? 1L : 0L);
    }

    public bool TryExhaustRegion(RegionCoord region, out long newlyMined)
    {
        newlyMined = 0L;
        long quota = GetRegionQuota(region);
        if (quota <= 0 || State.IsRegionExhausted(region))
        {
            return false;
        }

        newlyMined = State.MarkRegionExhausted(region, quota);
        return newlyMined > 0;
    }

    public Aabb GetRegionVoxelBounds(RegionCoord region)
    {
        int regionVoxelSize = checked(Profile.ChunkSize * Profile.RegionSizeInChunks);
        Vector3I minVoxel = new(
            region.X * regionVoxelSize,
            region.Y * regionVoxelSize,
            region.Z * regionVoxelSize);
        return new Aabb((Vector3)minVoxel, Vector3.One * regionVoxelSize);
    }

    public Aabb GetWorldBounds()
    {
        float spacing = Profile.BlockSpacing;
        float min = (-MaxCoordinate - 0.5f) * spacing;
        float size = (MaxCoordinate * 2 + 1) * spacing;
        return new Aabb(new Vector3(min, min, min), new Vector3(size, size, size));
    }
}
