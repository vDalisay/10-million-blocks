using System;
using Godot;

namespace TenMillionBlocks.World.Interaction;

public static class VoxelRaycaster
{
    public static bool TryRaycast(
        VirtualWorld world,
        Camera3D camera,
        Vector2 screenPosition,
        float maxWorldDistance,
        out Vector3I voxel)
    {
        Vector3 rayOrigin = camera.ProjectRayOrigin(screenPosition);
        Vector3 rayDirection = camera.ProjectRayNormal(screenPosition).Normalized();
        float spacing = world.Profile.BlockSpacing;

        // Shift by half a cell so integer voxel coordinates represent cell centers.
        Vector3 gridOrigin = rayOrigin / spacing + Vector3.One * 0.5f;
        Vector3 direction = rayDirection;
        Vector3I cell = new(
            Mathf.FloorToInt(gridOrigin.X),
            Mathf.FloorToInt(gridOrigin.Y),
            Mathf.FloorToInt(gridOrigin.Z));

        int stepX = direction.X >= 0.0f ? 1 : -1;
        int stepY = direction.Y >= 0.0f ? 1 : -1;
        int stepZ = direction.Z >= 0.0f ? 1 : -1;

        float tDeltaX = SafeReciprocalAbs(direction.X);
        float tDeltaY = SafeReciprocalAbs(direction.Y);
        float tDeltaZ = SafeReciprocalAbs(direction.Z);

        float nextX = stepX > 0 ? cell.X + 1.0f : cell.X;
        float nextY = stepY > 0 ? cell.Y + 1.0f : cell.Y;
        float nextZ = stepZ > 0 ? cell.Z + 1.0f : cell.Z;

        float tMaxX = SafeAxisT(nextX - gridOrigin.X, direction.X);
        float tMaxY = SafeAxisT(nextY - gridOrigin.Y, direction.Y);
        float tMaxZ = SafeAxisT(nextZ - gridOrigin.Z, direction.Z);
        float maxGridDistance = maxWorldDistance / spacing;
        float travelled = 0.0f;

        while (travelled <= maxGridDistance)
        {
            if (world.SampleVoxel(cell).Present)
            {
                voxel = cell;
                return true;
            }

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                cell.X += stepX;
                travelled = tMaxX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                cell.Y += stepY;
                travelled = tMaxY;
                tMaxY += tDeltaY;
            }
            else
            {
                cell.Z += stepZ;
                travelled = tMaxZ;
                tMaxZ += tDeltaZ;
            }
        }

        voxel = default;
        return false;
    }

    private static float SafeReciprocalAbs(float value)
        => MathF.Abs(value) < 0.000001f ? float.PositiveInfinity : 1.0f / MathF.Abs(value);

    private static float SafeAxisT(float numerator, float denominator)
        => MathF.Abs(denominator) < 0.000001f ? float.PositiveInfinity : numerator / denominator;
}
