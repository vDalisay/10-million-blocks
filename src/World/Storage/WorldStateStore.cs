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

public sealed class WorldStateStore
{
    private readonly int _chunkSize;
    private readonly Dictionary<ChunkCoord, HashSet<int>> _minedByChunk = new();

    public WorldStateStore(int chunkSize)
    {
        _chunkSize = chunkSize;
    }

    public int ModifiedChunkCount => _minedByChunk.Count;
    public long MinedVoxelCount { get; private set; }

    public bool IsMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);
        return _minedByChunk.TryGetValue(chunk, out HashSet<int>? mined)
            && mined.Contains(VoxelMath.LocalIndex(voxel, _chunkSize));
    }

    public bool MarkMined(Vector3I voxel)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _chunkSize);
        if (!_minedByChunk.TryGetValue(chunk, out HashSet<int>? mined))
        {
            mined = new HashSet<int>();
            _minedByChunk.Add(chunk, mined);
        }

        if (!mined.Add(VoxelMath.LocalIndex(voxel, _chunkSize)))
        {
            return false;
        }

        MinedVoxelCount++;
        return true;
    }

    public IReadOnlyCollection<int> GetMinedLocalIndices(ChunkCoord chunk)
    {
        if (_minedByChunk.TryGetValue(chunk, out HashSet<int>? mined))
        {
            return mined;
        }

        return System.Array.Empty<int>();
    }

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

    public void RestoreSnapshot(IEnumerable<MinedChunkSnapshot> chunks)
    {
        _minedByChunk.Clear();
        MinedVoxelCount = 0;

        int maxLocalIndex = checked(_chunkSize * _chunkSize * _chunkSize);
        foreach (MinedChunkSnapshot snapshot in chunks)
        {
            var key = new ChunkCoord(snapshot.X, snapshot.Y, snapshot.Z);
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
