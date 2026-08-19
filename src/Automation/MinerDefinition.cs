using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TenMillionBlocks.Automation;

public sealed class MinerDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double BaseRate { get; set; } = 1.0;
    public string PatternId { get; set; } = "line";
    public int Range { get; set; } = 32;
    public double Power { get; set; } = 1.0;
    public List<string> AllowedBlockTags { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public sealed class MinerCatalog
{
    public const int SupportedSchemaVersion = 1;

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public List<MinerDefinition> Miners { get; set; } = new();
    }

    private readonly Dictionary<string, MinerDefinition> _miners;

    private MinerCatalog(Dictionary<string, MinerDefinition> miners)
    {
        _miners = miners;
    }

    public IReadOnlyDictionary<string, MinerDefinition> Miners => _miners;

    public static MinerCatalog Load(string path = "res://data/miners/miners.json")
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new InvalidOperationException($"Miner catalog was not found: {path}");
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        Document? document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        if (document is null || document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException($"Miner catalog has an unsupported or unreadable schema: {path}");
        }

        var result = new Dictionary<string, MinerDefinition>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (MinerDefinition miner in document.Miners)
        {
            if (string.IsNullOrWhiteSpace(miner.Id)) errors.Add("Miner has an empty id.");
            if (string.IsNullOrWhiteSpace(miner.DisplayName)) errors.Add($"Miner '{miner.Id}' has no display name.");
            if (string.IsNullOrWhiteSpace(miner.PatternId)) errors.Add($"Miner '{miner.Id}' has no pattern id.");
            if (miner.BaseRate <= 0.0) errors.Add($"Miner '{miner.Id}' must have base_rate > 0.");
            if (miner.Range <= 0) errors.Add($"Miner '{miner.Id}' must have range > 0.");
            if (!string.IsNullOrWhiteSpace(miner.Id) && !result.TryAdd(miner.Id, miner))
            {
                errors.Add($"Duplicate miner id '{miner.Id}'.");
            }
        }

        if (result.Count == 0) errors.Add("Miner catalog contains no miners.");
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Miner catalog validation failed:\n - " + string.Join("\n - ", errors));
        }

        GD.Print($"Loaded {result.Count} miner definitions from {path}.");
        return new MinerCatalog(result);
    }

    public MinerDefinition Get(string id)
    {
        if (!_miners.TryGetValue(id, out MinerDefinition? definition))
        {
            throw new KeyNotFoundException($"Unknown miner id '{id}'.");
        }

        return definition;
    }
}
