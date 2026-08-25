using System;
using System.Collections.Generic;
using System.Linq;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Skills;

public sealed class SkillDerivedStats
{
    // Legacy count remains for compatibility with existing saves/data while progression moves manual
    // area mining onto explicit footprint strategies.
    public int ManualBlocksPerClick { get; internal set; } = 1;
    public ManualMiningFootprintKind ManualFootprint { get; internal set; } = ManualMiningFootprintKind.Single;
    public bool HoverMiningUnlocked { get; internal set; }
    public double ManualMiningRateMultiplier { get; internal set; } = 1.0;

    // Manual power is the voxel-world analogue of incremental-game damage. Hardness is already authored
    // on every block, so this lets harder content slow the player until the corresponding power node is
    // reached instead of making speed/footprint the only meaningful throughput stats.
    public double ManualMiningPower { get; internal set; } = 1.0;

    // Penetration is deliberately reserved for a late, expensive payoff. A value of two means a timed
    // manual action may continue one freshly exposed voxel inward after clearing its surface target.
    public int ManualPenetrationDepth { get; internal set; } = 1;

    // Incremental economy axes deliberately mirror the satisfying payout/golden/critical upgrade
    // vocabulary common to the reference game, but operate on our authored block rewards.
    public double ResourceYieldMultiplier { get; internal set; } = 1.0;
    public double PreciousResourceYieldMultiplier { get; internal set; } = 1.0;
    public double CriticalYieldChance { get; internal set; }
    public double CriticalYieldMultiplier { get; internal set; } = 2.0;

    public double MinerRateMultiplier { get; internal set; } = 1.0;
    public int MinerPatternWidth { get; internal set; } = 1;
    public string DrillPatternId { get; internal set; } = "line";

    // Tier 0 = basic stone only. Later drill-bit skills widen the material vocabulary without making
    // the drill silently skip blockers it does not yet understand.
    public int DrillMaterialTier { get; internal set; } = 0;

    // Powered Shovel deliberately starts primitive: one soft-terrain tile per second, adjacent cardinal
    // tiles only, and no ability to climb/drop. Separate skills make it faster and smarter.
    public double ShovelRateMultiplier { get; internal set; } = 1.0;
    public int ShovelHeightTolerance { get; internal set; } = 0;
    public int ShovelSearchRadius { get; internal set; } = 1;

    // Late event upgrades are the voxel equivalents of electricity-chain and supernova-radius upgrades:
    // the authored cloud/meteor mechanics stay the same while their reach grows through the tree.
    public bool AutoCloudChargerUnlocked { get; internal set; }
    public int LightningRadiusBonus { get; internal set; }
    public int LightningChainCount { get; internal set; }
    public int MeteorRadiusBonus { get; internal set; }

    public HashSet<string> UnlockedMiners { get; } = new(StringComparer.Ordinal);
    public HashSet<string> UnlockedPatterns { get; } = new(StringComparer.Ordinal) { "line" };
    public HashSet<string> ResourceFilters { get; } = new(StringComparer.Ordinal);
}

public enum SkillPurchaseFailure
{
    None,
    UnknownSkill,
    MaxRank,
    MissingPrerequisite,
    InsufficientResources,
    InsufficientSpecialResources,
    CommitRejected,
}

public readonly record struct SkillPurchaseResult(
    bool Success,
    string SkillId,
    int NewRank,
    SkillPurchaseFailure Failure);

