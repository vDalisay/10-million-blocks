using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    public long AutomationPresentationUpdatesQueued { get; private set; }
    public long AutomationPresentationUpdatesSuppressed { get; private set; }

    /// <summary>
    /// Automation simulation is authoritative regardless of camera position, but presentation work is
    /// only useful when the automation is in the currently observed detail set and on the camera-facing
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
    /// World state is already authoritative before this method is called. For large worlds, only queue
    /// exact mesh rebuilds when the changed area can contribute pixels now. A drill on the far side or
    /// deep inside the cube therefore keeps mining computationally without forcing hidden chunk scans.
    /// When the player later brings that side into view, normal deterministic chunk construction reads
    /// the accumulated sparse state and presents the latest result in one catch-up build.
    /// </summary>
    public void MarkAutomationDirty(Vector3I voxel)
    {
        if (!StreamingEnabled)
        {
            MarkDirtyAround(voxel);
            AutomationPresentationUpdatesQueued++;
            return;
        }

        Vector3I outward = _world.Source.GetOutwardNormal(voxel);
        if (!ShouldPresentAutomation(voxel, outward))
        {
            AutomationPresentationUpdatesSuppressed++;
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        MarkAutomationChunkIfObserved(ChunkCoord.FromVoxel(voxel, chunkSize));
        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            MarkAutomationChunkIfObserved(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
        }
        AutomationPresentationUpdatesQueued++;
    }

    public void FocusAutomationVoxel(Vector3I voxel)
    {
        _camera?.FocusWorldPoint(VoxelToWorld(voxel));
    }

    private void MarkAutomationChunkIfObserved(ChunkCoord chunk)
    {
        if (_desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk))
        {
            // Use the normal dirty path so visible full-surface chunks switch to exact exposed-voxel
            // rebuilding. Suppressed chunks deliberately never enter this path until viewed later.
            MarkChunkDirty(chunk, forceExact: FullSurfaceRenderer);
        }
    }
}
