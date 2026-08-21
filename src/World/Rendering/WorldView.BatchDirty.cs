using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Coalesces a large authoritative mutation set into unique affected chunks before scheduling
    /// rebuilds. Replay/world-event systems can remove hundreds of voxels in one logical burst without
    /// paying seven HashSet insert paths per voxel all the way into the renderer queue.
    /// </summary>
    public void MarkDirtyBatch(IEnumerable<Vector3I> voxels)
    {
        int chunkSize = _world.Profile.ChunkSize;
        var chunks = new HashSet<ChunkCoord>();

        foreach (Vector3I voxel in voxels)
        {
            chunks.Add(ChunkCoord.FromVoxel(voxel, chunkSize));
            foreach (Vector3I direction in VoxelMath.Neighbors)
            {
                chunks.Add(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
            }
        }

        foreach (ChunkCoord chunk in chunks)
        {
            MarkChunkDirty(chunk, forceExact: FullSurfaceRenderer);
        }
    }
}
