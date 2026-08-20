using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Automation.MiningPatterns;

public interface IMiningPattern
{
    string Id { get; }
    IEnumerable<Vector3I> Enumerate(Vector3I origin, Vector3I direction, int range, int width = 1);
}

public sealed class LineMiningPattern : IMiningPattern
{
    public string Id => "line";

    public IEnumerable<Vector3I> Enumerate(Vector3I origin, Vector3I direction, int range, int width = 1)
    {
        _ = width;
        Vector3I step = Cardinal(direction);
        for (int i = 0; i < range; i++)
        {
            yield return origin + step * i;
        }
    }

    internal static Vector3I Cardinal(Vector3I direction)
    {
        if (direction == Vector3I.Zero) return Vector3I.Down;
        int ax = Math.Abs(direction.X);
        int ay = Math.Abs(direction.Y);
        int az = Math.Abs(direction.Z);
        if (ax >= ay && ax >= az) return direction.X >= 0 ? Vector3I.Right : Vector3I.Left;
        if (ay >= ax && ay >= az) return direction.Y >= 0 ? Vector3I.Up : Vector3I.Down;
        return direction.Z >= 0 ? Vector3I.Back : Vector3I.Forward;
    }

    internal static (Vector3I A, Vector3I B) PerpendicularAxes(Vector3I forward)
    {
        if (Math.Abs(forward.Y) == 1) return (Vector3I.Right, Vector3I.Back);
        if (Math.Abs(forward.X) == 1) return (Vector3I.Up, Vector3I.Back);
        return (Vector3I.Right, Vector3I.Up);
    }
}

public sealed class WideLineMiningPattern : IMiningPattern
{
    public string Id => "wide_line";

    public IEnumerable<Vector3I> Enumerate(Vector3I origin, Vector3I direction, int range, int width = 3)
    {
        Vector3I forward = LineMiningPattern.Cardinal(direction);
        (Vector3I sideA, Vector3I sideB) = LineMiningPattern.PerpendicularAxes(forward);
        int radius = Math.Max(0, width / 2);

        for (int depth = 0; depth < range; depth++)
        for (int a = -radius; a <= radius; a++)
        for (int b = -radius; b <= radius; b++)
        {
            yield return origin + forward * depth + sideA * a + sideB * b;
        }
    }
}

/// <summary>
/// Pure tangential address pattern retained for data-driven surface tools and future broad-strip
/// automations. The Powered Shovel now layers a topology-aware crawler policy on top of its tool class:
/// it chooses exposed neighboring terrain dynamically so it can follow relief on every cube face.
/// </summary>
public sealed class SurfaceStripMiningPattern : IMiningPattern
{
    public string Id => "surface_strip";

    public IEnumerable<Vector3I> Enumerate(Vector3I origin, Vector3I direction, int range, int width = 3)
    {
        Vector3I inward = LineMiningPattern.Cardinal(direction);
        (Vector3I along, Vector3I across) = LineMiningPattern.PerpendicularAxes(inward);
        int radius = Math.Max(0, width / 2);

        for (int step = 0; step < range; step++)
        {
            if ((step & 1) == 0)
            {
                for (int offset = -radius; offset <= radius; offset++)
                    yield return origin + along * step + across * offset;
            }
            else
            {
                for (int offset = radius; offset >= -radius; offset--)
                    yield return origin + along * step + across * offset;
            }
        }
    }
}

public sealed class MiningPatternRegistry
{
    private readonly Dictionary<string, IMiningPattern> _patterns = new(StringComparer.Ordinal)
    {
        ["line"] = new LineMiningPattern(),
        ["wide_line"] = new WideLineMiningPattern(),
        ["surface_strip"] = new SurfaceStripMiningPattern(),
    };

    public IMiningPattern Get(string id)
    {
        if (!_patterns.TryGetValue(id, out IMiningPattern? pattern))
        {
            throw new KeyNotFoundException($"Unknown mining pattern '{id}'.");
        }

        return pattern;
    }

    public bool Contains(string id) => _patterns.ContainsKey(id);
}
