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
    /// World state is authoritative regardless of camera position. Hidden automation collapses to a
    /// deferred chunk marker; visible automation uses the same sparse full-surface presentation path as
    /// manual mining so a busy machine cannot trigger repeated 16^3 exact scans.
    /// </summary>
    public void MarkAutomationDirty(Vector3I voxel)
    {
        if (!StreamingEnabled)
        {
            MarkDirtyVoxel(voxel);
            AutomationPresentationUpdatesQueued++;
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        Vector3I outward = _world.Source.GetOutwardNormal(voxel);
        ChunkCoord changedChunk = ChunkCoord.FromVoxel(voxel, chunkSize);
        int localX = VoxelMath.PositiveMod(voxel.X, chunkSize);
        int localY = VoxelMath.PositiveMod(voxel.Y, chunkSize);
        int localZ = VoxelMath.PositiveMod(voxel.Z, chunkSize);

        if (!ShouldPresentAutomation(voxel, outward))
        {
            DeferAutomationChunk(changedChunk);
            DeferBoundaryAutomationChunks(changedChunk, localX, localY, localZ, chunkSize);
            AutomationPresentationUpdatesSuppressed++;
            return;
        }

        MarkAutomationChunkIfObserved(changedChunk);
        MarkBoundaryAutomationChunksIfObserved(changedChunk, localX, localY, localZ, chunkSize);
        AutomationPresentationUpdatesQueued++;
    }

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
                MarkInteractiveChunkDirty(chunk);
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

    private void DeferBoundaryAutomationChunks(
        ChunkCoord chunk,
        int localX,
        int localY,
        int localZ,
        int chunkSize)
    {
        if (localX == 0) DeferAutomationChunk(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (localX == chunkSize - 1) DeferAutomationChunk(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (localY == 0) DeferAutomationChunk(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (localY == chunkSize - 1) DeferAutomationChunk(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (localZ == 0) DeferAutomationChunk(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (localZ == chunkSize - 1) DeferAutomationChunk(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
    }

    private void MarkBoundaryAutomationChunksIfObserved(
        ChunkCoord chunk,
        int localX,
        int localY,
        int localZ,
        int chunkSize)
    {
        if (localX == 0) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (localX == chunkSize - 1) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (localY == 0) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (localY == chunkSize - 1) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (localZ == 0) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (localZ == chunkSize - 1) MarkAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
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
            MarkInteractiveChunkDirty(chunk);
            _deferredAutomationChunks.Remove(chunk);
        }
    }
}
