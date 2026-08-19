using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TenMillionBlocks.World;

public static class ProceduralWorldGenerator
{
    private readonly record struct Candidate(Vector3I Position, float Score);

    public static Dictionary<Vector3I, BlockType> Generate(int targetBlockCount, int seed)
    {
        if (targetBlockCount <= 0)
        {
            return [];
        }

        if (targetBlockCount == 1)
        {
            return new Dictionary<Vector3I, BlockType>
            {
                [Vector3I.Zero] = BlockType.Grass,
            };
        }

        int side = Math.Max(3, (int)Math.Ceiling(Math.Pow(targetBlockCount * 1.75, 1.0 / 3.0)));
        if ((side & 1) == 0)
        {
            side++;
        }

        int half = side / 2;
        var candidates = new List<Candidate>(side * side * side);

        for (int x = -half; x <= half; x++)
        for (int y = -half; y <= half; y++)
        for (int z = -half; z <= half; z++)
        {
            float nx = Math.Abs(x) / (half + 0.45f);
            float ny = Math.Abs(y) / (half + 0.45f);
            float nz = Math.Abs(z) / (half + 0.45f);

            float cubeDistance = Math.Max(nx, Math.Max(ny, nz));
            float sphereDistance = MathF.Sqrt(nx * nx + ny * ny + nz * nz) / 1.73205f;
            float terrainNoise = Hash01(x, y, z, seed) * 0.12f;
            float verticalBias = y * -0.0015f;

            float score = cubeDistance * 0.73f + sphereDistance * 0.27f + terrainNoise + verticalBias;
            candidates.Add(new Candidate(new Vector3I(x, y, z), score));
        }

        var chosen = candidates
            .OrderBy(candidate => candidate.Score)
            .Take(targetBlockCount)
            .Select(candidate => candidate.Position)
            .ToHashSet();

        var result = new Dictionary<Vector3I, BlockType>(targetBlockCount);
        foreach (Vector3I position in chosen)
        {
            bool topExposed = !chosen.Contains(position + Vector3I.Up);
            bool anyExposed = IsSurface(chosen, position);
            float detail = Hash01(position.X, position.Y, position.Z, seed ^ 0x4f1bbcdc);

            BlockType type;
            if (!anyExposed)
            {
                type = detail > 0.965f ? BlockType.Crystal : BlockType.Stone;
            }
            else if (topExposed)
            {
                float lowlandThreshold = -half * 0.18f;
                if (position.Y <= lowlandThreshold && detail < 0.12f)
                {
                    type = BlockType.Water;
                }
                else if (position.Y <= lowlandThreshold + 1.0f && detail < 0.32f)
                {
                    type = BlockType.Sand;
                }
                else
                {
                    type = BlockType.Grass;
                }
            }
            else
            {
                type = detail < 0.62f ? BlockType.Stone : BlockType.Dirt;
            }

            result[position] = type;
        }

        return result;
    }

    private static bool IsSurface(HashSet<Vector3I> blocks, Vector3I position)
    {
        foreach (Vector3I direction in VoxelDirections.All)
        {
            if (!blocks.Contains(position + direction))
            {
                return true;
            }
        }

        return false;
    }

    public static float Hash01(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0x85EBCA77u;
            h = (h << 11) | (h >> 21);
            h ^= (uint)z * 0xC2B2AE3Du;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215.0f;
        }
    }
}

public static class VoxelDirections
{
    public static readonly Vector3I[] All =
    [
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        Vector3I.Back,
        Vector3I.Forward,
    ];
}
