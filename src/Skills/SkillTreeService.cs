using System;
using System.Collections.Generic;
using System.Linq;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Skills;

public sealed class SkillDerivedStats
{
    // Legacy count remains for compatibility with existing saves/data while the future progression
    // moves manual area mining onto explicit footprint strategies.
    public int ManualBlocksPerClick { get; internal set; } = 1;
    public ManualMiningFootprintKind ManualFootprint { get; internal set; } = ManualMiningFootprintKind.Single;
    public bool HoverMiningUnlocked { get; internal set; }
    public double ManualMiningRateMultiplier { get; internal set; } = 1.0;

    public double MinerRateMultiplier { get; internal set; } = 1.0;
    public int MinerPatternWidth { get; internal set; } = 1;
    public string DrillPatternId { get; internal set; } = "line";

    // Tier 0 = basic stone only. Later drill-bit skills widen the material vocabulary without making
    // the drill silently skip blockers it does not yet understand.
    public int DrillMaterialTier { get; internal set; } = 0;

    // Powered Shovel deliberately starts primitive: one sand tile per second, adjacent cardinal tiles
    // only, and no ability to climb/drop. Separate skills make it faster and smarter.
    public double ShovelRateMultiplier { get; internal set; } = 1.0;
    public int ShovelHeightTolerance { get; internal set; } = 0;
    public int ShovelSearchRadius { get; internal set; } = 1;

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
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);

    public SkillTreeService(SkillTreeCatalog catalog, MiningService mining)
    {
        _catalog = catalog;
        _mining = mining;
        Derived = new SkillDerivedStats();
        RebuildDerivedStats();
    }

    public event Action? Changed;

    public SkillTreeCatalog Catalog => _catalog;
    public SkillDerivedStats Derived { get; private set; }
    public IReadOnlyDictionary<string, int> Ranks => _ranks;

    public int GetRank(string skillId) => _ranks.GetValueOrDefault(skillId);

    public bool PrerequisitesMet(SkillNodeDefinition node)
        => node.Prerequisites.All(prerequisite => GetRank(prerequisite.NodeId) >= prerequisite.RequiredRank);

    public SkillPurchaseResult Purchase(string skillId)
    {
        SkillPurchaseResult validation = ValidatePurchase(skillId, out SkillNodeDefinition? node, out int currentRank, out long cost);
        if (!validation.Success || node is null)
        {
            return validation;
        }

        if (!_mining.TrySpend(cost))
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
    /// the placement callback sees the miner as unlocked, but no resources are deducted until that
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

        // ValidatePurchase checked the same currency value immediately before the synchronous callback;
        // placement itself never spends currency, so this is an invariant rather than an expected fail.
        if (!_mining.TrySpend(cost))
        {
            RestorePurchaseRank(skillId, currentRank);
            throw new InvalidOperationException($"Deferred purchase '{skillId}' lost its reserved affordability during placement commit.");
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

        return new SkillPurchaseResult(true, skillId, currentRank + 1, SkillPurchaseFailure.None);
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
            case "set_manual_footprint":
                stats.ManualFootprint = ManualMiningFootprint.Parse(effect.StringValue);
                break;
            case "unlock_hover_mining":
                stats.HoverMiningUnlocked = true;
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
        }
    }
}
