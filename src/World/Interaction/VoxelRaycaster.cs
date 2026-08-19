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

        // Jump directly to the logical world's AABB before entering voxel DDA. This turns far-camera
        // picking from O(camera distance / block size) into O(terrain thickness) and is essential for
        // the 1000 and million-scale profiles.
        if (!TryIntersectAabb(rayOrigin, rayDirection, world.GetWorldBounds(), out float enter, out float exit))
        {
            voxel = default;
            return false;
        }

        float startWorldDistance = MathF.Max(0.0f, enter - spacing * 0.02f);
        float endWorldDistance = MathF.Min(maxWorldDistance, exit + spacing * 0.02f);
        if (endWorldDistance < startWorldDistance)
        {
            voxel = default;
            return false;
        }

        Vector3 start = rayOrigin + rayDirection * startWorldDistance;
        // Shift by half a cell so integer voxel coordinates represent cell centers.
        Vector3 gridOrigin = start / spacing + Vector3.One * 0.5f;
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
        float maxGridDistance = (endWorldDistance - startWorldDistance) / spacing;
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

    private static bool TryIntersectAabb(Vector3 origin, Vector3 direction, Aabb bounds, out float enter, out float exit)
    {
        enter = 0.0f;
        exit = float.PositiveInfinity;
        Vector3 min = bounds.Position;
        Vector3 max = bounds.End;

        if (!IntersectAxis(origin.X, direction.X, min.X, max.X, ref enter, ref exit)
            || !IntersectAxis(origin.Y, direction.Y, min.Y, max.Y, ref enter, ref exit)
            || !IntersectAxis(origin.Z, direction.Z, min.Z, max.Z, ref enter, ref exit))
        {
            enter = 0.0f;
            exit = 0.0f;
            return false;
        }

        return exit >= MathF.Max(0.0f, enter);
    }

    private static bool IntersectAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) < 0.000001f)
        {
            return origin >= min && origin <= max;
        }

        float inverse = 1.0f / direction;
        float t0 = (min - origin) * inverse;
        float t1 = (max - origin) * inverse;
        if (t0 > t1) (t0, t1) = (t1, t0);
        enter = MathF.Max(enter, t0);
        exit = MathF.Min(exit, t1);
        return enter <= exit;
    }

    private static float SafeReciprocalAbs(float value)
        => MathF.Abs(value) < 0.000001f ? float.PositiveInfinity : 1.0f / MathF.Abs(value);

    private static float SafeAxisT(float numerator, float denominator)
        => MathF.Abs(denominator) < 0.000001f ? float.PositiveInfinity : numerator / denominator;
}
