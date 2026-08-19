using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TenMillionBlocks.World.Storage;

public sealed class MinedChunkSnapshot
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public List<int> MinedLocalIndices { get; set; } = new();
}

public sealed class ExhaustedRegionSnapshot
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public long MinedCount { get; set; }
}

public sealed class WorldStateStore
{
    private readonly int _chunkSize;
    private readonly int _regionSizeInChunks;
    private readonly Dictionary<ChunkCoord, HashSet<int>> _minedByChunk = new();
    private readonly Dictionary<RegionCoord, long> _exhaustedRegions = new();

    public WorldStateStore(int chunkSize, int regionSizeInChunks = 8)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (regionSizeInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(regionSizeInChunks));
        _chunkSize = chunkSize;
        _regionSizeInChunks = regionSizeInChunks;
    }

    public int ModifiedChunkCount => _minedByChunk.Count;
    public int ExhaustedRegionCount => _exhaustedRegions.Count;
    public long SparseVoxelOverrideCount => _minedByChunk.Sum(pair => (long)pair.Value.Count);
    public long MinedVoxelCount { get; private set; }

    public bool IsRegionExhausted(RegionCoord region) => _exhaustedRegions.ContainsKey(region);

    public bool IsMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);
        RegionCoord region = RegionCoord.FromChunk(chunk, _regionSizeInChunks);
        if (_exhaustedRegions.ContainsKey(region))
        {
            return true;
        }

        return _minedByChunk.TryGetValue(chunk, out HashSet<int>? mined)
            && mined.Contains(VoxelMath.LocalIndex(voxel, _chunkSize));
    }

    public bool MarkMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);
        RegionCoord region = RegionCoord.FromChunk(chunk, _regionSizeInChunks);
        if (_exhaustedRegions.ContainsKey(region))
        {
            return false;
        }

        if (!_minedByChunk.TryGetValue(chunk, out HashSet<int>? mined))
        {
            mined = new HashSet<int>();
            _minedByChunk.Add(chunk, mined);
        }

        if (!mined.Add(VoxelMath.LocalIndex(voxel, _chunkSize)))
        {
            return false;
        }

        MinedVoxelCount = checked(MinedVoxelCount + 1L);
        return true;
    }

    public long GetSparseMinedCountInRegion(RegionCoord region)
    {
        long count = 0L;
        foreach ((ChunkCoord chunk, HashSet<int> mined) in _minedByChunk)
        {
            if (RegionCoord.FromChunk(chunk, _regionSizeInChunks) == region)
            {
                count = checked(count + mined.Count);
            }
        }
        return count;
    }

    /// <summary>
    /// Replaces all sparse per-voxel deviations inside a region with one aggregate exhausted marker.
    /// regionMineableCount is the exact logical quota assigned by VirtualWorld, not a scanned count.
    /// </summary>
    public long MarkRegionExhausted(RegionCoord region, long regionMineableCount)
    {
        if (regionMineableCount <= 0 || _exhaustedRegions.ContainsKey(region))
        {
            return 0L;
        }

        long alreadyMined = 0L;
        var remove = new List<ChunkCoord>();
        foreach ((ChunkCoord chunk, HashSet<int> mined) in _minedByChunk)
        {
            if (RegionCoord.FromChunk(chunk, _regionSizeInChunks) != region)
            {
                continue;
            }

            alreadyMined = checked(alreadyMined + mined.Count);
            remove.Add(chunk);
        }

        foreach (ChunkCoord chunk in remove)
        {
            _minedByChunk.Remove(chunk);
        }

        long newlyMined = Math.Max(0L, regionMineableCount - alreadyMined);
        _exhaustedRegions[region] = regionMineableCount;
        MinedVoxelCount = checked(MinedVoxelCount + newlyMined);
        return newlyMined;
    }

    public IReadOnlyCollection<int> GetMinedLocalIndices(ChunkCoord chunk)
    {
        if (_minedByChunk.TryGetValue(chunk, out HashSet<int>? mined))
        {
            return mined;
        }

        return Array.Empty<int>();
    }

    public long GetExhaustedRegionMinedCount(RegionCoord region)
        => _exhaustedRegions.GetValueOrDefault(region);

    public List<MinedChunkSnapshot> CreateSnapshot()
        => _minedByChunk
            .OrderBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Y)
            .ThenBy(pair => pair.Key.Z)
            .Select(pair => new MinedChunkSnapshot
            {
                X = pair.Key.X,
                Y = pair.Key.Y,
                Z = pair.Key.Z,
                MinedLocalIndices = pair.Value.OrderBy(index => index).ToList(),
            })
            .ToList();

    public List<ExhaustedRegionSnapshot> CreateExhaustedRegionSnapshot()
        => _exhaustedRegions
            .OrderBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Y)
            .ThenBy(pair => pair.Key.Z)
            .Select(pair => new ExhaustedRegionSnapshot
            {
                X = pair.Key.X,
                Y = pair.Key.Y,
                Z = pair.Key.Z,
                MinedCount = pair.Value,
            })
            .ToList();

    public void RestoreSnapshot(IEnumerable<MinedChunkSnapshot> chunks)
        => RestoreSnapshot(chunks, Array.Empty<ExhaustedRegionSnapshot>());

    public void RestoreSnapshot(
        IEnumerable<MinedChunkSnapshot> chunks,
        IEnumerable<ExhaustedRegionSnapshot> exhaustedRegions)
    {
        _minedByChunk.Clear();
        _exhaustedRegions.Clear();
        MinedVoxelCount = 0L;

        foreach (ExhaustedRegionSnapshot snapshot in exhaustedRegions)
        {
            if (snapshot.MinedCount <= 0)
            {
                continue;
            }

            var region = new RegionCoord(snapshot.X, snapshot.Y, snapshot.Z);
            if (_exhaustedRegions.TryAdd(region, snapshot.MinedCount))
            {
                MinedVoxelCount = checked(MinedVoxelCount + snapshot.MinedCount);
            }
        }

        int maxLocalIndex = checked(_chunkSize * _chunkSize * _chunkSize);
        foreach (MinedChunkSnapshot snapshot in chunks)
        {
            var key = new ChunkCoord(snapshot.X, snapshot.Y, snapshot.Z);
            RegionCoord region = RegionCoord.FromChunk(key, _regionSizeInChunks);
            if (_exhaustedRegions.ContainsKey(region))
            {
                continue;
            }

            var indices = new HashSet<int>();
            foreach (int index in snapshot.MinedLocalIndices)
            {
                if (index < 0 || index >= maxLocalIndex)
                {
                    continue;
                }
                indices.Add(index);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            _minedByChunk[key] = indices;
            MinedVoxelCount = checked(MinedVoxelCount + indices.Count);
        }
    }
}
