using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

/// <summary>
/// Sparse authoritative mined-state store. Modified chunks use a compact bitset rather than a
/// HashSet&lt;int&gt; per mined voxel. This matters increasingly as worlds approach one million blocks:
/// membership becomes one array lookup/bit test and dense modified chunks no longer create thousands
/// of managed hash entries. Save snapshots remain the same sorted local-index format.
/// </summary>
public sealed class WorldStateStore
{
    private sealed class ChunkBits
    {
        private readonly ulong[] _words;
        private readonly int _capacity;

        public ChunkBits(int capacity)
        {
            _capacity = capacity;
            _words = new ulong[(capacity + 63) >> 6];
        }

        public int Count { get; private set; }

        public bool Contains(int index)
        {
            if ((uint)index >= (uint)_capacity) return false;
            int wordIndex = index >> 6;
            ulong mask = 1UL << (index & 63);
            return (_words[wordIndex] & mask) != 0;
        }

        public bool Add(int index)
        {
            if ((uint)index >= (uint)_capacity) return false;
            int wordIndex = index >> 6;
            ulong mask = 1UL << (index & 63);
            if ((_words[wordIndex] & mask) != 0) return false;
            _words[wordIndex] |= mask;
            Count++;
            return true;
        }

        public void CopyIndicesTo(List<int> destination)
        {
            for (int wordIndex = 0; wordIndex < _words.Length; wordIndex++)
            {
                ulong word = _words[wordIndex];
                while (word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(word);
                    int index = (wordIndex << 6) + bit;
                    if (index < _capacity) destination.Add(index);
                    word &= word - 1;
                }
            }
        }

        public List<int> ToIndexList()
        {
            var result = new List<int>(Count);
            CopyIndicesTo(result);
            return result;
        }
    }

    private readonly int _chunkSize;
    private readonly int _chunkVoxelCapacity;
    private readonly int _regionSizeInChunks;
    private readonly Dictionary<ChunkCoord, ChunkBits> _minedByChunk = new();
    private readonly Dictionary<RegionCoord, long> _sparseMinedCountByRegion = new();
    private readonly Dictionary<RegionCoord, HashSet<ChunkCoord>> _modifiedChunksByRegion = new();
    private readonly Dictionary<RegionCoord, long> _exhaustedRegions = new();
    private bool _regionTrackingEnabled;