public sealed class SkillTreeService
{
    private readonly SkillTreeCatalog _catalog;
    private readonly MiningService _mining;
    private readonly SpecialResourceInventory _specialResources;
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);

    public SkillTreeService(SkillTreeCatalog catalog, MiningService mining)
        : this(catalog, mining, mining.SpecialResources)
    {
    }

    public SkillTreeService(
        SkillTreeCatalog catalog,
        MiningService mining,
        SpecialResourceInventory specialResources)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));
        Derived = new SkillDerivedStats();
        RebuildDerivedStats();
    }

    public event Action? Changed;

    public SkillTreeCatalog Catalog => _catalog;
    public SkillDerivedStats Derived { get; private set; }
    public IReadOnlyDictionary<string, int> Ranks => _ranks;
    public SpecialResourceInventory SpecialResources => _specialResources;

    public int GetRank(string skillId) => _ranks.GetValueOrDefault(skillId);

    public bool PrerequisitesMet(SkillNodeDefinition node)
        => node.Prerequisites.All(prerequisite => GetRank(prerequisite.NodeId) >= prerequisite.RequiredRank);

    public bool IsRevealed(SkillNodeDefinition node)
        => !node.HideUntilPrerequisitesMet
            || node.Prerequisites.Count == 0
            || GetRank(node.Id) > 0
            || PrerequisitesMet(node);

    public bool SpecialCostsAffordable(SkillNodeDefinition node)
        => node.SpecialCosts.All(cost => _specialResources.CanAfford(cost.ResourceId, cost.Amount));

    public SkillPurchaseResult Purchase(string skillId)
    {
        SkillPurchaseResult validation = ValidatePurchase(skillId, out SkillNodeDefinition? node, out int currentRank, out long cost);
        if (!validation.Success || node is null)
        {
            return validation;
        }

        if (!TryCommitCosts(node, cost))
        {
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientResources);
        }

        _ranks[skillId] = currentRank + 1;
        RebuildDerivedStats();
        Changed?.Invoke();
        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
    }

    /// <summary>
    /// Transaction used by buy-and-place automation UI. The prospective rank is applied temporarily so
    /// the placement callback sees the miner as unlocked, but no currency is deducted until that
    /// callback has successfully created the accepted placement. Cancelled/red previews therefore cost
    /// nothing and a failed commit rolls the temporary unlock back.
    /// </summary>
    public SkillPurchaseResult PurchaseAfterCommit(string skillId, Func<bool> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        SkillPurchaseResult validation = ValidatePurchase(skillId, out SkillNodeDefinition? node, out int currentRank, out long cost);
        if (!validation.Success || node is null)
        {
            return validation;
        }

        _ranks[skillId] = currentRank + 1;
        RebuildDerivedStats();

        bool committed;
        try
        {
            committed = commit();
        }
        catch
        {
            RestorePurchaseRank(skillId, currentRank);
            throw;
        }

        if (!committed)
        {
            RestorePurchaseRank(skillId, currentRank);
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.CommitRejected);
        }

        if (!TryCommitCosts(node, cost))
        {
            RestorePurchaseRank(skillId, currentRank);
            throw new InvalidOperationException(
                $"Deferred purchase '{skillId}' lost its validated affordability during placement commit.");
        }

        Changed?.Invoke();
        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
    }

    public bool IsMinerUnlocked(string minerId) => Derived.UnlockedMiners.Contains(minerId);
    public bool IsPatternUnlocked(string patternId) => Derived.UnlockedPatterns.Contains(patternId);

    public void RestoreRanks(IReadOnlyDictionary<string, int> ranks)
    {
        _ranks.Clear();
        foreach ((string id, int rank) in ranks)
        {
            if (!_catalog.Nodes.TryGetValue(id, out SkillNodeDefinition? node)) continue;
            _ranks[id] = Math.Clamp(rank, 0, node.MaxRank);
        }

        RebuildDerivedStats();
        Changed?.Invoke();
    }

    private SkillPurchaseResult ValidatePurchase(
        string skillId,
        out SkillNodeDefinition? node,
        out int currentRank,
        out long cost)
    {
        node = null;
        currentRank = 0;
        cost = 0L;

        if (!_catalog.Nodes.TryGetValue(skillId, out node))
        {
            return new SkillPurchaseResult(false, skillId, 0, SkillPurchaseFailure.UnknownSkill);
        }

        currentRank = GetRank(skillId);
        if (currentRank >= node.MaxRank)
        {
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.MaxRank);
        }

        if (!PrerequisitesMet(node))
        {
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.MissingPrerequisite);
        }

        cost = checked(node.Cost * (currentRank + 1L));
        if (_mining.Currency < cost)
        {
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientResources);
        }

        if (!SpecialCostsAffordable(node))
        {
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientSpecialResources);
        }

        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
    }

    private bool TryCommitCosts(SkillNodeDefinition node, long ordinaryCost)
    {
        // Validation happens immediately before this synchronous commit. Still keep the operation
        // rollback-safe so future UI/event hooks cannot turn a transformation into a partial purchase.
        if (_mining.Currency < ordinaryCost || !SpecialCostsAffordable(node)) return false;
        if (!_mining.TrySpend(ordinaryCost)) return false;

        var spentSpecial = new List<SkillSpecialCostDefinition>();
        foreach (SkillSpecialCostDefinition specialCost in node.SpecialCosts)
        {
            if (_specialResources.TrySpend(specialCost.ResourceId, specialCost.Amount))
            {
                spentSpecial.Add(specialCost);
                continue;
            }

            _mining.GrantCurrency(ordinaryCost);
            foreach (SkillSpecialCostDefinition spent in spentSpecial)
            {
                _specialResources.Grant(spent.ResourceId, spent.Amount);
            }
            return false;
        }

        return true;
    }

    private void RestorePurchaseRank(string skillId, int previousRank)
    {
        if (previousRank <= 0) _ranks.Remove(skillId);
        else _ranks[skillId] = previousRank;
        RebuildDerivedStats();
    }

    private void RebuildDerivedStats()
    {
        var stats = new SkillDerivedStats();

        foreach ((string id, int rank) in _ranks)
        {
            if (rank <= 0 || !_catalog.Nodes.TryGetValue(id, out SkillNodeDefinition? node)) continue;

            for (int appliedRank = 0; appliedRank < rank; appliedRank++)
            {
                foreach (SkillEffectDefinition effect in node.Effects)
                {
                    ApplyEffect(stats, effect);
                }
            }
        }

        Derived = stats;
        _mining.ConfigureProgressionEconomy(
            stats.ResourceYieldMultiplier,
            stats.PreciousResourceYieldMultiplier,
            stats.CriticalYieldChance,
            stats.CriticalYieldMultiplier);
    }

    private static void ApplyEffect(SkillDerivedStats stats, SkillEffectDefinition effect)
    {
        switch (effect.Type)
        {
            case "add_manual_blocks_per_click":
                stats.ManualBlocksPerClick = checked(stats.ManualBlocksPerClick + Math.Max(0, (int)Math.Round(effect.Value)));
                break;
            case "multiply_manual_mining_rate":
                stats.ManualMiningRateMultiplier *= Math.Max(0.01, effect.Value);
                break;
            case "set_manual_mining_power":
                stats.ManualMiningPower = Math.Max(stats.ManualMiningPower, Math.Max(0.01, effect.Value));
                break;
            case "set_manual_penetration_depth":
                stats.ManualPenetrationDepth = Math.Max(stats.ManualPenetrationDepth, Math.Max(1, (int)Math.Round(effect.Value)));
                break;
            case "set_manual_footprint":
                stats.ManualFootprint = ManualMiningFootprint.Parse(effect.StringValue);
                break;
            case "unlock_hover_mining":
                stats.HoverMiningUnlocked = true;
                break;
            case "multiply_resource_yield":
                stats.ResourceYieldMultiplier *= Math.Max(0.01, effect.Value);
                break;
            case "multiply_precious_resource_yield":
                stats.PreciousResourceYieldMultiplier *= Math.Max(0.01, effect.Value);
                break;
            case "add_critical_yield_chance":
                stats.CriticalYieldChance = Math.Clamp(stats.CriticalYieldChance + Math.Max(0.0, effect.Value), 0.0, 0.75);
                break;
            case "set_critical_yield_multiplier":
                stats.CriticalYieldMultiplier = Math.Max(stats.CriticalYieldMultiplier, Math.Max(1.0, effect.Value));
                break;
            case "multiply_miner_rate":
                stats.MinerRateMultiplier *= Math.Max(0.01, effect.Value);
                break;
            case "multiply_shovel_rate":
                stats.ShovelRateMultiplier *= Math.Max(0.01, effect.Value);
                break;
            case "unlock_miner":
                if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.UnlockedMiners.Add(effect.StringValue);
                break;
            case "unlock_pattern":
                if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.UnlockedPatterns.Add(effect.StringValue);
                break;
            case "set_drill_pattern":
                if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.DrillPatternId = effect.StringValue;
                break;
            case "set_drill_material_tier":
                stats.DrillMaterialTier = Math.Max(stats.DrillMaterialTier, (int)Math.Round(effect.Value));
                break;
            case "set_miner_pattern_width":
                stats.MinerPatternWidth = Math.Max(stats.MinerPatternWidth, (int)Math.Round(effect.Value));
                break;
            case "set_shovel_height_tolerance":
                stats.ShovelHeightTolerance = Math.Max(stats.ShovelHeightTolerance, (int)Math.Round(effect.Value));
                break;
            case "set_shovel_search_radius":
                stats.ShovelSearchRadius = Math.Max(stats.ShovelSearchRadius, (int)Math.Round(effect.Value));
                break;
            case "unlock_resource_filter":
                if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.ResourceFilters.Add(effect.StringValue);
                break;
            case "unlock_auto_cloud_charger":
                stats.AutoCloudChargerUnlocked = true;
                break;
            case "add_lightning_radius":
                stats.LightningRadiusBonus = checked(stats.LightningRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value)));
                break;
            case "add_lightning_chain_count":
                stats.LightningChainCount = checked(stats.LightningChainCount + Math.Max(0, (int)Math.Round(effect.Value)));
                break;
            case "add_meteor_radius":
                stats.MeteorRadiusBonus = checked(stats.MeteorRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value)));
                break;
        }
    }
}
