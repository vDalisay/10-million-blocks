using System;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Automation simulation is authoritative regardless of camera position, but presentation work is
    /// only useful when the automation is in the currently streamed detail set and on the camera-facing
    /// side of the cube. Hidden/back-side/deep automations can therefore stay computational-only.
    /// Small authored worlds retain their eager behavior.
    /// </summary>
    public bool ShouldPresentAutomation(Vector3I voxel, Vector3I outward)
    {
        if (!StreamingEnabled)
        {
            return true;
        }

        ChunkCoord chunk = ChunkCoord.FromVoxel(voxel, _world.Profile.ChunkSize);
        if (!_desiredChunks.Contains(chunk) && !_chunkRoots.ContainsKey(chunk))
        {
            return false;
        }

        if (_camera?.Camera is null || outward == Vector3I.Zero)
        {
            return true;
        }

        Vector3 worldPosition = VoxelToWorld(voxel);
        Vector3 toCamera = _camera.Camera.GlobalPosition - worldPosition;
        return toCamera.Dot((Vector3)outward) > 0.0f;
    }

    /// <summary>
    /// Dirty only render chunks that can currently contribute pixels. World state is already updated
    /// independently, so an off-screen mined area needs no mesh rebuild until streaming later visits it.
    /// </summary>
    public void MarkAutomationDirty(Vector3I voxel)
    {
        if (!StreamingEnabled)
        {
            MarkDirtyAround(voxel);
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        MarkAutomationChunkIfObserved(ChunkCoord.FromVoxel(voxel, chunkSize));
        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            MarkAutomationChunkIfObserved(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
        }
    }

    private void MarkAutomationChunkIfObserved(ChunkCoord chunk)
    {
        if (_desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk))
        {
            _dirtyChunks.Add(chunk);
        }
    }
}
