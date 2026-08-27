using System;
using System.Collections.Generic;
using System.Linq;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Skills;

public sealed class SkillDerivedStats
{
    public int ManualBlocksPerClick { get; internal set; } = 1;
    public ManualMiningFootprintKind ManualFootprint { get; internal set; } = ManualMiningFootprintKind.Single;
    public bool HoverMiningUnlocked { get; internal set; }
    public double ManualMiningRateMultiplier { get; internal set; } = 1.0;
    public double ManualMiningPower { get; internal set; } = 1.0;
    public int ManualPenetrationDepth { get; internal set; } = 1;

    public bool LaserUnlocked { get; internal set; }
    public double LaserManualChargePerAction { get; internal set; } = 0.0125;
    public double LaserAutoChargePerAction { get; internal set; } = 0.0030;
    public double LaserDamagePerSecond { get; internal set; } = 1.0;
    public int LaserBeamRadius { get; internal set; } = 1;
    public double LaserDurationSeconds { get; internal set; } = 5.0;
    public double LaserCooldownSeconds { get; internal set; } = 60.0;
    public bool LaserResourceBurnUnlocked { get; internal set; }
    public double LaserResourceCostPerSecond { get; internal set; } = 300.0;

    public double CollectionRadiusBlocks { get; internal set; } = 0.32;
    public double CollectionRatePerSecond { get; internal set; } = 8.0;
    public bool ManualAutoCollectUnlocked { get; internal set; }
    public bool AutomationAutoCollectUnlocked { get; internal set; }

    public double ResourceYieldMultiplier { get; internal set; } = 1.0;
    public double PreciousResourceYieldMultiplier { get; internal set; } = 1.0;
    public double CriticalYieldChance { get; internal set; }
    public double CriticalYieldMultiplier { get; internal set; } = 2.0;

    public double MinerRateMultiplier { get; internal set; } = 1.0;
    public int MinerPatternWidth { get; internal set; } = 1;
    public string DrillPatternId { get; internal set; } = "line";
    public int DrillMaterialTier { get; internal set; } = 0;

    public double ShovelRateMultiplier { get; internal set; } = 1.0;
    public int ShovelHeightTolerance { get; internal set; } = 0;
    public int ShovelSearchRadius { get; internal set; } = 1;

    public bool AutoCloudChargerUnlocked { get; internal set; }
    public bool RadioactiveCloudUnlocked { get; internal set; }
    public double RadioactiveCloudRateMultiplier { get; internal set; } = 1.0;
    public int RadioactiveCloudRadiusBonus { get; internal set; }
    public bool OrbBreakerUnlocked { get; internal set; }
    public double OrbBreakerRateMultiplier { get; internal set; } = 1.0;
    public int OrbBreakerCount { get; internal set; } = 1;
    public int OrbBreakerRadiusBonus { get; internal set; }
    public double CloudChargeRateMultiplier { get; internal set; } = 1.0;
    public int LightningRadiusBonus { get; internal set; }
    public int LightningChainCount { get; internal set; }
    public double MeteorSpawnRateMultiplier { get; internal set; } = 1.0;
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

public readonly record struct SkillPurchaseResult(bool Success, string SkillId, int NewRank, SkillPurchaseFailure Failure);

public sealed class SkillTreeService
{
    private readonly SkillTreeCatalog _catalog;
    private readonly MiningService _mining;
    private readonly SpecialResourceInventory _specialResources;
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);

    public SkillTreeService(SkillTreeCatalog catalog, MiningService mining)
        : this(catalog, mining, mining.SpecialResources) { }

    public SkillTreeService(SkillTreeCatalog catalog, MiningService mining, SpecialResourceInventory specialResources)
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
        => !node.HideUntilPrerequisitesMet || node.Prerequisites.Count == 0 || GetRank(node.Id) > 0 || PrerequisitesMet(node);

    public bool SpecialCostsAffordable(SkillNodeDefinition node)
        => node.SpecialCosts.All(cost => _specialResources.CanAfford(cost.ResourceId, cost.Amount));

    public SkillPurchaseResult Purchase(string skillId)
    {
        SkillPurchaseResult validation = ValidatePurchase(skillId, out SkillNodeDefinition? node, out int currentRank, out long cost);
        if (!validation.Success || node is null) return validation;
        if (!TryCommitCosts(node, cost))
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientResources);