    public WorldStateStore(int chunkSize, int regionSizeInChunks = 8)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (regionSizeInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(regionSizeInChunks));
        _chunkSize = chunkSize;
        _chunkVoxelCapacity = checked(chunkSize * chunkSize * chunkSize);
        _regionSizeInChunks = regionSizeInChunks;
    }

    public int ModifiedChunkCount => _minedByChunk.Count;
    public int SparseModifiedRegionCount => _regionTrackingEnabled ? _sparseMinedCountByRegion.Count : 0;
    public int ExhaustedRegionCount => _exhaustedRegions.Count;
    public long SparseVoxelOverrideCount
        => _regionTrackingEnabled ? _sparseMinedCountByRegion.Values.Sum() : MinedVoxelCount;
    public long MinedVoxelCount { get; private set; }

    public bool IsRegionExhausted(RegionCoord region) => _exhaustedRegions.ContainsKey(region);
    public bool HasMinedVoxels(ChunkCoord chunk) => _minedByChunk.ContainsKey(chunk);
    public int GetMinedVoxelCount(ChunkCoord chunk)
        => _minedByChunk.TryGetValue(chunk, out ChunkBits? mined) ? mined.Count : 0;

    public bool IsMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);

        if (_exhaustedRegions.Count > 0)
        {
            RegionCoord region = RegionCoord.FromChunk(chunk, _regionSizeInChunks);
            if (_exhaustedRegions.ContainsKey(region))
            {
                return true;
            }
        }

        return _minedByChunk.TryGetValue(chunk, out ChunkBits? mined)
            && mined.Contains(VoxelMath.LocalIndex(voxel, chunk, _chunkSize));
    }

    public bool MarkMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);
        RegionCoord region = default;

        if (_regionTrackingEnabled)
        {
            region = RegionCoord.FromChunk(chunk, _regionSizeInChunks);
            if (_exhaustedRegions.ContainsKey(region))
            {
                return false;
            }
        }

        if (!_minedByChunk.TryGetValue(chunk, out ChunkBits? mined))
        {
            mined = new ChunkBits(_chunkVoxelCapacity);
            _minedByChunk.Add(chunk, mined);
            if (_regionTrackingEnabled)
            {
                AddModifiedChunkToRegion(region, chunk);
            }
        }

        if (!mined.Add(VoxelMath.LocalIndex(voxel, chunk, _chunkSize)))
        {
            return false;
        }

        if (_regionTrackingEnabled)
        {
            _sparseMinedCountByRegion[region] = checked(_sparseMinedCountByRegion.GetValueOrDefault(region) + 1L);
        }
        MinedVoxelCount = checked(MinedVoxelCount + 1L);
        return true;
    }

    public long GetSparseMinedCountInRegion(RegionCoord region)
    {
        EnsureRegionTracking();
        return _sparseMinedCountByRegion.GetValueOrDefault(region);
    }

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

        EnsureRegionTracking();
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
        => _minedByChunk.TryGetValue(chunk, out ChunkBits? mined)
            ? mined.ToIndexList()
            : Array.Empty<int>();

    /// <summary>
    /// Hot renderer path for one-time sparse-frontier bootstrap. The caller owns and reuses the list,
    /// avoiding a new managed List allocation every time a modified chunk becomes presentation-relevant.
    /// </summary>
    public int CopyMinedLocalIndices(ChunkCoord chunk, List<int> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (!_minedByChunk.TryGetValue(chunk, out ChunkBits? mined)) return 0;
        if (destination.Capacity < mined.Count) destination.Capacity = mined.Count;
        mined.CopyIndicesTo(destination);
        return destination.Count;
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
                MinedLocalIndices = pair.Value.ToIndexList(),
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
        _regionTrackingEnabled = false;
        MinedVoxelCount = 0L;

        foreach (ExhaustedRegionSnapshot snapshot in exhaustedRegions)
        {
            if (snapshot.MinedCount <= 0)
            {
                continue;
            }

            _regionTrackingEnabled = true;
            var region = new RegionCoord(snapshot.X, snapshot.Y, snapshot.Z);
            if (_exhaustedRegions.TryAdd(region, snapshot.MinedCount))
            {
                MinedVoxelCount = checked(MinedVoxelCount + snapshot.MinedCount);
            }
        }

        foreach (MinedChunkSnapshot snapshot in chunks)
        {
            var key = new ChunkCoord(snapshot.X, snapshot.Y, snapshot.Z);
            RegionCoord region = default;
            if (_regionTrackingEnabled)
            {
                region = RegionCoord.FromChunk(key, _regionSizeInChunks);
                if (_exhaustedRegions.ContainsKey(region))
                {
                    continue;
                }
            }

            var bits = new ChunkBits(_chunkVoxelCapacity);
            foreach (int index in snapshot.MinedLocalIndices)
            {
                bits.Add(index);
            }

            if (bits.Count == 0)
            {
                continue;
            }

            _minedByChunk[key] = bits;
            if (_regionTrackingEnabled)
            {
                _sparseMinedCountByRegion[region] = checked(_sparseMinedCountByRegion.GetValueOrDefault(region) + bits.Count);
                AddModifiedChunkToRegion(region, key);
            }
            MinedVoxelCount = checked(MinedVoxelCount + bits.Count);
        }
    }

    private void EnsureRegionTracking()
    {
        if (_regionTrackingEnabled) return;
        _regionTrackingEnabled = true;
        _sparseMinedCountByRegion.Clear();
        _modifiedChunksByRegion.Clear();

        foreach ((ChunkCoord chunk, ChunkBits bits) in _minedByChunk)
        {
            RegionCoord region = RegionCoord.FromChunk(chunk, _regionSizeInChunks);
            _sparseMinedCountByRegion[region] = checked(_sparseMinedCountByRegion.GetValueOrDefault(region) + bits.Count);
            AddModifiedChunkToRegion(region, chunk);
        }
    }

    private void AddModifiedChunkToRegion(RegionCoord region, ChunkCoord chunk)
    {
        if (!_modifiedChunksByRegion.TryGetValue(region, out HashSet<ChunkCoord>? regionChunks))
        {
            regionChunks = new HashSet<ChunkCoord>();
            _modifiedChunksByRegion.Add(region, regionChunks);
        }
        regionChunks.Add(chunk);
    }
}
