using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Core;

namespace TenMillionBlocks.World;

public static class VoxelMesher
{
    private readonly record struct Face(Vector3 Normal, Vector3[] Corners, float Shade);

    private static readonly Face[] Faces =
    [
        new(Vector3.Right,
        [
            new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f),
            new(0.5f, 0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
        ], 0.90f),
        new(Vector3.Left,
        [
            new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
            new(-0.5f, 0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f),
        ], 0.76f),
        new(Vector3.Up,
        [
            new(-0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, 0.5f),
            new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f),
        ], 1.08f),
        new(Vector3.Down,
        [
            new(-0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, -0.5f),
            new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f),
        ], 0.58f),
        new(Vector3.Back,
        [
            new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f),
            new(-0.5f, 0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f),
        ], 0.98f),
        new(Vector3.Forward,
        [
            new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
            new(0.5f, 0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
        ], 0.82f),
    ];

    private static readonly Vector3I[] NeighborOffsets =
    [
        Vector3I.Right, Vector3I.Left, Vector3I.Up,
        Vector3I.Down, Vector3I.Back, Vector3I.Forward,
    ];

    public static ArrayMesh? BuildChunk(
        IReadOnlyDictionary<Vector3I, BlockType> blocks,
        Vector3I chunkKey,
        int chunkSize)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        int faceCount = 0;
        Vector3I start = chunkKey * chunkSize;

        for (int x = start.X; x < start.X + chunkSize; x++)
        for (int y = start.Y; y < start.Y + chunkSize; y++)
        for (int z = start.Z; z < start.Z + chunkSize; z++)
        {
            var position = new Vector3I(x, y, z);
            if (!blocks.TryGetValue(position, out BlockType type))
            {
                continue;
            }

            Color baseColor = BlockPalette.Get(type).Color;

            for (int faceIndex = 0; faceIndex < Faces.Length; faceIndex++)
            {
                if (blocks.ContainsKey(position + NeighborOffsets[faceIndex]))
                {
                    continue;
                }

                AddFace(surface, position, Faces[faceIndex], Shade(baseColor, Faces[faceIndex].Shade));
                faceCount++;
            }
        }

        if (faceCount == 0)
        {
            return null;
        }

        return surface.Commit();
    }

    private static void AddFace(SurfaceTool surface, Vector3I blockPosition, Face face, Color color)
    {
        int[] triangleOrder = [0, 1, 2, 0, 2, 3];
        Vector3 origin = (Vector3)blockPosition * GameConfig.BlockSize;

        foreach (int cornerIndex in triangleOrder)
        {
            surface.SetNormal(face.Normal);
            surface.SetColor(color);
            surface.AddVertex(origin + face.Corners[cornerIndex] * GameConfig.BlockSize);
        }
    }

    private static Color Shade(Color color, float amount)
        => new(
            Mathf.Clamp(color.R * amount, 0.0f, 1.0f),
            Mathf.Clamp(color.G * amount, 0.0f, 1.0f),
            Mathf.Clamp(color.B * amount, 0.0f, 1.0f),
            color.A);
}