        _ranks[skillId] = currentRank + 1;
        RebuildDerivedStats();
        Changed?.Invoke();
        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
    }

    public SkillPurchaseResult PurchaseAfterCommit(string skillId, Func<bool> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        SkillPurchaseResult validation = ValidatePurchase(skillId, out SkillNodeDefinition? node, out int currentRank, out long cost);
        if (!validation.Success || node is null) return validation;

        _ranks[skillId] = currentRank + 1;
        RebuildDerivedStats();
        bool committed;
        try { committed = commit(); }
        catch { RestorePurchaseRank(skillId, currentRank); throw; }

        if (!committed)
        {
            RestorePurchaseRank(skillId, currentRank);
            return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.CommitRejected);
        }
        if (!TryCommitCosts(node, cost))
        {
            RestorePurchaseRank(skillId, currentRank);
            throw new InvalidOperationException($"Deferred purchase '{skillId}' lost affordability during placement commit.");
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
            if (_catalog.Nodes.TryGetValue(id, out SkillNodeDefinition? node)) _ranks[id] = Math.Clamp(rank, 0, node.MaxRank);
        RebuildDerivedStats();
        Changed?.Invoke();
    }

    private SkillPurchaseResult ValidatePurchase(string skillId, out SkillNodeDefinition? node, out int currentRank, out long cost)
    {
        node = null; currentRank = 0; cost = 0L;
        if (!_catalog.Nodes.TryGetValue(skillId, out node))
            return new SkillPurchaseResult(false, skillId, 0, SkillPurchaseFailure.UnknownSkill);
        currentRank = GetRank(skillId);
        if (currentRank >= node.MaxRank) return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.MaxRank);
        if (!PrerequisitesMet(node)) return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.MissingPrerequisite);
        cost = checked(node.Cost * (currentRank + 1L));
        if (_mining.Currency < cost) return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientResources);
        if (!SpecialCostsAffordable(node)) return new SkillPurchaseResult(false, skillId, currentRank, SkillPurchaseFailure.InsufficientSpecialResources);
        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
    }

    private bool TryCommitCosts(SkillNodeDefinition node, long ordinaryCost)
    {
        if (_mining.Currency < ordinaryCost || !SpecialCostsAffordable(node)) return false;
        if (!_mining.TrySpend(ordinaryCost)) return false;
        var spentSpecial = new List<SkillSpecialCostDefinition>();
        foreach (SkillSpecialCostDefinition specialCost in node.SpecialCosts)
        {
            if (_specialResources.TrySpend(specialCost.ResourceId, specialCost.Amount)) { spentSpecial.Add(specialCost); continue; }
            _mining.GrantCurrency(ordinaryCost);
            foreach (SkillSpecialCostDefinition spent in spentSpecial) _specialResources.Grant(spent.ResourceId, spent.Amount);
            return false;
        }
        return true;
    }

    private void RestorePurchaseRank(string skillId, int previousRank)
    {
        if (previousRank <= 0) _ranks.Remove(skillId); else _ranks[skillId] = previousRank;
        RebuildDerivedStats();
    }

    private void RebuildDerivedStats()
    {
        var stats = new SkillDerivedStats();
        foreach ((string id, int rank) in _ranks)
        {
            if (rank <= 0 || !_catalog.Nodes.TryGetValue(id, out SkillNodeDefinition? node)) continue;
            for (int appliedRank = 0; appliedRank < rank; appliedRank++)
                foreach (SkillEffectDefinition effect in node.Effects) ApplyEffect(stats, effect);
        }
        Derived = stats;
        _mining.ConfigureProgressionEconomy(stats.ResourceYieldMultiplier, stats.PreciousResourceYieldMultiplier,
            stats.CriticalYieldChance, stats.CriticalYieldMultiplier);
    }

    private static void ApplyEffect(SkillDerivedStats stats, SkillEffectDefinition effect)
    {
        switch (effect.Type)
        {
            case "add_manual_blocks_per_click": stats.ManualBlocksPerClick = checked(stats.ManualBlocksPerClick + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "multiply_manual_mining_rate": stats.ManualMiningRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "set_manual_mining_power": stats.ManualMiningPower = Math.Max(stats.ManualMiningPower, Math.Max(0.01, effect.Value)); break;
            case "set_manual_penetration_depth": stats.ManualPenetrationDepth = Math.Max(stats.ManualPenetrationDepth, Math.Max(1, (int)Math.Round(effect.Value))); break;
            case "set_manual_footprint": stats.ManualFootprint = ManualMiningFootprint.Parse(effect.StringValue); break;
            case "unlock_hover_mining": stats.HoverMiningUnlocked = true; break;
            case "unlock_laser": stats.LaserUnlocked = true; break;
            case "multiply_laser_manual_charge_rate": stats.LaserManualChargePerAction *= Math.Max(0.01, effect.Value); break;
            case "multiply_laser_auto_charge_rate": stats.LaserAutoChargePerAction *= Math.Max(0.01, effect.Value); break;
            case "multiply_laser_damage": stats.LaserDamagePerSecond *= Math.Max(0.01, effect.Value); break;
            case "set_laser_beam_radius": stats.LaserBeamRadius = Math.Max(stats.LaserBeamRadius, Math.Max(1, (int)Math.Round(effect.Value))); break;
            case "set_laser_duration_seconds": stats.LaserDurationSeconds = Math.Max(stats.LaserDurationSeconds, Math.Max(0.5, effect.Value)); break;
            case "set_laser_cooldown_seconds": stats.LaserCooldownSeconds = Math.Min(stats.LaserCooldownSeconds, Math.Max(1.0, effect.Value)); break;
            case "unlock_laser_resource_burn": stats.LaserResourceBurnUnlocked = true; break;
            case "set_laser_resource_cost_per_second": stats.LaserResourceCostPerSecond = Math.Max(1.0, effect.Value); break;
            case "multiply_laser_resource_cost": stats.LaserResourceCostPerSecond *= Math.Clamp(effect.Value, 0.05, 10.0); break;
            case "set_collection_radius_blocks": stats.CollectionRadiusBlocks = Math.Max(stats.CollectionRadiusBlocks, Math.Max(0.05, effect.Value)); break;
            case "multiply_collection_rate": stats.CollectionRatePerSecond *= Math.Max(0.05, effect.Value); break;
            case "unlock_manual_auto_collect": stats.ManualAutoCollectUnlocked = true; break;
            case "unlock_automation_auto_collect": stats.AutomationAutoCollectUnlocked = true; break;
            case "multiply_resource_yield": stats.ResourceYieldMultiplier *= Math.Max(0.01, effect.Value); break;
            case "multiply_precious_resource_yield": stats.PreciousResourceYieldMultiplier *= Math.Max(0.01, effect.Value); break;
            case "add_critical_yield_chance": stats.CriticalYieldChance = Math.Clamp(stats.CriticalYieldChance + Math.Max(0.0, effect.Value), 0.0, 0.75); break;
            case "set_critical_yield_multiplier": stats.CriticalYieldMultiplier = Math.Max(stats.CriticalYieldMultiplier, Math.Max(1.0, effect.Value)); break;
            case "multiply_miner_rate": stats.MinerRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "multiply_shovel_rate": stats.ShovelRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "unlock_miner": if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.UnlockedMiners.Add(effect.StringValue); break;
            case "unlock_pattern": if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.UnlockedPatterns.Add(effect.StringValue); break;
            case "set_drill_pattern": if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.DrillPatternId = effect.StringValue; break;
            case "set_drill_material_tier": stats.DrillMaterialTier = Math.Max(stats.DrillMaterialTier, (int)Math.Round(effect.Value)); break;
            case "set_miner_pattern_width": stats.MinerPatternWidth = Math.Max(stats.MinerPatternWidth, (int)Math.Round(effect.Value)); break;
            case "set_shovel_height_tolerance": stats.ShovelHeightTolerance = Math.Max(stats.ShovelHeightTolerance, (int)Math.Round(effect.Value)); break;
            case "set_shovel_search_radius": stats.ShovelSearchRadius = Math.Max(stats.ShovelSearchRadius, (int)Math.Round(effect.Value)); break;
            case "unlock_resource_filter": if (!string.IsNullOrWhiteSpace(effect.StringValue)) stats.ResourceFilters.Add(effect.StringValue); break;
            case "unlock_auto_cloud_charger": stats.AutoCloudChargerUnlocked = true; break;
            case "unlock_radioactive_cloud": stats.RadioactiveCloudUnlocked = true; break;
            case "multiply_radioactive_cloud_rate": stats.RadioactiveCloudRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "add_radioactive_cloud_radius": stats.RadioactiveCloudRadiusBonus = checked(stats.RadioactiveCloudRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "unlock_orb_breaker": stats.OrbBreakerUnlocked = true; break;
            case "multiply_orb_breaker_rate": stats.OrbBreakerRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "add_orb_breaker_count": stats.OrbBreakerCount = checked(stats.OrbBreakerCount + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "add_orb_breaker_radius": stats.OrbBreakerRadiusBonus = checked(stats.OrbBreakerRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "multiply_cloud_charge_rate": stats.CloudChargeRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "add_lightning_radius": stats.LightningRadiusBonus = checked(stats.LightningRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "add_lightning_chain_count": stats.LightningChainCount = checked(stats.LightningChainCount + Math.Max(0, (int)Math.Round(effect.Value))); break;
            case "multiply_meteor_spawn_rate": stats.MeteorSpawnRateMultiplier *= Math.Max(0.01, effect.Value); break;
            case "add_meteor_radius": stats.MeteorRadiusBonus = checked(stats.MeteorRadiusBonus + Math.Max(0, (int)Math.Round(effect.Value))); break;
        }
    }
}
