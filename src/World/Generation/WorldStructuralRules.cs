using System;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Generation;

/// <summary>
/// Final deterministic structural pass shared by gameplay, authoring metrics and generation CI.
/// ProceduralWorldSource owns the broad terrain language; this pass enforces hard visual/voxel
/// invariants that must never depend on a lucky seed: water is a single inset surface layer with a
/// solid basin immediately behind it, and the literal outer cube border never uses dirt-sided grass.
/// </summary>
public static class WorldStructuralRules
{
    public static BlockSample Apply(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I coordinate,
        BlockSample generated)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(source);

        if (profile.UsesSingleBlockGenerator || profile.UsesSolidCubeGenerator)
        {
            return generated;
        }

        Vector3I normal = DominantNormal(coordinate);
        GetFaceTangents(coordinate, normal, out int u, out int v, out int radial);

        // The visible perimeter where two cube faces meet must use the all-green surface material.
        // Coordinate-tie tests are insufficient here: relief can make the owning face radial one block
        // larger than its tangent edge, which is exactly how dirt-sided grass leaked onto corners.
        int faceBorder = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f));
        if (generated.Present
            && generated.BlockId == profile.SurfaceEdgeBlock
            && Math.Max(Math.Abs(u), Math.Abs(v)) >= faceBorder)
        {
            generated = new BlockSample(true, profile.SurfaceBlock, generated.Mineable);
        }

        int waterRadial = Math.Max(0, faceBorder - 1);
        bool generatedWater = IsWater(profile, generated.BlockId);
        bool nearWaterStructure = generatedWater || radial == waterRadial || radial == waterRadial - 1;
        if (!nearWaterStructure)
        {
            return generated;
        }

        if (!TryFindWaterColumn(profile, source, normal, u, v, waterRadial, out string waterBlockId))
        {
            return generatedWater ? new BlockSample(true, profile.SandBlock, generated.Mineable) : generated;
        }

        // Water is presentation/gameplay surface, not a tower of cubes sitting above terrain. Collapse
        // every raw hydrology column to exactly one water voxel one block inside the normal cube face.
        // The immediately inward voxel is guaranteed sand, so the water can never float or stack.
        if (radial > waterRadial)
        {
            return generatedWater ? BlockSample.Empty : generated;
        }

        if (radial == waterRadial)
        {
            return new BlockSample(true, waterBlockId, true);
        }

        if (radial == waterRadial - 1)
        {
            return new BlockSample(true, profile.SandBlock, true);
        }

        return generatedWater ? new BlockSample(true, profile.SandBlock, true) : generated;
    }

    public static bool IsWater(WorldProfile profile, string blockId)
        => blockId == profile.WaterBlock
            || blockId == profile.ShallowWaterBlock
            || blockId == profile.DeepWaterBlock;

    public static Vector3I DominantNormal(Vector3I coordinate)
    {
        int ax = Math.Abs(coordinate.X);
        int ay = Math.Abs(coordinate.Y);
        int az = Math.Abs(coordinate.Z);
        if (ax >= ay && ax >= az) return coordinate.X >= 0 ? Vector3I.Right : Vector3I.Left;
        if (ay >= ax && ay >= az) return coordinate.Y >= 0 ? Vector3I.Up : Vector3I.Down;
        return coordinate.Z >= 0 ? Vector3I.Back : Vector3I.Forward;
    }

    public static void GetFaceTangents(
        Vector3I coordinate,
        Vector3I normal,
        out int u,
        out int v,
        out int radial)
    {
        if (normal.X != 0)
        {
            u = coordinate.Y;
            v = coordinate.Z;
            radial = coordinate.X * normal.X;
            return;
        }

        if (normal.Y != 0)
        {
            u = coordinate.X;
            v = coordinate.Z;
            radial = coordinate.Y * normal.Y;
            return;
        }

        u = coordinate.X;
        v = coordinate.Y;
        radial = coordinate.Z * normal.Z;
    }

    public static Vector3I FaceVoxel(Vector3I normal, int radial, int u, int v)
    {
        if (normal == Vector3I.Right) return new Vector3I(radial, u, v);
        if (normal == Vector3I.Left) return new Vector3I(-radial, u, v);
        if (normal == Vector3I.Up) return new Vector3I(u, radial, v);
        if (normal == Vector3I.Down) return new Vector3I(u, -radial, v);
        if (normal == Vector3I.Back) return new Vector3I(u, v, radial);
        return new Vector3I(u, v, -radial);
    }

    private static bool TryFindWaterColumn(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial,
        out string waterBlockId)
    {
        waterBlockId = string.Empty;
        for (int radial = profile.MaxCoordinate; radial >= Math.Max(0, minimumRadial); radial--)
        {
            BlockSample raw = source.SampleVoxel(FaceVoxel(normal, radial, u, v));
            if (!raw.Present || !IsWater(profile, raw.BlockId)) continue;
            waterBlockId = raw.BlockId;
            return true;
        }

        return false;
    }
}
