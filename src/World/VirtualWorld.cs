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

        BlockSample sample = Source.SampleVoxel(coordinate);
        return ReclassifyDeepSpecialBlock(coordinate, sample);
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

        if (regionQuota > 0 && beforeInRegion + 1L >= regionQuota)
        {
            State.MarkRegionExhausted(region, regionQuota);
        }

        return true;
    }

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
            BlockSample sample = ReclassifyDeepSpecialBlock(new Vector3I(x, y, z), Source.SampleVoxel(new Vector3I(x, y, z)));
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

    /// <summary>
    /// Adds rare late-game content without storing it. Untouched gem pockets and unstable blocks are
    /// pure functions of world seed + voxel address, so save files remain sparse and deterministic.
    /// The broad field creates pockets; the high-frequency hash prevents every block in a pocket from
    /// becoming special. Surface rendering stays cheap because these rules only affect deep rock.
    /// </summary>
    private BlockSample ReclassifyDeepSpecialBlock(Vector3I coordinate, BlockSample sample)
    {
        if (!sample.Present || !sample.Mineable || !IsRockFamily(sample.BlockId))
        {
            return sample;
        }

        float maxAbs = Math.Max(Math.Abs(coordinate.X), Math.Max(Math.Abs(coordinate.Y), Math.Abs(coordinate.Z)));
        float approximateDepth = Profile.BaseRadius - maxAbs;
        if (approximateDepth < 4.0f)
        {
            return sample;
        }

        float pocket = DeterministicNoise.Fractal3D(
            coordinate.X * 0.075f,
            coordinate.Y * 0.075f,
            coordinate.Z * 0.075f,
            Profile.Seed + 51031,
            3);
        float grain = DeterministicNoise.Hash01(
            coordinate.X,
            coordinate.Y,
            coordinate.Z,
            Profile.Seed + 51047);

        // Unstable blocks are intentionally rare. They are promoted to multi-hit blast events by
        // MiningService; this sampler only owns deterministic placement.
        float bombRoll = DeterministicNoise.Hash01(
            coordinate.X,
            coordinate.Y,
            coordinate.Z,
            Profile.Seed + 77191);
        if (approximateDepth > 9.0f && bombRoll > 0.99955f)
        {
            return new BlockSample(true, "bomb", true);
        }

        if (approximateDepth > 10.0f && pocket > 0.56f && grain > 0.78f)
        {
            return new BlockSample(true, "gem_red", true);
        }

        if (approximateDepth > 7.0f && pocket > 0.43f && grain > 0.70f)
        {
            return new BlockSample(true, "gem_blue", true);
        }

        if (pocket > 0.30f && grain > 0.64f)
        {
            return new BlockSample(true, "gem_green", true);
        }

        return sample;
    }

    private bool IsRockFamily(string blockId)
        => blockId == Profile.StoneBlock
            || blockId == Profile.DarkStoneBlock
            || blockId == Profile.CopperBlock
            || blockId == Profile.SilverBlock
            || blockId == Profile.GoldBlock;
}
