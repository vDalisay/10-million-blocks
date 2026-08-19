using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TenMillionBlocks.Skills;

public sealed class SkillEffectDefinition
{
    public string Type { get; set; } = string.Empty;
    public double Value { get; set; }
    public string StringValue { get; set; } = string.Empty;
}

public sealed class SkillNodeDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GridX { get; set; }
    public int GridY { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> PrerequisiteNodeIds { get; set; } = new();
    public long Cost { get; set; }
    public int MaxRank { get; set; } = 1;
    public List<SkillEffectDefinition> Effects { get; set; } = new();
}

public sealed class SkillTreeCatalog
{
    public const int SupportedSchemaVersion = 1;

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public int ContentVersion { get; set; }
        public List<SkillNodeDefinition> Nodes { get; set; } = new();
    }

    private static readonly HashSet<string> KnownEffectTypes = new(StringComparer.Ordinal)
    {
        "add_manual_blocks_per_click",
        "multiply_miner_rate",
        "unlock_miner",
        "unlock_pattern",
        "set_miner_pattern_width",
        "unlock_resource_filter",
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
            throw new InvalidOperationException($"Skill tree has an unsupported or unreadable schema: {path}");
        }

        var nodes = new Dictionary<string, SkillNodeDefinition>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (SkillNodeDefinition node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id)) errors.Add("Skill node has an empty id.");
            if (string.IsNullOrWhiteSpace(node.DisplayName)) errors.Add($"Skill '{node.Id}' has no display name.");
            if (node.Cost < 0) errors.Add($"Skill '{node.Id}' has a negative cost.");
            if (node.MaxRank <= 0) errors.Add($"Skill '{node.Id}' must have max_rank > 0.");
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
            foreach (string prerequisite in node.PrerequisiteNodeIds)
            {
                if (!nodes.ContainsKey(prerequisite))
                {
                    errors.Add($"Skill '{node.Id}' references missing prerequisite '{prerequisite}'.");
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

            foreach (string prerequisite in nodes[id].PrerequisiteNodeIds)
            {
                if (nodes.ContainsKey(prerequisite) && Visit(prerequisite)) return true;
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
