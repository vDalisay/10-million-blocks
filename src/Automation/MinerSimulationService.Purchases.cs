using Godot;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    public MinerDefinition GetDefinition(string minerId) => _catalog.Get(minerId);

    /// <summary>
    /// Purchases one physical automation instance at the miner class's fixed unit price. The class
    /// unlock remains player-bound in SkillTreeService; this unit remains world-bound in the miner
    /// snapshot. Payment happens only after the player accepts a valid placement preview.
    /// </summary>
    public MinerInstance? PurchaseAndPlaceMiner(string minerId, Vector3I surfaceVoxel)
    {
        if (!_skills.IsMinerUnlocked(minerId)) return null;

        MinerDefinition definition = _catalog.Get(minerId);
        if (!_mining.TrySpend(definition.UnitPrice)) return null;

        MinerInstance? placed = PlaceMiner(minerId, surfaceVoxel);
        if (placed is not null) return placed;

        // Placement validity can theoretically change between preview and commit. Keep the purchase
        // transactional: a rejected commit restores the exact fixed unit price.
        _mining.GrantCurrency(definition.UnitPrice);
        return null;
    }
}
