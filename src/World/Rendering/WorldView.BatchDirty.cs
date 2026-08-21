using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly HashSet<ChunkCoord> _dirtyBatchScratch = new();

    /// <summary>
    /// Coalesces a large authoritative mutation set into unique affected chunks before scheduling
    /// rebuilds. The scratch set is retained and cleared between calls so replay/world-event bursts do
    /// not allocate a new HashSet every rendered frame.
    /// </summary>
    public void MarkDirtyBatch(IEnumerable<Vector3I> voxels)
    {
        int chunkSize = _world.Profile.ChunkSize;
        _dirtyBatchScratch.Clear();

        foreach (Vector3I voxel in voxels)
        {
            _dirtyBatchScratch.Add(ChunkCoord.FromVoxel(voxel, chunkSize));
            foreach (Vector3I direction in VoxelMath.Neighbors)
            {
                _dirtyBatchScratch.Add(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
            }
        }

        foreach (ChunkCoord chunk in _dirtyBatchScratch)
        {
            MarkChunkDirty(chunk, forceExact: FullSurfaceRenderer);
        }
    }
}
