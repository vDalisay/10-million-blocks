using Godot;
using TenMillionBlocks.World.Interaction;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    /// <summary>
    /// Finds an actionable stopped automation that the player is directly hovering in the normal
    /// world view. Unlike the explicit attention-cycle flow, this path is deliberately visibility
    /// gated: it is for machines the player notices themselves, not for locating buried machines.
    /// </summary>
    public MinerInstance? FindVisibleStoppedMinerUnderMouse(Vector2 mousePosition, Camera3D camera)
    {
        MinerInstance? best = null;
        float bestScreenDistance = float.PositiveInfinity;

        foreach (MinerInstance miner in _miners)
        {
            if (!NeedsAttention(miner)) continue;
            if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root) || !root.Visible) continue;
            if (camera.IsPositionBehind(root.GlobalPosition)) continue;

            Vector2 screen = camera.UnprojectPosition(root.GlobalPosition);
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            float radius = 34.0f + DrillFootprint(definition) * 12.0f;
            float screenDistance = screen.DistanceTo(mousePosition);
            if (screenDistance > radius || screenDistance >= bestScreenDistance) continue;
            if (IsAmbientAttentionOccluded(mousePosition, camera, root.GlobalPosition)) continue;

            best = miner;
            bestScreenDistance = screenDistance;
        }

        return best;
    }

    private bool IsAmbientAttentionOccluded(Vector2 mousePosition, Camera3D camera, Vector3 minerPosition)
    {
        float rayDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (!VoxelRaycaster.TryRaycast(_world, camera, mousePosition, rayDistance, out Vector3I hitVoxel))
        {
            return false;
        }

        Vector3 cameraPosition = camera.GlobalPosition;
        float surfaceDistance = cameraPosition.DistanceTo(_view.VoxelToWorld(hitVoxel));
        float minerDistance = cameraPosition.DistanceTo(minerPosition);

        // The automation model sits slightly outward from its anchor block, so allow one block of
        // tolerance. A genuinely intervening surface remains significantly closer than the machine.
        return surfaceDistance + _world.Profile.BlockSpacing * 0.9f < minerDistance;
    }
}
