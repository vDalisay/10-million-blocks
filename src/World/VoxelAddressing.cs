using System;
using Godot;

namespace TenMillionBlocks.World;

public readonly record struct ChunkCoord(int X, int Y, int Z)
{
    public static ChunkCoord FromVoxel(Vector3I voxel, int chunkSize)
        => new(
            VoxelMath.FloorDiv(voxel.X, chunkSize),
            VoxelMath.FloorDiv(voxel.Y, chunkSize),
            VoxelMath.FloorDiv(voxel.Z, chunkSize));

    public Vector3I MinVoxel(int chunkSize) => new(X * chunkSize, Y * chunkSize, Z * chunkSize);
}

public readonly record struct RegionCoord(int X, int Y, int Z)
{
    public static RegionCoord FromChunk(ChunkCoord chunk, int regionSizeInChunks)
        => new(
            VoxelMath.FloorDiv(chunk.X, regionSizeInChunks),
            VoxelMath.FloorDiv(chunk.Y, regionSizeInChunks),
            VoxelMath.FloorDiv(chunk.Z, regionSizeInChunks));
}

public static class VoxelMath
{
    public static readonly Vector3I[] Neighbors =
    {
        Vector3I.Right,
        Vector3I.Left,
        Vector3I.Up,
        Vector3I.Down,
        Vector3I.Back,
        Vector3I.Forward,
    };

    public static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor));
        }

        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public static int PositiveMod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    public static int LocalIndex(Vector3I voxel, int chunkSize)
    {
        int x = PositiveMod(voxel.X, chunkSize);
        int y = PositiveMod(voxel.Y, chunkSize);
        int z = PositiveMod(voxel.Z, chunkSize);
        return x + chunkSize * (y + chunkSize * z);
    }
}
