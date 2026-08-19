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
    private readonly Dictionary<RegionCoord, long> _sparseMinedCountByRegion = new();
    private readonly Dictionary<RegionCoord, HashSet<ChunkCoord>> _modifiedChunksByRegion = new();
    private readonly Dictionary<RegionCoord, long> _exhaustedRegions = new();

    public WorldStateStore(int chunkSize, int regionSizeInChunks = 8)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (regionSizeInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(regionSizeInChunks));
        _chunkSize = chunkSize;
        _regionSizeInChunks = regionSizeInChunks;
    }

    public int ModifiedChunkCount => _minedByChunk.Count;
    public int SparseModifiedRegionCount => _sparseMinedCountByRegion.Count;
    public int ExhaustedRegionCount => _exhaustedRegions.Count;
    public long SparseVoxelOverrideCount => _sparseMinedCountByRegion.Values.Sum();
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
            if (!_modifiedChunksByRegion.TryGetValue(region, out HashSet<ChunkCoord>? regionChunks))
            {
                regionChunks = new HashSet<ChunkCoord>();
                _modifiedChunksByRegion.Add(region, regionChunks);
            }
            regionChunks.Add(chunk);
        }

        if (!mined.Add(VoxelMath.LocalIndex(voxel, _chunkSize)))
        {
            return false;
        }

        _sparseMinedCountByRegion[region] = checked(_sparseMinedCountByRegion.GetValueOrDefault(region) + 1L);
        MinedVoxelCount = checked(MinedVoxelCount + 1L);
        return true;
    }

    public long GetSparseMinedCountInRegion(RegionCoord region)
        => _sparseMinedCountByRegion.GetValueOrDefault(region);

    /// <summary>
    /// Replaces all sparse per-voxel deviations inside a region with one aggregate exhausted marker.
    /// Region bookkeeping makes this proportional only to modified chunks in that region, rather than
    /// scanning every modified chunk in the world.
    /// </summary>
    public long MarkRegionExhausted(RegionCoord region, long regionMineableCount)
    {
        if (regionMineableCount <= 0 || _exhaustedRegions.ContainsKey(region))
        {
            return 0L;
        }

        long alreadyMined = _sparseMinedCountByRegion.GetValueOrDefault(region);
        if (_modifiedChunksByRegion.Remove(region, out HashSet<ChunkCoord>? remove))
        {
            foreach (ChunkCoord chunk in remove)
            {
                _minedByChunk.Remove(chunk);
            }
        }
        _sparseMinedCountByRegion.Remove(region);

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
        _sparseMinedCountByRegion.Clear();
        _modifiedChunksByRegion.Clear();
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
            _sparseMinedCountByRegion[region] = checked(_sparseMinedCountByRegion.GetValueOrDefault(region) + indices.Count);
            if (!_modifiedChunksByRegion.TryGetValue(region, out HashSet<ChunkCoord>? regionChunks))
            {
                regionChunks = new HashSet<ChunkCoord>();
                _modifiedChunksByRegion.Add(region, regionChunks);
            }
            regionChunks.Add(key);
            MinedVoxelCount = checked(MinedVoxelCount + indices.Count);
        }
    }
}
