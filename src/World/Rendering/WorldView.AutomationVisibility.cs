using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly HashSet<ChunkCoord> _deferredAutomationChunks = new();
    private readonly List<ChunkCoord> _deferredPromotionScratch = new();
    private bool _deferredRefreshStateInitialized;
    private Vector3 _lastDeferredRefreshCameraPosition;
    private int _lastDeferredRefreshCount = -1;
    private int _lastDeferredRefreshDesiredCount = -1;
    private int _lastDeferredRefreshResidentCount = -1;

    public long AutomationPresentationUpdatesQueued { get; private set; }
    public long AutomationPresentationUpdatesSuppressed { get; private set; }
    public int DeferredAutomationChunkCount => _deferredAutomationChunks.Count;

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

        return IsAutomationFaceCameraFacing(voxel, outward);
    }

    /// <summary>
    /// World state is already authoritative before this method is called. For large worlds, only queue
    /// exact mesh rebuilds when the changed area can contribute pixels now. A drill on the far side or
    /// deep inside the cube therefore keeps mining computationally without forcing hidden chunk scans.
    /// Suppressed chunks are remembered and rebuilt in a coalesced pass when the player later faces
    /// their side of the cube.
    /// </summary>
    public void MarkAutomationDirty(Vector3I voxel)
    {
        if (!StreamingEnabled)
        {
            MarkDirtyAround(voxel);
            AutomationPresentationUpdatesQueued++;
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        Vector3I outward = _world.Source.GetOutwardNormal(voxel);
        ChunkCoord changedChunk = ChunkCoord.FromVoxel(voxel, chunkSize);
        if (!ShouldPresentAutomation(voxel, outward))
        {
            DeferAutomationChunk(changedChunk);
            foreach (Vector3I direction in VoxelMath.Neighbors)
            {
                DeferAutomationChunk(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
            }
            AutomationPresentationUpdatesSuppressed++;
            return;
        }

        MarkAutomationChunkIfObserved(changedChunk);
        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            MarkAutomationChunkIfObserved(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
        }
        AutomationPresentationUpdatesQueued++;
    }

    /// <summary>
    /// Called at the same low frequency as automation visibility checks. Hundreds of off-screen mining
    /// ticks can collapse into one deferred chunk rebuild instead of rebuilding the same hidden mesh on
    /// every tick. Full-surface worlds may promote modified interior chunks once their cube face is
    /// viewed; macro-streamed experiments still require the chunk to be in the camera working set.
    ///
    /// The deferred set can stay unchanged for seconds while automation keeps working in the same hidden
    /// chunks. Cache that state and the camera position so the 8 Hz policy tick does not repeatedly walk
    /// the same HashSet. The promotion list is also retained as scratch storage to avoid periodic GC.
    /// </summary>
    public void RefreshDeferredAutomationPresentation()
    {
        if (_deferredAutomationChunks.Count == 0 || _camera?.Camera is null)
        {
            _deferredRefreshStateInitialized = false;
            return;
        }

        Vector3 cameraPosition = _camera.Camera.GlobalPosition;
        bool unchanged = _deferredRefreshStateInitialized
            && _lastDeferredRefreshCount == _deferredAutomationChunks.Count
            && _lastDeferredRefreshDesiredCount == _desiredChunks.Count
            && _lastDeferredRefreshResidentCount == _chunkRoots.Count
            && cameraPosition.DistanceSquaredTo(_lastDeferredRefreshCameraPosition) < 0.0004f;
        if (unchanged)
        {
            return;
        }

        _deferredRefreshStateInitialized = true;
        _lastDeferredRefreshCameraPosition = cameraPosition;
        _lastDeferredRefreshDesiredCount = _desiredChunks.Count;
        _lastDeferredRefreshResidentCount = _chunkRoots.Count;

        _deferredPromotionScratch.Clear();
        foreach (ChunkCoord chunk in _deferredAutomationChunks)
        {
            if (!ChunkInWorldBounds(chunk))
            {
                _deferredPromotionScratch.Add(chunk);
                continue;
            }

            Vector3I min = chunk.MinVoxel(_world.Profile.ChunkSize);
            Vector3I center = min + new Vector3I(
                _world.Profile.ChunkSize / 2,
                _world.Profile.ChunkSize / 2,
                _world.Profile.ChunkSize / 2);
            Vector3I outward = _world.Source.GetOutwardNormal(center);

            bool inWorkingSet = _desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk);
            bool eligible = FullSurfaceRenderer || inWorkingSet;
            if (eligible && IsAutomationFaceCameraFacing(center, outward))
            {
                // MarkChunkDirty adds a modified interior chunk to the full-surface working set only at
                // this point, after it has become presentation-relevant.
                MarkChunkDirty(chunk, forceExact: FullSurfaceRenderer);
                _deferredPromotionScratch.Add(chunk);
            }
        }

        foreach (ChunkCoord chunk in _deferredPromotionScratch)
        {
            _deferredAutomationChunks.Remove(chunk);
        }
        _lastDeferredRefreshCount = _deferredAutomationChunks.Count;
    }

    public void FocusAutomationVoxel(Vector3I voxel)
    {
        _camera?.FocusWorldPoint(VoxelToWorld(voxel));
    }

    private bool IsAutomationFaceCameraFacing(Vector3I voxel, Vector3I outward)
    {
        if (_camera?.Camera is null || outward == Vector3I.Zero)
        {
            return true;
        }

        Vector3 worldPosition = VoxelToWorld(voxel);
        Vector3 toCamera = _camera.Camera.GlobalPosition - worldPosition;
        return toCamera.Dot((Vector3)outward) > 0.0f;
    }

    private void DeferAutomationChunk(ChunkCoord chunk)
    {
        if (ChunkInWorldBounds(chunk))
        {
            _deferredAutomationChunks.Add(chunk);
        }
    }

    private void MarkAutomationChunkIfObserved(ChunkCoord chunk)
    {
        if (_desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk))
        {
            MarkChunkDirty(chunk, forceExact: FullSurfaceRenderer);
            _deferredAutomationChunks.Remove(chunk);
        }
    }
}
