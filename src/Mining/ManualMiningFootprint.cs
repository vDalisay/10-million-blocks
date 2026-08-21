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
    // Footprint shapes are immutable content, not per-hover state. Keep one template for the lifetime of
    // the process so Square10 does not allocate/build a 100-element List every time the cursor moves or
    // hover mining exposes the next layer.
    private static readonly Vector2I[] SingleOffsets = [Vector2I.Zero];
    private static readonly Vector2I[] Plus3Offsets =
    [
        Vector2I.Zero,
        Vector2I.Left,
        Vector2I.Right,
        Vector2I.Up,
        Vector2I.Down,
    ];
    private static readonly Vector2I[] Square3Offsets = BuildSquare(3);
    private static readonly Vector2I[] Square10Offsets = BuildSquare(10);

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
        Vector2I[] offsets = OffsetArray(kind);
        var result = new List<Vector3I>(offsets.Length) { hovered };
        if (kind == ManualMiningFootprintKind.Single) return result;

        Vector3I outward = LineMiningPattern.Cardinal(surfaceNormal);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);

        foreach (Vector2I offset in offsets)
        {
            if (offset == Vector2I.Zero) continue;
            Vector3I candidate = hovered + tangentA * offset.X + tangentB * offset.Y;

            // IsExposed already rejects missing voxels, so the old IsPresent + IsExposed pair sampled the
            // candidate center twice for every surrounding cell.
            if (world.IsExposed(candidate)) result.Add(candidate);
        }
        return result;
    }

    public static IReadOnlyList<Vector2I> Offsets(ManualMiningFootprintKind kind)
        => OffsetArray(kind);

    public static ManualMiningFootprintKind Parse(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "plus_3" or "plus3" => ManualMiningFootprintKind.Plus3,
            "square_3" or "square3" => ManualMiningFootprintKind.Square3,
            "square_10" or "square10" => ManualMiningFootprintKind.Square10,
            _ => ManualMiningFootprintKind.Single,
        };

    private static Vector2I[] OffsetArray(ManualMiningFootprintKind kind)
        => kind switch
        {
            ManualMiningFootprintKind.Plus3 => Plus3Offsets,
            ManualMiningFootprintKind.Square3 => Square3Offsets,
            ManualMiningFootprintKind.Square10 => Square10Offsets,
            _ => SingleOffsets,
        };

    private static Vector2I[] BuildSquare(int size)
    {
        var result = new Vector2I[size * size];
        int min = -(size / 2);
        int maxExclusive = min + size;
        int index = 0;
        for (int y = min; y < maxExclusive; y++)
        for (int x = min; x < maxExclusive; x++)
        {
            result[index++] = new Vector2I(x, y);
        }
        return result;
    }
}
