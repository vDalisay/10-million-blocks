using System;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Generation;

/// <summary>
/// Final deterministic structural pass shared by gameplay, authoring metrics and generation CI.
/// ProceduralWorldSource owns the broad terrain language; this pass enforces hard visual/voxel
/// invariants that must never depend on a lucky seed: water is a single inset surface layer with a
/// solid basin immediately behind it, and every visible shoreline is sand.
/// </summary>
public static class WorldStructuralRules
{
    private static readonly Vector3I[] FaceNormals =
    [
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        Vector3I.Back,
        Vector3I.Forward,
    ];

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

        int faceBorder = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f));
        int waterRadial = Math.Max(0, faceBorder - 1);
        bool generatedWater = IsWater(profile, generated.BlockId);

        // Check every plausible owning face instead of only the dominant normal of this voxel. The
        // water surface can be owned by +Z while its immediately inward support voxel sits on an XYZ
        // magnitude tie. Dominant-normal-only processing left that support as dirt even though it is
        // part of the same basin column.
        foreach (Vector3I normal in FaceNormals)
        {
            GetFaceTangents(coordinate, normal, out int u, out int v, out int radial);
            if (radial < 0) continue;

            bool nearWaterStructure = generatedWater || radial >= waterRadial - 1;
            if (!nearWaterStructure) continue;
            if (!TryFindWaterColumn(profile, source, normal, u, v, waterRadial, out string waterBlockId)) continue;

            // Water is presentation/gameplay surface, not a tower of cubes sitting above terrain.
            // Collapse the raw hydrology column to exactly one water voxel one block inside the normal
            // cube face, carve every cap above it, and guarantee sand immediately behind it.
            if (radial > waterRadial) return BlockSample.Empty;
            if (radial == waterRadial)
            {
                return new BlockSample(
                    true,
                    ResolveStructuralWaterMaterial(profile, source, normal, u, v, waterRadial, waterBlockId),
                    true);
            }
            if (radial == waterRadial - 1) return new BlockSample(true, profile.SandBlock, true);
            if (generatedWater) return new BlockSample(true, profile.SandBlock, true);
        }

        if (generatedWater)
        {
            // A raw water classification that belongs to no accepted structural basin is invalid as
            // visible water. Fill it with sand rather than leaving a detached/stacked water voxel.
            generated = new BlockSample(true, profile.SandBlock, generated.Mineable);
        }

        // Every dry surface cell directly beside an accepted water column is an authored shoreline.
        // Make that immediate ring sand unconditionally. This is deliberately stronger than a noise
        // threshold: the player should never see an inset lake touching grass/soil/stone on its first
        // cardinal ring simply because the neighbouring terrain sample crossed a biome threshold.
        if (generated.Present && !IsWater(profile, generated.BlockId))
        {
            foreach (Vector3I normal in FaceNormals)
            {
                GetFaceTangents(coordinate, normal, out int u, out int v, out int radial);
                if (radial < waterRadial + 1) continue;
                if (!HasAdjacentWaterColumn(profile, source, normal, u, v, waterRadial)) continue;
                return new BlockSample(true, profile.SandBlock, generated.Mineable);
            }
        }

        return generated;
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

    private static string ResolveStructuralWaterMaterial(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial,
        string rawBlockId)
    {
        // Dark water is only meaningful in the visual middle of a basin. The raw terrain classifier
        // cannot know that the structural pass may reject an adjacent edge/seam water column, so check
        // the final accepted four-neighbour topology again here. Any boundary cell is always shallow.
        bool interior = TryFindWaterColumn(profile, source, normal, u + 1, v, minimumRadial, out _)
            && TryFindWaterColumn(profile, source, normal, u - 1, v, minimumRadial, out _)
            && TryFindWaterColumn(profile, source, normal, u, v + 1, minimumRadial, out _)
            && TryFindWaterColumn(profile, source, normal, u, v - 1, minimumRadial, out _);

        if (!interior) return profile.ShallowWaterBlock;
        return rawBlockId;
    }

    private static bool HasAdjacentWaterColumn(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial)
        => TryFindWaterColumn(profile, source, normal, u + 1, v, minimumRadial, out _)
            || TryFindWaterColumn(profile, source, normal, u - 1, v, minimumRadial, out _)
            || TryFindWaterColumn(profile, source, normal, u, v + 1, minimumRadial, out _)
            || TryFindWaterColumn(profile, source, normal, u, v - 1, minimumRadial, out _);

    private static bool TryFindWaterColumn(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial,
        out string waterBlockId)
    {
        if (!TryFindRawWaterColumn(profile, source, normal, u, v, minimumRadial, out waterBlockId))
        {
            return false;
        }

        // Re-apply the Minecraft-like broad-patch rule *after* structural edge/face ownership filters.
        // Trimming an otherwise coherent raw lake near a face boundary must not leave a one- or two-cell
        // water filament. Every accepted cell must belong to at least one accepted 2x2 basin square.
        bool broadPatch =
            (HasRawWater(profile, source, normal, u + 1, v, minimumRadial)
                && HasRawWater(profile, source, normal, u, v + 1, minimumRadial)
                && HasRawWater(profile, source, normal, u + 1, v + 1, minimumRadial))
            || (HasRawWater(profile, source, normal, u - 1, v, minimumRadial)
                && HasRawWater(profile, source, normal, u, v + 1, minimumRadial)
                && HasRawWater(profile, source, normal, u - 1, v + 1, minimumRadial))
            || (HasRawWater(profile, source, normal, u + 1, v, minimumRadial)
                && HasRawWater(profile, source, normal, u, v - 1, minimumRadial)
                && HasRawWater(profile, source, normal, u + 1, v - 1, minimumRadial))
            || (HasRawWater(profile, source, normal, u - 1, v, minimumRadial)
                && HasRawWater(profile, source, normal, u, v - 1, minimumRadial)
                && HasRawWater(profile, source, normal, u - 1, v - 1, minimumRadial));

        return broadPatch;
    }

    private static bool HasRawWater(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial)
        => TryFindRawWaterColumn(profile, source, normal, u, v, minimumRadial, out _);

    private static bool TryFindRawWaterColumn(
        WorldProfile profile,
        ProceduralWorldSource source,
        Vector3I normal,
        int u,
        int v,
        int minimumRadial,
        out string waterBlockId)
    {
        waterBlockId = string.Empty;

        // Reserve one complete dry/sand ring before the literal cube-face border. This means neither
        // water nor its first shoreline cell can ever consume the edge/corner line that must remain
        // visually uniform from both adjoining faces.
        int faceBorder = Math.Max(0, Mathf.FloorToInt(profile.BaseRadius + 0.001f));
        int maximumWaterTangent = Math.Max(0, faceBorder - 2);
        if (Math.Max(Math.Abs(u), Math.Abs(v)) > maximumWaterTangent) return false;

        for (int radial = profile.MaxCoordinate; radial >= Math.Max(0, minimumRadial); radial--)
        {
            Vector3I rawVoxel = FaceVoxel(normal, radial, u, v);
            BlockSample raw = source.SampleVoxel(rawVoxel);
            if (!raw.Present || !IsWater(profile, raw.BlockId)) continue;

            // Reject water borrowed from an overlapping neighbouring face. Water is deliberately kept
            // away from face seams, so a valid basin column has a stable owning outward normal.
            if (source.GetOutwardNormal(rawVoxel) != normal) continue;

            waterBlockId = raw.BlockId;
            return true;
        }

        return false;
    }
}
