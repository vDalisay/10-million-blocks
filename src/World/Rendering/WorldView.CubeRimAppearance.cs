using System;
using Godot;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Stable presentation mapping for procedural terrain. Chunks receive their final visual IDs while
/// they are built, so mining-driven rebuilds cannot change neighbouring block textures.
/// </summary>
public partial class WorldView
{
    private const string OuterRimVisualBlockId = "grass_outer";

    public string ResolveSurfaceVisualBlockId(Vector3I voxel, string blockId)
    {
        if (_world is null
            || _world.Profile.UsesSingleBlockGenerator
            || _world.Profile.UsesSolidCubeGenerator)
        {
            return blockId;
        }

        return ResolveTerrainVisualBlockId(
            blockId,
            _world.Profile.SurfaceBlock,
            _world.Profile.SurfaceEdgeBlock,
            _world.Profile.SoilBlock,
            blockId == _world.Profile.SoilBlock && IsOuterCubeFaceRim(voxel));
    }

    public Basis ResolveSurfaceVisualBasis(Vector3I voxel, string visualBlockId)
        => ShouldOrientToCubeFace(visualBlockId)
            ? BasisForNormal(_world.Source.GetOutwardNormal(voxel))
            : Basis.Identity;

    internal static string ResolveTerrainVisualBlockId(
        string blockId,
        string surfaceBlockId,
        string surfaceEdgeBlockId,
        string soilBlockId,
        bool outerRim)
        => blockId == soilBlockId && outerRim
            ? OuterRimVisualBlockId
            : blockId == surfaceBlockId ? surfaceEdgeBlockId : blockId;

    private bool IsOuterCubeFaceRim(Vector3I voxel)
    {
        Vector3I normal = _world.Source.GetOutwardNormal(voxel);
        int tangentA;
        int tangentB;
        if (Math.Abs(normal.X) == 1)
        {
            tangentA = Math.Abs(voxel.Y);
            tangentB = Math.Abs(voxel.Z);
        }
        else if (Math.Abs(normal.Y) == 1)
        {
            tangentA = Math.Abs(voxel.X);
            tangentB = Math.Abs(voxel.Z);
        }
        else
        {
            tangentA = Math.Abs(voxel.X);
            tangentB = Math.Abs(voxel.Y);
        }

        int faceBorder = Math.Max(1, Mathf.FloorToInt(_world.Profile.BaseRadius + 0.001f));
        return Math.Max(tangentA, tangentB) >= faceBorder;
    }
}
