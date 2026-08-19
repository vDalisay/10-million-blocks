using System;
using Godot;

namespace TenMillionBlocks.World;

public readonly record struct VoxelRayHit(Vector3I Coordinate, Vector3I Normal, float Distance);

public static class VoxelRaycaster
{
    public static bool TryRaycast(
        VoxelWorld world,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out VoxelRayHit hit)
    {
        hit = default;
        if (direction.IsZeroApprox())
        {
            return false;
        }

        direction = direction.Normalized();
        Vector3 shiftedOrigin = origin + Vector3.One * 0.5f;

        var cell = new Vector3I(
            Mathf.FloorToInt(shiftedOrigin.X),
            Mathf.FloorToInt(shiftedOrigin.Y),
            Mathf.FloorToInt(shiftedOrigin.Z));

        var step = new Vector3I(Sign(direction.X), Sign(direction.Y), Sign(direction.Z));

        float deltaX = SafeDelta(direction.X);
        float deltaY = SafeDelta(direction.Y);
        float deltaZ = SafeDelta(direction.Z);

        float maxX = InitialBoundaryDistance(shiftedOrigin.X, cell.X, step.X, direction.X);
        float maxY = InitialBoundaryDistance(shiftedOrigin.Y, cell.Y, step.Y, direction.Y);
        float maxZ = InitialBoundaryDistance(shiftedOrigin.Z, cell.Z, step.Z, direction.Z);

        float traveled = 0.0f;
        Vector3I normal = Vector3I.Zero;

        while (traveled <= maxDistance)
        {
            if (world.HasBlock(cell))
            {
                hit = new VoxelRayHit(cell, normal, traveled);
                return true;
            }

            if (maxX <= maxY && maxX <= maxZ)
            {
                cell.X += step.X;
                traveled = maxX;
                maxX += deltaX;
                normal = new Vector3I(-step.X, 0, 0);
            }
            else if (maxY <= maxZ)
            {
                cell.Y += step.Y;
                traveled = maxY;
                maxY += deltaY;
                normal = new Vector3I(0, -step.Y, 0);
            }
            else
            {
                cell.Z += step.Z;
                traveled = maxZ;
                maxZ += deltaZ;
                normal = new Vector3I(0, 0, -step.Z);
            }
        }

        return false;
    }

    private static int Sign(float value)
        => value > 0.0f ? 1 : value < 0.0f ? -1 : 0;

    private static float SafeDelta(float component)
        => MathF.Abs(component) < 0.000001f ? float.PositiveInfinity : MathF.Abs(1.0f / component);

    private static float InitialBoundaryDistance(float origin, int cell, int step, float direction)
    {
        if (step == 0)
        {
            return float.PositiveInfinity;
        }

        float boundary = step > 0 ? cell + 1.0f : cell;
        return (boundary - origin) / direction;
    }
}
