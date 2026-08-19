using Godot;

namespace TenMillionBlocks.Automation;

internal static class VoxelVectorExtensions
{
    public static int Dot(this Vector3I value, Vector3I other)
        => value.X * other.X + value.Y * other.Y + value.Z * other.Z;
}
