using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    // Automation can mutate hundreds of blocks per second, but its visible terrain does not need to
    // reconstruct a chunk for every rendered frame. Coalesce observed automation changes for 50 ms and
    // let the normal dirty-chunk scheduler rebuild each affected chunk once. Authoritative mining and
    // the incremental exposure frontier still update immediately; only the expensive base presentation
    // commit is rate-limited. At 20 Hz this remains visually responsive while substantially reducing
    // repeated chunk reconstruction when several machines work in the same area.
    private const double VisibleAutomationFlushIntervalSeconds = 0.05;
    private const int VisibleAutomationFlushChunkBudget = 64;

    private readonly HashSet<ChunkCoord> _deferredAutomationChunks = new();
    private readonly HashSet<ChunkCoord> _pendingVisibleAutomationChunks = new();
    private readonly List<ChunkCoord> _deferredPromotionScratch = new();
    private readonly List<ChunkCoord> _visibleAutomationFlushScratch = new(VisibleAutomationFlushChunkBudget);
    private AutomationPresentationWorker? _automationPresentationWorker;
    private bool _deferredRefreshStateInitialized;
    private Vector3 _lastDeferredRefreshCameraPosition;
    private int _lastDeferredRefreshCount = -1;
    private int _lastDeferredRefreshDesiredCount = -1;
    private int _lastDeferredRefreshResidentCount = -1;

    public long AutomationPresentationUpdatesQueued { get; private set; }
    public long AutomationPresentationUpdatesSuppressed { get; private set; }
    public long AutomationPresentationChunkFlushes { get; private set; }
    public int DeferredAutomationChunkCount => _deferredAutomationChunks.Count;
    public int PendingVisibleAutomationChunkCount => _pendingVisibleAutomationChunks.Count;

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

        if (FullSurfaceRenderer && !IsChunkPresentationRelevant(chunk))
        {
            return false;
        }

        return IsAutomationFaceCameraFacing(voxel, outward);
    }

    /// <summary>
    /// World state is authoritative regardless of camera position. Visible automation records the same
    /// six-neighbour incremental frontier as manual mining. Hidden, back-side and off-frustum automation
    /// does even less: it invalidates only the affected chunk frontier and stores a chunk marker. Visible
    /// base-chunk reconstruction is coalesced at 20 Hz rather than being re-requested every simulation
    /// frame. If a hidden area later becomes presentation-relevant, its frontier is reconstructed once
    /// from compact mined state.
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
            InvalidateSparseExposureFrontierForMutation(voxel);
            DeferAutomationChunk(changedChunk);
            DeferBoundaryAutomationChunks(changedChunk, localX, localY, localZ, chunkSize);
            AutomationPresentationUpdatesSuppressed++;
            return;
        }

        RecordSparseExposureMutation(voxel);
        QueueAutomationChunkIfObserved(changedChunk);
        QueueBoundaryAutomationChunksIfObserved(changedChunk, localX, localY, localZ, chunkSize);
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
            bool cameraRelevant = !FullSurfaceRenderer || IsChunkPresentationRelevant(chunk);
            if (eligible && cameraRelevant && IsAutomationFaceCameraFacing(center, outward))
            {
                // Deferred state may represent a long period of hidden mining. Promote immediately when
                // it becomes visible rather than waiting for the 50-ms live-automation coalescer.
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

    private void QueueBoundaryAutomationChunksIfObserved(
        ChunkCoord chunk,
        int localX,
        int localY,
        int localZ,
        int chunkSize)
    {
        if (localX == 0) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X - 1, chunk.Y, chunk.Z));
        else if (localX == chunkSize - 1) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X + 1, chunk.Y, chunk.Z));

        if (localY == 0) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y - 1, chunk.Z));
        else if (localY == chunkSize - 1) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y + 1, chunk.Z));

        if (localZ == 0) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y, chunk.Z - 1));
        else if (localZ == chunkSize - 1) QueueAutomationChunkIfObserved(new ChunkCoord(chunk.X, chunk.Y, chunk.Z + 1));
    }

    private void DeferAutomationChunk(ChunkCoord chunk)
    {
        if (!ChunkInWorldBounds(chunk)) return;
        _pendingVisibleAutomationChunks.Remove(chunk);
        _deferredAutomationChunks.Add(chunk);
    }

    private void QueueAutomationChunkIfObserved(ChunkCoord chunk)
    {
        bool inWorkingSet = _desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk);
        if (!inWorkingSet)
        {
            return;
        }

        if (FullSurfaceRenderer && !IsChunkPresentationRelevant(chunk))
        {
            DeferAutomationChunk(chunk);
            return;
        }

        if (_pendingVisibleAutomationChunks.Add(chunk))
        {
            EnsureAutomationPresentationWorker();
        }
        _deferredAutomationChunks.Remove(chunk);
    }

    private void EnsureAutomationPresentationWorker()
    {
        if (_automationPresentationWorker is not null
            && GodotObject.IsInstanceValid(_automationPresentationWorker))
        {
            return;
        }

        _automationPresentationWorker = new AutomationPresentationWorker(this)
        {
            Name = "AutomationPresentationWorker",
        };
        AddChild(_automationPresentationWorker);
    }

    private void FlushVisibleAutomationChunks()
    {
        if (_pendingVisibleAutomationChunks.Count == 0) return;

        _visibleAutomationFlushScratch.Clear();
        foreach (ChunkCoord chunk in _pendingVisibleAutomationChunks)
        {
            _visibleAutomationFlushScratch.Add(chunk);
            if (_visibleAutomationFlushScratch.Count >= VisibleAutomationFlushChunkBudget) break;
        }

        foreach (ChunkCoord chunk in _visibleAutomationFlushScratch)
        {
            _pendingVisibleAutomationChunks.Remove(chunk);
            if (!ChunkInWorldBounds(chunk)) continue;

            // Camera motion during the 50-ms coalescing window can make a previously visible chunk
            // irrelevant. Convert it to deferred work rather than paying for an off-screen rebuild.
            if (FullSurfaceRenderer && !IsChunkPresentationRelevant(chunk))
            {
                _deferredAutomationChunks.Add(chunk);
                continue;
            }

            MarkInteractiveChunkDirty(chunk);
            AutomationPresentationChunkFlushes++;
        }
    }

    private sealed partial class AutomationPresentationWorker : Node
    {
        private readonly WorldView _owner;
        private double _elapsed;

        public AutomationPresentationWorker(WorldView owner)
        {
            _owner = owner;
        }

        public override void _Process(double delta)
        {
            _elapsed += Math.Max(0.0, delta);
            if (_elapsed < VisibleAutomationFlushIntervalSeconds) return;
            // Drop catch-up rather than running several flush passes after a hitch. The dirty set is
            // retained, so nothing is lost; it simply drains through the normal bounded renderer path.
            _elapsed = 0.0;
            _owner.FlushVisibleAutomationChunks();
        }
    }
}
