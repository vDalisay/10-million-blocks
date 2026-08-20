using System;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    public int PresentedChunkCount { get; private set; }
    public int CulledChunkCount { get; private set; }

    /// <summary>
    /// Full-surface worlds keep deterministic shell chunks resident so orbiting never causes a large
    /// regeneration spike, but resident does not mean drawable. At most the cube faces oriented toward
    /// the camera are visible. Back-side chunk roots are disabled before Godot submits their MultiMesh
    /// instances; modified interior chunks follow their nearest outward face. This is complementary to
    /// engine frustum culling and is especially useful when a large cube fills most of the frustum.
    /// </summary>
    public void RefreshViewDependentPresentation()
    {
        if (!FullSurfaceRenderer || _camera?.Camera is null)
        {
            PresentedChunkCount = _chunkRoots.Count;
            CulledChunkCount = 0;
            return;
        }

        Vector3 cameraPosition = _camera.Camera.GlobalPosition;
        int presented = 0;
        int culled = 0;
        int chunkSize = _world.Profile.ChunkSize;

        foreach ((ChunkCoord chunk, Node3D root) in _chunkRoots)
        {
            Vector3I min = chunk.MinVoxel(chunkSize);
            Vector3I centerVoxel = min + new Vector3I(chunkSize / 2, chunkSize / 2, chunkSize / 2);
            Vector3 centerWorld = VoxelToWorld(centerVoxel);
            Vector3 toCamera = cameraPosition - centerWorld;

            bool visible = false;
            bool hadShellNormal = false;
            foreach (Vector3I normal in RelevantFullSurfaceNormals(chunk))
            {
                hadShellNormal = true;
                if (toCamera.Dot((Vector3)normal) > 0.0f)
                {
                    visible = true;
                    break;
                }
            }

            if (!hadShellNormal)
            {
                Vector3I outward = _world.Source.GetOutwardNormal(centerVoxel);
                visible = toCamera.Dot((Vector3)outward) > 0.0f;
            }

            root.Visible = visible;
            if (visible) presented++;
            else culled++;
        }

        PresentedChunkCount = presented;
        CulledChunkCount = culled;
    }
}
