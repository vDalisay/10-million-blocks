using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Storage;

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
}
