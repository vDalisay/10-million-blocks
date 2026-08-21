using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;

namespace TenMillionBlocks.Mining;

public enum ManualMiningFootprintKind
{
    Single,
    Plus3,
    Square3,
    Square10,
}

/// <summary>
/// Produces the logical footprint for one manual mining tick.
/// </summary>
public static class ManualMiningFootprint
{
    /// <summary>
    /// Resolves an area-mining footprint in screen space. The hovered voxel is always the centre target;
    /// every surrounding footprint cell is found by casting another camera ray one projected block-width
    /// away from the cursor. This makes an oblique/corner view behave like the player sees it instead of
    /// rotating the footprint onto one of the world's cardinal axes.
    /// </summary>
    public static IReadOnlyList<Vector3I> ResolveScreenSpace(
        VirtualWorld world,
        Camera3D camera,
        Vector2 screenPosition,
        float maxWorldDistance,
        Vector3I hovered,
        ManualMiningFootprintKind kind)
    {
        var result = new List<Vector3I>();
        var seen = new HashSet<Vector3I>();

        // The cursor ray is authoritative and is always first. Besides keeping the preview centred on
        // the block under the cursor, this also gives unstable-block effects deterministic priority.
        seen.Add(hovered);
        result.Add(hovered);
        if (kind == ManualMiningFootprintKind.Single)
        {
            return result;
        }

        float spacing = world.Profile.BlockSpacing;
        Vector3 centerWorld = (Vector3)hovered * spacing;
        Vector2 projectedCenter = camera.UnprojectPosition(centerWorld);
        Basis cameraBasis = camera.GlobalTransform.Basis;

        // Camera-local right/up vectors have equal depth to the hovered centre, so their projection is
        // an inexpensive estimate of one visible block-width at the cursor's current depth.
        Vector2 rightStep = camera.UnprojectPosition(
            centerWorld + cameraBasis.X.Normalized() * spacing) - projectedCenter;
        Vector2 upStep = camera.UnprojectPosition(
            centerWorld + cameraBasis.Y.Normalized() * spacing) - projectedCenter;
        Vector2 downStep = -upStep;

        // Extremely distant worlds can project a cell to sub-pixel size. Keep the footprint stable and
        // ray-based rather than collapsing every sample onto the same cursor pixel.
        if (rightStep.LengthSquared() < 0.25f)
        {
            rightStep = Vector2.Right;
        }
        if (downStep.LengthSquared() < 0.25f)
        {
            downStep = Vector2.Down;
        }

        foreach (Vector2I offset in Offsets(kind))
        {
            if (offset == Vector2I.Zero) continue;

            Vector2 samplePosition = screenPosition
                + rightStep * offset.X
                + downStep * offset.Y;
            if (!VoxelRaycaster.TryRaycast(
                    world,
                    camera,
                    samplePosition,
                    maxWorldDistance,
                    out Vector3I candidate))
            {
                continue;
            }

            if (seen.Add(candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// Legacy cardinal-plane resolver retained for automation/debug callers. Manual and hover mining use
    /// ResolveScreenSpace so their preview and destruction footprint follow the camera view.
    /// </summary>
    public static IReadOnlyList<Vector3I> ResolveHighestLayer(
        VirtualWorld world,
        Vector3I hovered,
        ManualMiningFootprintKind kind,
        Vector3I viewNormal)
    {
        Vector3I outward = LineMiningPattern.Cardinal(viewNormal);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);
        IReadOnlyList<Vector2I> offsets = Offsets(kind);

        int centerLayer = Dot(hovered, outward);
        int scan = kind == ManualMiningFootprintKind.Square10 ? 12 : 5;
        int highestLayer = int.MinValue;
        var candidates = new List<(Vector3I Voxel, int Layer)>(offsets.Count);

        foreach (Vector2I offset in offsets)
        {
            Vector3I columnBase = hovered + tangentA * offset.X + tangentB * offset.Y;
            Vector3I? found = null;
            int foundLayer = int.MinValue;

            for (int radialOffset = scan; radialOffset >= -scan; radialOffset--)
            {
                Vector3I candidate = columnBase + outward * radialOffset;
                if (!world.IsPresent(candidate) || !world.IsExposed(candidate)) continue;

                int layer = Dot(candidate, outward);
                if (layer < centerLayer - scan || layer > centerLayer + scan) continue;
                found = candidate;
                foundLayer = layer;
                break;
            }

            if (found is not Vector3I voxel) continue;
            candidates.Add((voxel, foundLayer));
            highestLayer = Math.Max(highestLayer, foundLayer);
        }

        if (highestLayer == int.MinValue) return Array.Empty<Vector3I>();

        var result = new List<Vector3I>(candidates.Count);
        foreach ((Vector3I voxel, int layer) in candidates)
        {
            if (layer == highestLayer) result.Add(voxel);
        }
        return result;
    }

    public static IReadOnlyList<Vector2I> Offsets(ManualMiningFootprintKind kind)
        => kind switch
        {
            ManualMiningFootprintKind.Single => new[] { Vector2I.Zero },
            ManualMiningFootprintKind.Plus3 => new[]
            {
                Vector2I.Zero,
                Vector2I.Left,
                Vector2I.Right,
                Vector2I.Up,
                Vector2I.Down,
            },
            ManualMiningFootprintKind.Square3 => BuildSquare(3),
            ManualMiningFootprintKind.Square10 => BuildSquare(10),
            _ => new[] { Vector2I.Zero },
        };

    public static ManualMiningFootprintKind Parse(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "plus_3" or "plus3" => ManualMiningFootprintKind.Plus3,
            "square_3" or "square3" => ManualMiningFootprintKind.Square3,
            "square_10" or "square10" => ManualMiningFootprintKind.Square10,
            _ => ManualMiningFootprintKind.Single,
        };

    private static IReadOnlyList<Vector2I> BuildSquare(int size)
    {
        var result = new List<Vector2I>(size * size);
        int min = -(size / 2);
        int maxExclusive = min + size;
        for (int y = min; y < maxExclusive; y++)
        for (int x = min; x < maxExclusive; x++)
        {
            result.Add(new Vector2I(x, y));
        }
        return result;
    }

    private static int Dot(Vector3I a, Vector3I b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
