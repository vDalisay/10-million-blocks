using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace TenMillionBlocks.Skills;

public sealed class SkillEffectDefinition
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public string StringValue { get; set; } = string.Empty;
}

public sealed class SkillSpecialCostDefinition
{
    public string ResourceId { get; set; } = string.Empty;
    public long Amount { get; set; }
}

public sealed class SkillRoutePoint
{
    public int GridX { get; set; }
    public int GridY { get; set; }
}

public sealed class SkillPrerequisiteDefinition
{
    public string NodeId { get; set; } = string.Empty;
    public int RequiredRank { get; set; } = 1;
    public List<SkillRoutePoint> Route { get; set; } = new();
}

public sealed class SkillNodeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GridX { get; set; }
    public int GridY { get; set; }
    public string Category { get; set; } = string.Empty;
    public string PurchaseMode { get; set; } = "once"; // once | repeatable
    public List<SkillPrerequisiteDefinition> Prerequisites { get; set; } = new();

    // Optional progressive-disclosure rule. World staging still decides whether a skill belongs in the
    // current world's tree at all; this flag only decides whether that staged node is revealed before
    // its authored prerequisite ranks have been reached.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideUntilPrerequisitesMet { get; set; }

    public long Cost { get; set; }
    public List<SkillSpecialCostDefinition> SpecialCosts { get; set; } = new();
    public int MaxRank { get; set; } = 1;
    public List<SkillEffectDefinition> Effects { get; set; } = new();
}

