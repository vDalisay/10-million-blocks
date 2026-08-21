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
/// Produces the logical footprint for one manual mining tick.
/// </summary>
public static class ManualMiningFootprint
{
    /// <summary>
    /// Expands one authoritative raycast hit across the face that the cursor entered. Camera angle can
    /// never move the surrounding cells away from the hovered centre because no secondary rays are cast.
    /// </summary>
    public static IReadOnlyList<Vector3I> ResolveFromCenter(
        VirtualWorld world,
        Vector3I hovered,
        ManualMiningFootprintKind kind,
        Vector3I surfaceNormal)
    {
        var result = new List<Vector3I>();
        result.Add(hovered);
        if (kind == ManualMiningFootprintKind.Single) return result;

        Vector3I outward = LineMiningPattern.Cardinal(surfaceNormal);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);

        foreach (Vector2I offset in Offsets(kind))
        {
            if (offset == Vector2I.Zero) continue;
            Vector3I candidate = hovered + tangentA * offset.X + tangentB * offset.Y;
            if (world.IsPresent(candidate) && world.IsExposed(candidate)) result.Add(candidate);
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
}
