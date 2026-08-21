using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly HashSet<ChunkCoord> _dirtyBatchScratch = new();

    /// <summary>
    /// Marks the chunk containing one changed voxel plus only the adjacent chunks whose shared border
    /// can actually have changed exposure. Full-surface worlds now use the cheap surface-column rebuild
    /// plus a sparse exposure overlay instead of rescanning the whole 16^3 chunk after every click.
    /// </summary>
    public void MarkDirtyVoxel(Vector3I voxel)
    {
        int chunkSize = _world.Profile.ChunkSize;
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, chunkSize);
        MarkInteractiveChunkDirty(chunk);

        int x = VoxelMath.PositiveMod(voxel.X, chunkSize);
        int y = VoxelMath.PositiveMod(voxel.Y, chunkSize);
        int z = VoxelMath.PositiveMod(voxel.Z, chunkSize);

        if (x == 0) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (x == chunkSize - 1) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (y == 0) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (y == chunkSize - 1) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (z == 0) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (z == chunkSize - 1) MarkInteractiveChunkDirty(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
    }

    /// <summary>
    /// Coalesces a large authoritative mutation set into unique affected chunks before scheduling
    /// rebuilds. Only cross-chunk voxel neighbours are inserted: an interior changed voxel affects one
    /// render chunk, not seven equivalent ChunkCoord lookups. The scratch set is retained between calls.
    /// </summary>
    public void MarkDirtyBatch(IEnumerable<Vector3I> voxels)
    {
        int chunkSize = _world.Profile.ChunkSize;
        _dirtyBatchScratch.Clear();

        foreach (Vector3I voxel in voxels)
        {
            AddAffectedChunks(voxel, chunkSize, _dirtyBatchScratch);
        }

        FlushDirtyBatchScratch();
    }

    /// <summary>
    /// Allocation-free dirty-region path for bounded blast effects. It coalesces the entire sphere to
    /// chunk coordinates before touching the rebuild scheduler, which is substantially cheaper than
    /// calling MarkDirtyAround for every removed voxel in a crater.
    /// </summary>
    public void MarkDirtySphere(Vector3I center, int radius)
    {
        int safeRadius = Math.Max(0, radius);
        if (safeRadius == 0)
        {
            MarkDirtyVoxel(center);
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        int radiusSquared = safeRadius * safeRadius;
        _dirtyBatchScratch.Clear();

        for (int z = -safeRadius; z <= safeRadius; z++)
        for (int y = -safeRadius; y <= safeRadius; y++)
        for (int x = -safeRadius; x <= safeRadius; x++)
        {
            if (x * x + y * y + z * z > radiusSquared) continue;
            AddAffectedChunks(center + new Vector3I(x, y, z), chunkSize, _dirtyBatchScratch);
        }

        FlushDirtyBatchScratch();
    }

    private static void AddAffectedChunks(Vector3I voxel, int chunkSize, HashSet<ChunkCoord> chunks)
    {
        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, chunkSize);
        chunks.Add(chunk);

        int x = VoxelMath.PositiveMod(voxel.X, chunkSize);
        int y = VoxelMath.PositiveMod(voxel.Y, chunkSize);
        int z = VoxelMath.PositiveMod(voxel.Z, chunkSize);

        if (x == 0) chunks.Add(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (x == chunkSize - 1) chunks.Add(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (y == 0) chunks.Add(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (y == chunkSize - 1) chunks.Add(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (z == 0) chunks.Add(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (z == chunkSize - 1) chunks.Add(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
    }

    private void FlushDirtyBatchScratch()
    {
        foreach (ChunkCoord chunk in _dirtyBatchScratch)
        {
            MarkInteractiveChunkDirty(chunk);
        }
    }
}