public sealed class SkillTreeCatalog
{
    public const int SupportedSchemaVersion = 2;

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public int ContentVersion { get; set; }
        public List<SkillNodeDefinition> Nodes { get; set; } = new();
    }

    private static readonly HashSet<string> KnownEffectTypes = new(StringComparer.Ordinal)
    {
        "add_manual_blocks_per_click",
        "multiply_manual_mining_rate",
        "set_manual_mining_power",
        "set_manual_penetration_depth",
        "set_manual_footprint",
        "unlock_hover_mining",
        "multiply_resource_yield",
        "multiply_precious_resource_yield",
        "add_critical_yield_chance",
        "set_critical_yield_multiplier",
        "multiply_miner_rate",
        "multiply_shovel_rate",
        "unlock_miner",
        "unlock_pattern",
        "set_drill_pattern",
        "set_drill_material_tier",
        "set_miner_pattern_width",
        "set_shovel_height_tolerance",
        "set_shovel_search_radius",
        "unlock_resource_filter",
        "unlock_auto_cloud_charger",
        "add_lightning_radius",
        "add_lightning_chain_count",
        "add_meteor_radius",
    };

    private static readonly HashSet<string> KnownPurchaseModes = new(StringComparer.Ordinal)
    {
        "once",
        "repeatable",
    };

    private readonly Dictionary<string, SkillNodeDefinition> _nodes;

    private SkillTreeCatalog(int contentVersion, Dictionary<string, SkillNodeDefinition> nodes)
    {
        ContentVersion = contentVersion;
        _nodes = nodes;
    }

    public int ContentVersion { get; }
    public IReadOnlyDictionary<string, SkillNodeDefinition> Nodes => _nodes;

    public static SkillTreeCatalog Load(string path = "res://data/skills/skill_tree.json")
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new InvalidOperationException($"Skill tree was not found: {path}");
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        Document? document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        if (document is null || document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Skill tree has an unsupported or unreadable schema: {path}. Expected schema {SupportedSchemaVersion}.");
        }

        var nodes = new Dictionary<string, SkillNodeDefinition>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (SkillNodeDefinition node in document.Nodes)
        {
            node.Prerequisites ??= new List<SkillPrerequisiteDefinition>();
            node.SpecialCosts ??= new List<SkillSpecialCostDefinition>();
            node.Effects ??= new List<SkillEffectDefinition>();

            if (string.IsNullOrWhiteSpace(node.Id)) errors.Add("Skill node has an empty id.");
            if (string.IsNullOrWhiteSpace(node.DisplayName)) errors.Add($"Skill '{node.Id}' has no display name.");
            if (node.Cost < 0) errors.Add($"Skill '{node.Id}' has a negative cost.");
            if (node.MaxRank <= 0) errors.Add($"Skill '{node.Id}' must have max_rank > 0.");
            if (!KnownPurchaseModes.Contains(node.PurchaseMode))
            {
                errors.Add($"Skill '{node.Id}' has unknown purchase_mode '{node.PurchaseMode}'.");
            }
            else if (node.PurchaseMode == "once" && node.MaxRank != 1)
            {
                errors.Add($"One-time skill '{node.Id}' must have max_rank = 1.");
            }
            else if (node.PurchaseMode == "repeatable" && node.MaxRank < 2)
            {
                errors.Add($"Repeatable skill '{node.Id}' must have max_rank >= 2.");
            }

            var specialIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillSpecialCostDefinition specialCost in node.SpecialCosts)
            {
                if (string.IsNullOrWhiteSpace(specialCost.ResourceId))
                {
                    errors.Add($"Skill '{node.Id}' has a special cost with an empty resource id.");
                }
                else if (!specialIds.Add(specialCost.ResourceId))
                {
                    errors.Add($"Skill '{node.Id}' contains duplicate special cost '{specialCost.ResourceId}'.");
                }
                if (specialCost.Amount <= 0)
                {
                    errors.Add($"Skill '{node.Id}' special cost '{specialCost.ResourceId}' must be positive.");
                }
            }

            if (!string.IsNullOrWhiteSpace(node.Id) && !nodes.TryAdd(node.Id, node))
            {
                errors.Add($"Duplicate skill id '{node.Id}'.");
            }

            foreach (SkillEffectDefinition effect in node.Effects)
            {
                if (!KnownEffectTypes.Contains(effect.Type))
                {
                    errors.Add($"Skill '{node.Id}' references unknown effect '{effect.Type}'.");
                }
            }
        }

        foreach (SkillNodeDefinition node in nodes.Values)
        {
            var seenPrerequisites = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            {
                prerequisite.Route ??= new List<SkillRoutePoint>();
                if (!nodes.TryGetValue(prerequisite.NodeId, out SkillNodeDefinition? source))
                {
                    errors.Add($"Skill '{node.Id}' references missing prerequisite '{prerequisite.NodeId}'.");
                    continue;
                }

                if (!seenPrerequisites.Add(prerequisite.NodeId))
                {
                    errors.Add($"Skill '{node.Id}' contains duplicate prerequisite '{prerequisite.NodeId}'.");
                }

                if (prerequisite.RequiredRank <= 0 || prerequisite.RequiredRank > source.MaxRank)
                {
                    errors.Add(
                        $"Skill '{node.Id}' prerequisite '{prerequisite.NodeId}' requires rank {prerequisite.RequiredRank}, " +
                        $"but the source max rank is {source.MaxRank}.");
                }
            }
        }

        DetectCycles(nodes, errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Skill tree validation failed:\n - " + string.Join("\n - ", errors));
        }

        GD.Print($"Loaded and validated {nodes.Count} skill nodes from {path}.");
        return new SkillTreeCatalog(document.ContentVersion, nodes);
    }

    public SkillNodeDefinition Get(string id)
    {
        if (!_nodes.TryGetValue(id, out SkillNodeDefinition? node))
        {
            throw new KeyNotFoundException($"Unknown skill id '{id}'.");
        }

        return node;
    }

    private static void DetectCycles(IReadOnlyDictionary<string, SkillNodeDefinition> nodes, ICollection<string> errors)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;

            foreach (SkillPrerequisiteDefinition prerequisite in nodes[id].Prerequisites)
            {
                if (nodes.ContainsKey(prerequisite.NodeId) && Visit(prerequisite.NodeId)) return true;
            }

            visiting.Remove(id);
            visited.Add(id);
            return false;
        }

        foreach (string id in nodes.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (Visit(id))
            {
                errors.Add($"Circular prerequisite graph detected at '{id}'.");
                return;
            }
        }
    }
}
