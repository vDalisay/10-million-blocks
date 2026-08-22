using System;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private const double VisibilityRefreshIntervalSeconds = 0.05;
    private const float TreeLodMinimumProjectedBlockPixels = 3.5f;
    private const float TerrainShadowMinimumProjectedBlockPixels = 5.0f;
    private const float TreeShadowMinimumProjectedBlockPixels = 10.0f;

    private bool _visibilityPoseInitialized;
    private Vector3 _lastVisibilityCameraPosition;
    private Vector3 _lastVisibilityCameraForward;
    private Vector2 _lastVisibilityViewportSize;
    private float _lastVisibilityFov;
    private int _lastVisibilityChunkCount = -1;
    private double _visibilityRefreshTimer;

    public int PresentedChunkCount { get; private set; }
    public int CulledChunkCount { get; private set; }
    public int BackfaceCulledChunkCount { get; private set; }
    public int FrustumCulledChunkCount { get; private set; }
    public int LodHiddenTreeBatchCount { get; private set; }
    public int LodShadowDisabledBatchCount { get; private set; }

    /// <summary>
    /// Runs the culling policy often enough to follow camera motion without making every process tick
    /// walk the complete resident shell. The actual refresh also has a pose cache, so explicit refreshes
    /// requested by automation remain cheap while the view is stationary.
    /// </summary>
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

    /// <summary>
    /// Million-block worlds keep deterministic shell chunks resident so orbiting never causes a large
    /// regeneration spike, but resident does not mean drawable. Visibility is rejected in increasingly
    /// expensive stages: cube-face/back-side test first, then a conservative chunk-sphere frustum test,
    /// then screen-space LOD for decorative tree batches and shadows.
    ///
    /// Godot already performs GPU triangle backface/depth rejection, but doing the coarse tests here
    /// prevents entire MultiMesh batches from reaching the renderer at all. This is especially valuable
    /// during close surface inspection, where most of the resident cube shell is outside the camera.
    /// </summary>
    public void RefreshViewDependentPresentation()
    {
        if (!FullSurfaceRenderer || _camera?.Camera is null)
        {
            PresentedChunkCount = _chunkRoots.Count;
            CulledChunkCount = 0;
            BackfaceCulledChunkCount = 0;
            FrustumCulledChunkCount = 0;
            LodHiddenTreeBatchCount = 0;
            LodShadowDisabledBatchCount = 0;
            _visibilityPoseInitialized = false;
            _lastVisibilityChunkCount = _chunkRoots.Count;
            return;
        }

        Camera3D camera = _camera.Camera;
        Vector3 cameraPosition = camera.GlobalPosition;
        Vector3 cameraForward = -camera.GlobalBasis.Z.Normalized();
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        bool samePose = _visibilityPoseInitialized
            && _lastVisibilityChunkCount == _chunkRoots.Count
            && cameraPosition.DistanceSquaredTo(_lastVisibilityCameraPosition) < 0.0004f
            && cameraForward.Dot(_lastVisibilityCameraForward) > 0.999995f
            && viewportSize.DistanceSquaredTo(_lastVisibilityViewportSize) < 0.25f
            && MathF.Abs(camera.Fov - _lastVisibilityFov) < 0.001f;
        if (samePose)
        {
            return;
        }

        _visibilityPoseInitialized = true;
        _lastVisibilityCameraPosition = cameraPosition;
        _lastVisibilityCameraForward = cameraForward;
        _lastVisibilityViewportSize = viewportSize;
        _lastVisibilityFov = camera.Fov;
        _lastVisibilityChunkCount = _chunkRoots.Count;

        int presented = 0;
        int backfaceCulled = 0;
        int frustumCulled = 0;
        int hiddenTreeBatches = 0;
        int shadowDisabledBatches = 0;
        int chunkSize = _world.Profile.ChunkSize;

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

            if (!IsSphereWithinCameraFrustum(centerWorld, ChunkPresentationRadiusWorld(chunkSize)))
            {
                root.Visible = false;
                frustumCulled++;
                continue;
            }

            root.Visible = true;
            presented++;
            float projectedBlockPixels = EstimateProjectedBlockPixels(centerWorld);
            ApplyScreenSpaceLod(root, projectedBlockPixels, ref hiddenTreeBatches, ref shadowDisabledBatches);
        }

        PresentedChunkCount = presented;
        BackfaceCulledChunkCount = backfaceCulled;
        FrustumCulledChunkCount = frustumCulled;
        CulledChunkCount = backfaceCulled + frustumCulled;
        LodHiddenTreeBatchCount = hiddenTreeBatches;
        LodShadowDisabledBatchCount = shadowDisabledBatches;
    }

    /// <summary>
    /// Shared high-level visibility gate used by renderer queues and automation presentation. It is
    /// deliberately conservative: false means a chunk cannot contribute pixels to the current camera,
    /// while true may still be rejected later by normal depth testing or finer Godot culling.
    /// </summary>
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
        return IsFullSurfaceChunkCameraFacing(chunk, centerVoxel, toCamera)
            && IsSphereWithinCameraFrustum(centerWorld, ChunkPresentationRadiusWorld(chunkSize));
    }

    private float ChunkPresentationRadiusWorld(int chunkSize)
    {
        float halfExtent = chunkSize * _world.Profile.BlockSpacing * 0.5f;
        // Sphere encloses the chunk AABB. Extra padding keeps trees/grass fringes conservative so the
        // coarse CPU culler never clips a visible decorative mesh at the edge of the screen.
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

        // Radius is expanded by the slope of each plane rather than testing only the center point.
        // This intentionally errs toward rendering a borderline chunk instead of producing pop-in.
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
        Node node,
        float projectedBlockPixels,
        ref int hiddenTreeBatches,
        ref int shadowDisabledBatches)
    {
        float focus = _camera?.SurfaceFocusBlend ?? 0.0f;
        bool showTrees = projectedBlockPixels >= TreeLodMinimumProjectedBlockPixels || focus >= 0.42f;
        bool terrainShadows = projectedBlockPixels >= TerrainShadowMinimumProjectedBlockPixels || focus >= 0.30f;
        bool treeShadows = projectedBlockPixels >= TreeShadowMinimumProjectedBlockPixels || focus >= 0.58f;

        foreach (Node child in node.GetChildren())
        {
            if (child is MultiMeshInstance3D batch)
            {
                string name = batch.Name.ToString();
                bool tree = name.StartsWith("Batch_tree_", StringComparison.Ordinal);
                bool visible = !tree || showTrees;
                batch.Visible = visible;
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

            ApplyScreenSpaceLod(child, projectedBlockPixels, ref hiddenTreeBatches, ref shadowDisabledBatches);
        }
    }

    private bool IsFullSurfaceChunkCameraFacing(ChunkCoord chunk, Vector3I centerVoxel, Vector3 toCamera)
    {
        int depth = Math.Max(1, _world.Profile.DetailedSurfaceDepthChunks);
        int min = _world.MinChunkCoordinate;
        int max = _world.MaxChunkCoordinate;
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

        Vector3I outward = _world.Source.GetOutwardNormal(centerVoxel);
        return toCamera.Dot((Vector3)outward) > 0.0f;
    }
}
