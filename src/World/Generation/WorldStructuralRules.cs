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

        // The literal perimeter where two cube faces meet is read from several camera angles. A normal
        // dirt-sided grass block (and occasionally the first soil block beneath it) exposes a brown
        // third face there. Keep only that outer one-block border uniformly green; real inland ledges
        // still use the dirt-sided material.
        int faceBorder = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f));
        bool onOuterFaceBorder = Math.Max(Math.Abs(u), Math.Abs(v)) >= faceBorder;
        bool nearOuterSurface = radial >= Math.Max(0, faceBorder - 1);
        if (generated.Present
            && onOuterFaceBorder
            && nearOuterSurface
            && (generated.BlockId == profile.SurfaceEdgeBlock || generated.BlockId == profile.SoilBlock))
        {
            generated = new BlockSample(true, profile.SurfaceBlock, generated.Mineable);
        }

        int waterRadial = Math.Max(0, faceBorder - 1);
        bool generatedWater = IsWater(profile, generated.BlockId);
        bool nearWaterStructure = generatedWater || radial >= waterRadial - 1;
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
        // Everything farther outward in that same water column is carved away even if overlapping face
        // ownership classified it as ordinary terrain; otherwise a solid cap can sit on top of water.
        if (radial > waterRadial)
        {
            return BlockSample.Empty;
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
