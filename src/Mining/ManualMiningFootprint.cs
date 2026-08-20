using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Mining;

public enum ManualMiningFootprintKind
{
    Single,
    Plus3,
    Square3,
    Square10,
}

/// <summary>
/// Produces the logical footprint for one manual mining tick. Area mining obeys a global front-most
/// layer rule: if one selected column contains a protruding block, only blocks on that highest outward
/// layer receive damage until that obstruction is gone. This matches the intended "rock above a grass
/// plane" behavior and prevents an area upgrade from tunneling lower layers beside surviving blockers.
/// </summary>
public static class ManualMiningFootprint
{
    public static IReadOnlyList<Vector3I> ResolveHighestLayer(
        VirtualWorld world,
        Vector3I hovered,
        ManualMiningFootprintKind kind)
    {
        Vector3I outward = LineMiningPattern.Cardinal(world.Source.GetOutwardNormal(hovered));
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

            // Search outward first, then inward. We only accept exposed voxels that belong to the
            // same cube face, so a footprint near an edge does not wrap around a corner unexpectedly.
            for (int radialOffset = scan; radialOffset >= -scan; radialOffset--)
            {
                Vector3I candidate = columnBase + outward * radialOffset;
                if (!world.IsPresent(candidate) || !world.IsExposed(candidate)) continue;
                if (world.Source.GetOutwardNormal(candidate) != outward) continue;

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
        // Even-sized footprints are biased one cell toward the positive axes. Square10 is intentionally
        // deferred from the tutorial pass, but defining it here keeps the strategy/data contract stable.
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
