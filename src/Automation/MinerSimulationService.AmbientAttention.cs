using Godot;
using TenMillionBlocks.World.Interaction;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    /// <summary>
    /// Finds an actionable stopped automation that the player is directly hovering in the normal
    /// world view. Active machines are not candidates at all, so large fleets no longer make this
    /// player-input path O(total automation count) every rendered frame.
    ///
    /// Occlusion depends on the one terrain ray under the pointer, not on which automation is tested.
    /// Resolve that terrain distance once and compare every candidate against it instead of running a
    /// voxel DDA again for every stopped miner that overlaps the cursor in screen space.
    /// </summary>
    public MinerInstance? FindVisibleStoppedMinerUnderMouse(Vector2 mousePosition, Camera3D camera)
    {
        if (_attentionMinerIds.Count == 0) return null;

        Vector3 cameraPosition = camera.GlobalPosition;
        float surfaceDistance = float.PositiveInfinity;
        float rayDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(_world, camera, mousePosition, rayDistance, out Vector3I hitVoxel))
        {
            surfaceDistance = cameraPosition.DistanceTo(_view.VoxelToWorld(hitVoxel));
        }
        float occlusionTolerance = _world.Profile.BlockSpacing * 0.9f;

        MinerInstance? best = null;
        float bestScreenDistance = float.PositiveInfinity;

        foreach (long id in _attentionMinerIds)
        {
            if (!_minersById.TryGetValue(id, out MinerInstance? miner) || !NeedsAttention(miner)) continue;
            if (!_visuals.TryGetValue(id, out Node3D? root) || !root.Visible) continue;
            Vector3 minerPosition = root.GlobalPosition;
            if (camera.IsPositionBehind(minerPosition)) continue;

            Vector2 screen = camera.UnprojectPosition(minerPosition);
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            float radius = 34.0f + DrillFootprint(definition) * 12.0f;
            float screenDistance = screen.DistanceTo(mousePosition);
            if (screenDistance > radius || screenDistance >= bestScreenDistance) continue;

            float minerDistance = cameraPosition.DistanceTo(minerPosition);
            if (surfaceDistance + occlusionTolerance < minerDistance) continue;

            best = miner;
            bestScreenDistance = screenDistance;
        }

        return best;
    }
}
