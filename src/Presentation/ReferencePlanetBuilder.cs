using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.Presentation;

public static class ReferencePlanetBuilder
{
    private const int Radius = 6;
    private const float BlockSpacing = 2.0f;

    public static Node3D Build(BlockAssetRegistry assets)
    {
        var root = new Node3D { Name = "ReferenceCubePlanet" };
        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);

        for (int x = -Radius; x <= Radius; x++)
        for (int y = -Radius; y <= Radius; y++)
        for (int z = -Radius; z <= Radius; z++)
        {
            if (!IsSolid(x, y, z) || !IsSurface(x, y, z))
            {
                continue;
            }

            string blockId = ChooseBlock(x, y, z);
            AddTransform(batches, blockId, ToTransform(x, y, z));
        }

        AddTopFeatures(batches);

        foreach ((string blockId, List<Transform3D> transforms) in batches)
        {
            if (transforms.Count == 0)
            {
                continue;
            }

            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = assets.GetMesh(blockId),
                InstanceCount = transforms.Count,
                VisibleInstanceCount = transforms.Count,
            };

            for (int i = 0; i < transforms.Count; i++)
            {
                multiMesh.SetInstanceTransform(i, transforms[i]);
            }

            var instance = new MultiMeshInstance3D
            {
                Name = $"Batch_{blockId}",
                Multimesh = multiMesh,
            };
            root.AddChild(instance);
        }

        return root;
    }

    private static void AddTopFeatures(Dictionary<string, List<Transform3D>> batches)
    {
        for (int x = -4; x <= 4; x += 2)
        for (int z = -4; z <= 4; z += 2)
        {
            int top = FindTopY(x, z);
            if (top < -Radius)
            {
                continue;
            }

            float chance = Hash01(x, 91, z);
            if (chance > 0.58f && !(x >= 2 && z <= -1))
            {
                AddTransform(batches, "tree", ToTransform(x, top + 1, z));
            }
        }

        int towerX = 3;
        int towerZ = -3;
        int towerBase = FindTopY(towerX, towerZ) + 1;
        for (int y = towerBase; y < towerBase + 4; y++)
        {
            AddTransform(batches, "brick", ToTransform(towerX, y, towerZ));
        }

        AddTransform(batches, "brick", ToTransform(towerX + 1, towerBase + 3, towerZ));
        AddTransform(batches, "brick", ToTransform(towerX - 1, towerBase + 3, towerZ));
        AddTransform(batches, "brick", ToTransform(towerX, towerBase + 3, towerZ + 1));
        AddTransform(batches, "brick", ToTransform(towerX, towerBase + 3, towerZ - 1));
    }

    private static int FindTopY(int x, int z)
    {
        for (int y = Radius; y >= -Radius; y--)
        {
            if (IsSolid(x, y, z))
            {
                return y;
            }
        }

        return -Radius - 1;
    }

    private static string ChooseBlock(int x, int y, int z)
    {
        int ax = Math.Abs(x);
        int ay = Math.Abs(y);
        int az = Math.Abs(z);
        float detail = Hash01(x, y, z);

        bool frontFace = z > 0 && az >= ax && az >= ay;
        if (frontFace)
        {
            float dx = x;
            float dy = y - 1.0f;
            float lakeRadius = MathF.Sqrt(dx * dx + dy * dy);
            if (lakeRadius < 2.55f)
            {
                return "water";
            }

            if (lakeRadius < 3.55f)
            {
                return detail > 0.75f ? "stone_dark" : "stone";
            }
        }

        bool topFace = y > 0 && ay >= ax && ay >= az;
        if (topFace)
        {
            if (detail < 0.05f)
            {
                return "sand";
            }

            return detail > 0.88f ? "dirt_grass" : "grass";
        }

        if (y > 1)
        {
            return detail > 0.72f ? "grass" : "dirt_grass";
        }

        if (y < -3)
        {
            return detail > 0.82f ? "stone" : "stone_dark";
        }

        if (detail > 0.985f)
        {
            return "gold";
        }

        if (detail > 0.955f)
        {
            return "silver";
        }

        if (detail > 0.91f)
        {
            return "copper";
        }

        return detail > 0.58f ? "stone" : "dirt";
    }

    private static bool IsSurface(int x, int y, int z)
    {
        return !IsSolid(x + 1, y, z)
            || !IsSolid(x - 1, y, z)
            || !IsSolid(x, y + 1, z)
            || !IsSolid(x, y - 1, z)
            || !IsSolid(x, y, z + 1)
            || !IsSolid(x, y, z - 1);
    }

    private static bool IsSolid(int x, int y, int z)
    {
        if (Math.Abs(x) > Radius || Math.Abs(y) > Radius || Math.Abs(z) > Radius)
        {
            return false;
        }

        float nx = Math.Abs(x) / (Radius + 0.25f);
        float ny = Math.Abs(y) / (Radius + 0.25f);
        float nz = Math.Abs(z) / (Radius + 0.25f);
        float cube = Math.Max(nx, Math.Max(ny, nz));
        float sphere = MathF.Sqrt(nx * nx + ny * ny + nz * nz) / 1.7320508f;
        float terraces = (Hash01(x, y, z) - 0.5f) * 0.10f;
        float verticalBias = y > 2 ? -0.025f * MathF.Sin((x * 1.7f) + (z * 0.8f)) : 0.0f;
        return cube * 0.77f + sphere * 0.23f + terraces + verticalBias <= 0.93f;
    }

    private static Transform3D ToTransform(int x, int y, int z)
    {
        return new Transform3D(Basis.Identity, new Vector3(x, y, z) * BlockSpacing);
    }

    private static void AddTransform(Dictionary<string, List<Transform3D>> batches, string blockId, Transform3D transform)
    {
        if (!batches.TryGetValue(blockId, out List<Transform3D>? transforms))
        {
            transforms = [];
            batches[blockId] = transforms;
        }

        transforms.Add(transform);
    }

    private static float Hash01(int x, int y, int z)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h = (h ^ (uint)z) * 16777619u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0x00ffffffu) / 16777215.0f;
        }
    }
}
