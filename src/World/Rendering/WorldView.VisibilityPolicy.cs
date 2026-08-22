using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private const double VisibilityRefreshIntervalSeconds = 0.05;
    private const float TreeLodMinimumProjectedBlockPixels = 12.0f;
    private const float TreeLodShowHysteresis = 1.25f;
    private const float TerrainShadowMinimumProjectedBlockPixels = 5.0f;
    private const float TreeShadowMinimumProjectedBlockPixels = 10.0f;

    private bool _visibilityPoseInitialized;
    private Vector3 _lastVisibilityCameraPosition;
    private Vector3 _lastVisibilityCameraForward;
    private Vector2 _lastVisibilityViewportSize;
    private float _lastVisibilityFov;
    private int _lastVisibilityChunkCount = -1;
    private int _lastVisibilitySparseOverlayCount = -1;
    private int _lastVisibilityDetailDistance = -1;
    private readonly Dictionary<ChunkCoord, bool> _treeLodVisibilityByChunk = new();
    private double _visibilityRefreshTimer;
    private VisibilityRefreshTicker? _visibilityRefreshTicker;

    public int PresentedChunkCount { get; private set; }
    public int CulledChunkCount { get; private set; }
    public int BackfaceCulledChunkCount { get; private set; }
    public int FrustumCulledChunkCount { get; private set; }
    public int PresentedSparseOverlayCount { get; private set; }
    public int BackfaceCulledSparseOverlayCount { get; private set; }
    public int FrustumCulledSparseOverlayCount { get; private set; }
    public int LodHiddenTreeBatchCount { get; private set; }
    public int LodShadowDisabledBatchCount { get; private set; }

    private void TickViewDependentPresentation(double delta)
    {
        _visibilityRefreshTimer += Math.Max(0.0, delta);
        if (_visibilityRefreshTimer < VisibilityRefreshIntervalSeconds)
        {
            return;
        }

        _visibilityRefreshTimer = 0.0;
        RefreshViewDependentPresentation();
    }

    private void EnsureVisibilityRefreshTicker()
    {
        if (_visibilityRefreshTicker is not null && GodotObject.IsInstanceValid(_visibilityRefreshTicker))
        {
            return;
        }

        _visibilityRefreshTicker = new VisibilityRefreshTicker(this)
        {
            Name = "VisibilityRefreshTicker",
        };
        AddChild(_visibilityRefreshTicker);
    }

    /// <summary>
    /// Million-block worlds keep deterministic shell chunks resident so orbiting never causes a large
    /// regeneration spike, but resident does not mean drawable. Untouched base-shell roots are rejected
    /// in increasingly expensive stages: cube-face/back-side test first, then a conservative chunk-sphere
    /// frustum test, then screen-space LOD for decorative tree batches and shadows.
    ///
    /// Far/medium views also reject excavated roots on the cube's hidden side, keeping background mining
    /// data-only. Close tunnel inspection stays conservative because a cavity wall can face a direction
    /// unrelated to its original cube face.
    /// </summary>
    public void RefreshViewDependentPresentation()
    {
        if (!FullSurfaceRenderer || _camera?.Camera is null)
        {
            PresentedChunkCount = _chunkRoots.Count;
            CulledChunkCount = 0;
            BackfaceCulledChunkCount = 0;
            FrustumCulledChunkCount = 0;
            PresentedSparseOverlayCount = _sparseOverlayRoots.Count;
            BackfaceCulledSparseOverlayCount = 0;
            FrustumCulledSparseOverlayCount = 0;
            LodHiddenTreeBatchCount = 0;
            LodShadowDisabledBatchCount = 0;
            _visibilityPoseInitialized = false;
            _lastVisibilityChunkCount = _chunkRoots.Count;
            _lastVisibilitySparseOverlayCount = _sparseOverlayRoots.Count;
            return;
        }

        EnsureVisibilityRefreshTicker();

        Camera3D camera = _camera.Camera;
        Vector3 cameraPosition = camera.GlobalPosition;
        Vector3 cameraForward = -camera.GlobalBasis.Z.Normalized();
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        int detailDistance = GraphicsSettingsRuntime.Current?.DetailDistance ?? 1;
        bool samePose = _visibilityPoseInitialized
            && _lastVisibilityChunkCount == _chunkRoots.Count
            && _lastVisibilitySparseOverlayCount == _sparseOverlayRoots.Count
            && cameraPosition.DistanceSquaredTo(_lastVisibilityCameraPosition) < 0.0004f
            && cameraForward.Dot(_lastVisibilityCameraForward) > 0.999995f
            && viewportSize.DistanceSquaredTo(_lastVisibilityViewportSize) < 0.25f
            && detailDistance == _lastVisibilityDetailDistance
            && MathF.Abs(camera.Fov - _lastVisibilityFov) < 0.001f;
        if (samePose)
        {
            return;
        }

        _visibilityPoseInitialized = true;
        _lastVisibilityCameraPosition = cameraPosition;
        _lastVisibilityCameraForward = cameraForward;
        _lastVisibilityViewportSize = viewportSize;
        _lastVisibilityDetailDistance = detailDistance;
        _lastVisibilityFov = camera.Fov;
        _lastVisibilityChunkCount = _chunkRoots.Count;
        _lastVisibilitySparseOverlayCount = _sparseOverlayRoots.Count;

        int presented = 0;
        int backfaceCulled = 0;
        int frustumCulled = 0;
        int presentedSparse = 0;
        int backfaceCulledSparse = 0;
        int frustumCulledSparse = 0;
        int hiddenTreeBatches = 0;
        int shadowDisabledBatches = 0;
        int chunkSize = _world.Profile.ChunkSize;
        float chunkRadius = ChunkPresentationRadiusWorld(chunkSize);

        foreach ((ChunkCoord chunk, Node3D root) in _chunkRoots)
        {
            Vector3I minVoxel = chunk.MinVoxel(chunkSize);
            Vector3I centerVoxel = minVoxel + new Vector3I(chunkSize / 2, chunkSize / 2, chunkSize / 2);
            Vector3 centerWorld = VoxelToWorld(centerVoxel);
            Vector3 toCamera = cameraPosition - centerWorld;

            if (!IsFullSurfaceChunkCameraFacing(chunk, centerVoxel, toCamera))
            {
                root.Visible = false;
                backfaceCulled++;
                continue;
            }

            if (!IsSphereWithinCameraFrustum(centerWorld, chunkRadius))
            {
                root.Visible = false;
                frustumCulled++;
                continue;
            }

            root.Visible = true;
            presented++;
            float projectedBlockPixels = EstimateProjectedBlockPixels(centerWorld);
            ApplyScreenSpaceLod(chunk, root, projectedBlockPixels, ref hiddenTreeBatches, ref shadowDisabledBatches);
        }

        foreach ((ChunkCoord chunk, Node3D root) in _sparseOverlayRoots)
        {
            Vector3I minVoxel = chunk.MinVoxel(chunkSize);
            Vector3I centerVoxel = minVoxel + new Vector3I(chunkSize / 2, chunkSize / 2, chunkSize / 2);
            Vector3 centerWorld = VoxelToWorld(centerVoxel);
            Vector3 toCamera = cameraPosition - centerWorld;
            if (!IsFullSurfaceChunkCameraFacing(chunk, centerVoxel, toCamera))
            {
                root.Visible = false;
                backfaceCulledSparse++;
                continue;
            }
            if (!IsSphereWithinCameraFrustum(centerWorld, chunkRadius))
            {
                root.Visible = false;
                frustumCulledSparse++;
                continue;
            }

            root.Visible = true;
            presentedSparse++;
            float projectedBlockPixels = EstimateProjectedBlockPixels(centerWorld);
            ApplyScreenSpaceLod(chunk, root, projectedBlockPixels, ref hiddenTreeBatches, ref shadowDisabledBatches);
        }

        PresentedChunkCount = presented;
        BackfaceCulledChunkCount = backfaceCulled;
        FrustumCulledChunkCount = frustumCulled;
        PresentedSparseOverlayCount = presentedSparse;
        BackfaceCulledSparseOverlayCount = backfaceCulledSparse;
        FrustumCulledSparseOverlayCount = frustumCulledSparse;
        CulledChunkCount = backfaceCulled + frustumCulled;
        LodHiddenTreeBatchCount = hiddenTreeBatches;
        LodShadowDisabledBatchCount = shadowDisabledBatches;
    }

    private bool IsChunkPresentationRelevant(ChunkCoord chunk)
    {
        if (!FullSurfaceRenderer || _camera?.Camera is null)
        {
            return true;
        }

        int chunkSize = _world.Profile.ChunkSize;
        Vector3I minVoxel = chunk.MinVoxel(chunkSize);
        Vector3I centerVoxel = minVoxel + new Vector3I(chunkSize / 2, chunkSize / 2, chunkSize / 2);
        Vector3 centerWorld = VoxelToWorld(centerVoxel);
        Vector3 toCamera = _camera.Camera.GlobalPosition - centerWorld;
        bool facing = IsFullSurfaceChunkCameraFacing(chunk, centerVoxel, toCamera);
        return facing && IsSphereWithinCameraFrustum(centerWorld, ChunkPresentationRadiusWorld(chunkSize));
    }

    private float ChunkPresentationRadiusWorld(int chunkSize)
    {
        float halfExtent = chunkSize * _world.Profile.BlockSpacing * 0.5f;
        return halfExtent * 1.7320508f + _world.Profile.BlockSpacing * 3.0f;
    }

    private bool IsSphereWithinCameraFrustum(Vector3 centerWorld, float radius)
    {
        if (_camera?.Camera is not Camera3D camera)
        {
            return true;
        }

        Vector3 cameraPosition = camera.GlobalPosition;
        Basis basis = camera.GlobalBasis;
        Vector3 forward = -basis.Z.Normalized();
        Vector3 right = basis.X.Normalized();
        Vector3 up = basis.Y.Normalized();
        Vector3 toCenter = centerWorld - cameraPosition;
        float depth = toCenter.Dot(forward);

        if (depth + radius < camera.Near || depth - radius > camera.Far)
        {
            return false;
        }
        if (depth <= -radius)
        {
            return false;
        }

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float aspect = viewport.Y > 1.0f ? MathF.Max(0.25f, viewport.X / viewport.Y) : 1.0f;
        float tanVertical = MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f);
        float tanHorizontal = tanVertical * aspect;
        float effectiveDepth = MathF.Max(camera.Near, depth);

        float horizontalAllowance = effectiveDepth * tanHorizontal + radius * (1.0f + tanHorizontal);
        float verticalAllowance = effectiveDepth * tanVertical + radius * (1.0f + tanVertical);
        return MathF.Abs(toCenter.Dot(right)) <= horizontalAllowance
            && MathF.Abs(toCenter.Dot(up)) <= verticalAllowance;
    }

    private float EstimateProjectedBlockPixels(Vector3 centerWorld)
    {
        if (_camera?.Camera is not Camera3D camera)
        {
            return float.PositiveInfinity;
        }

        Vector3 toCenter = centerWorld - camera.GlobalPosition;
        float depth = MathF.Max(0.05f, toCenter.Dot(-camera.GlobalBasis.Z.Normalized()));
        float viewportHeight = MathF.Max(1.0f, GetViewport().GetVisibleRect().Size.Y);
        float tanVertical = MathF.Max(0.001f, MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f));
        float focalPixels = viewportHeight / (2.0f * tanVertical);
        return _world.Profile.BlockSpacing * focalPixels / depth;
    }

    private void ApplyScreenSpaceLod(
        ChunkCoord chunk,
        Node node,
        float projectedBlockPixels,
        ref int hiddenTreeBatches,
        ref int shadowDisabledBatches)
    {
        float focus = _camera?.SurfaceFocusBlend ?? 0.0f;
        bool terrainShadows = projectedBlockPixels >= TerrainShadowMinimumProjectedBlockPixels || focus >= 0.30f;
        bool treeShadows = projectedBlockPixels >= TreeShadowMinimumProjectedBlockPixels || focus >= 0.58f;

        foreach (Node child in node.GetChildren())
        {
            if (child is MultiMeshInstance3D batch)
            {
                string name = batch.Name.ToString();
                bool tree = name.StartsWith("Batch_tree_", StringComparison.Ordinal);
                bool visible = !tree || ShouldShowTreeBatch(
                    projectedBlockPixels,
                    focus,
                    _treeLodVisibilityByChunk.GetValueOrDefault(chunk, batch.Visible),
                    GraphicsSettingsRuntime.Current?.DetailDistance ?? 1);
                batch.Visible = visible;
                if (tree) _treeLodVisibilityByChunk[chunk] = visible;
                if (tree && !visible)
                {
                    hiddenTreeBatches++;
                }

                bool shadows = visible && (tree ? treeShadows : terrainShadows);
                GeometryInstance3D.ShadowCastingSetting wanted = shadows
                    ? GeometryInstance3D.ShadowCastingSetting.On
                    : GeometryInstance3D.ShadowCastingSetting.Off;
                if (batch.CastShadow != wanted)
                {
                    batch.CastShadow = wanted;
                }
                if (!shadows)
                {
                    shadowDisabledBatches++;
                }
                continue;
            }

            ApplyScreenSpaceLod(chunk, child, projectedBlockPixels, ref hiddenTreeBatches, ref shadowDisabledBatches);
        }
    }

    private void ApplyPresentationToRebuiltRoot(ChunkCoord chunk, Node3D root)
    {
        if (!FullSurfaceRenderer || _camera?.Camera is null) return;
        root.Visible = IsChunkPresentationRelevant(chunk);
        if (!root.Visible) return;

        Vector3I min = chunk.MinVoxel(_world.Profile.ChunkSize);
        Vector3I center = min + new Vector3I(
            _world.Profile.ChunkSize / 2,
            _world.Profile.ChunkSize / 2,
            _world.Profile.ChunkSize / 2);
        int hiddenTrees = 0;
        int disabledShadows = 0;
        ApplyScreenSpaceLod(
            chunk,
            root,
            EstimateProjectedBlockPixels(VoxelToWorld(center)),
            ref hiddenTrees,
            ref disabledShadows);
    }

    internal static bool ShouldShowTreeBatch(
        float projectedBlockPixels,
        float surfaceFocusBlend,
        bool currentlyVisible,
        int detailDistance)
    {
        if (surfaceFocusBlend >= 0.42f) return true;
        float hideThreshold = detailDistance switch
        {
            0 => 16.0f,
            2 => 8.0f,
            _ => TreeLodMinimumProjectedBlockPixels,
        };
        float threshold = currentlyVisible ? hideThreshold : hideThreshold * TreeLodShowHysteresis;
        return projectedBlockPixels >= threshold;
    }

    private bool IsFullSurfaceChunkCameraFacing(ChunkCoord chunk, Vector3I centerVoxel, Vector3 toCamera)
    {
        // ponytail: close inspection keeps conservative cavity roots; add portal/occlusion tests only if
        // measured overdraw here outweighs the correctness of seeing through deep tunnels.
        if ((_camera?.SurfaceFocusBlend ?? 0.0f) >= 0.55f && HasSparseExposurePotential(chunk))
        {
            return true;
        }

        int depth = Math.Max(1, _world.Profile.DetailedSurfaceDepthChunks);
        int min = _world.MinChunkCoordinate;
        int max = _world.MaxChunkCoordinate;
        Vector3I outward = _world.Source.GetOutwardNormal(centerVoxel);
        return IsStructuralChunkCameraFacing(chunk, depth, min, max, toCamera, outward);
    }

    internal static bool IsStructuralChunkCameraFacing(
        ChunkCoord chunk,
        int depth,
        int min,
        int max,
        Vector3 toCamera,
        Vector3I interiorOutward)
    {
        bool shell = false;

        if (max - chunk.X < depth)
        {
            shell = true;
            if (toCamera.X > 0.0f) return true;
        }
        if (chunk.X - min < depth)
        {
            shell = true;
            if (toCamera.X < 0.0f) return true;
        }
        if (max - chunk.Y < depth)
        {
            shell = true;
            if (toCamera.Y > 0.0f) return true;
        }
        if (chunk.Y - min < depth)
        {
            shell = true;
            if (toCamera.Y < 0.0f) return true;
        }
        if (max - chunk.Z < depth)
        {
            shell = true;
            if (toCamera.Z > 0.0f) return true;
        }
        if (chunk.Z - min < depth)
        {
            shell = true;
            if (toCamera.Z < 0.0f) return true;
        }

        if (shell) return false;
        return toCamera.Dot((Vector3)interiorOutward) > 0.0f;
    }

    private sealed partial class VisibilityRefreshTicker : Node
    {
        private readonly WorldView _owner;

        public VisibilityRefreshTicker(WorldView owner)
        {
            _owner = owner;
        }

        public override void _Process(double delta)
        {
            _owner.TickViewDependentPresentation(delta);
        }
    }
}
