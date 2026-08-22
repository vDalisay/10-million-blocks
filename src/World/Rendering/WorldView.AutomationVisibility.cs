using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    // Automation can mutate hundreds of blocks per second, but the exterior surface does not need to
    // reconstruct a whole chunk for every simulation frame. Cavity/tunnel walls have their own sparse
    // renderer and remain responsive independently; coalescing the comparatively expensive outer-shell
    // commit to ~13 Hz cuts repeated 16x16 surface-column reconstruction under large fleets without
    // affecting authoritative mining, rewards or save state.
    private const double VisibleAutomationFlushIntervalSeconds = 0.075;
    private const int VisibleAutomationFlushChunkBudget = 64;

    private readonly HashSet<ChunkCoord> _deferredAutomationChunks = new();
    private readonly HashSet<ChunkCoord> _pendingVisibleAutomationChunks = new();
    private readonly List<ChunkCoord> _deferredPromotionScratch = new();
    private readonly List<ChunkCoord> _visibleAutomationFlushScratch = new(VisibleAutomationFlushChunkBudget);
    private AutomationPresentationWorker? _automationPresentationWorker;
    private bool _deferredRefreshStateInitialized;
    private Vector3 _lastDeferredRefreshCameraPosition;
    private Vector3 _lastDeferredRefreshCameraForward;
    private int _lastDeferredRefreshCount = -1;
    private int _lastDeferredRefreshDesiredCount = -1;
    private int _lastDeferredRefreshResidentCount = -1;
    private int _lastDeferredRefreshSparseRootCount = -1;

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
        bool inWorkingSet = _desiredChunks.Contains(chunk)
            || _chunkRoots.ContainsKey(chunk)
            || _sparseOverlayRoots.ContainsKey(chunk);
        if (!inWorkingSet)
        {
            return false;
        }

        if (FullSurfaceRenderer)
        {
            // Once a full-surface world has been excavated, the original cube outward normal is no
            // longer a valid visibility test for a tunnel/cavity. A wall can face the camera while its
            // original cube normal points away. Use only the conservative chunk/frustum policy here.
            return IsChunkPresentationRelevant(chunk);
        }

        return IsAutomationFaceCameraFacing(voxel, outward);
    }

    /// <summary>
    /// World state is authoritative regardless of camera position. Visible automation records the same
    /// six-neighbour incremental frontier as manual mining. Hidden and off-frustum automation invalidates
    /// only the affected chunk frontier and stores a chunk marker. When that area becomes visible, the
    /// frontier is reconstructed once from compact mined state.
    ///
    /// Important: full-surface cavity promotion must never use the original cube-face normal. That old
    /// shortcut was able to leave a deferred cavity permanently stale after the camera moved to a view
    /// where the tunnel itself was visible, which produced the persistent black see-through gaps seen in
    /// the million-block stress world.
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

        Camera3D camera = _camera.Camera;
        Vector3 cameraPosition = camera.GlobalPosition;
        Vector3 cameraForward = -camera.GlobalBasis.Z.Normalized();
        bool unchanged = _deferredRefreshStateInitialized
            && _lastDeferredRefreshCount == _deferredAutomationChunks.Count
            && _lastDeferredRefreshDesiredCount == _desiredChunks.Count
            && _lastDeferredRefreshResidentCount == _chunkRoots.Count
            && _lastDeferredRefreshSparseRootCount == _sparseOverlayRoots.Count
            && cameraPosition.DistanceSquaredTo(_lastDeferredRefreshCameraPosition) < 0.0004f
            && cameraForward.Dot(_lastDeferredRefreshCameraForward) > 0.999995f;
        if (unchanged)
        {
            return;
        }

        _deferredRefreshStateInitialized = true;
        _lastDeferredRefreshCameraPosition = cameraPosition;
        _lastDeferredRefreshCameraForward = cameraForward;
        _lastDeferredRefreshDesiredCount = _desiredChunks.Count;
        _lastDeferredRefreshResidentCount = _chunkRoots.Count;
        _lastDeferredRefreshSparseRootCount = _sparseOverlayRoots.Count;

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

            bool inWorkingSet = _desiredChunks.Contains(chunk)
                || _chunkRoots.ContainsKey(chunk)
                || _sparseOverlayRoots.ContainsKey(chunk);
            bool eligible = FullSurfaceRenderer || inWorkingSet;
            bool cameraRelevant = !FullSurfaceRenderer || IsChunkPresentationRelevant(chunk);
            bool faceRelevant = FullSurfaceRenderer || IsAutomationFaceCameraFacing(center, outward);
            if (eligible && cameraRelevant && faceRelevant)
            {
                // Deferred state may represent a long period of hidden mining. Promote immediately when
                // it becomes visible rather than waiting for the live-automation coalescer.
                MarkInteractiveChunkDirty(chunk);
                _deferredPromotionScratch.Add(chunk);
            }
        }

        foreach (ChunkCoord chunk in _deferredPromotionScratch)
        {
            _deferredAutomationChunks.Remove(chunk);
        }
        _lastDeferredRefreshCount = _deferredAutomationChunks.Count;
        _lastDeferredRefreshSparseRootCount = _sparseOverlayRoots.Count;
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
        bool inWorkingSet = _desiredChunks.Contains(chunk)
            || _chunkRoots.ContainsKey(chunk)
            || _sparseOverlayRoots.ContainsKey(chunk);
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

            // Camera motion during the coalescing window can make a previously visible chunk irrelevant.
            // Convert it to deferred work rather than paying for an off-screen rebuild.
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
