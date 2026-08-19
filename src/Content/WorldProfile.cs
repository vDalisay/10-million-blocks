using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TenMillionBlocks.Content;

public sealed class WorldProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Seed { get; set; }
    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }
    public int LogicalDepth { get; set; }
    public float BaseRadius { get; set; }
    public float TerrainAmplitude { get; set; }
    public float DetailAmplitude { get; set; }
    public float MacroFrequency { get; set; }
    public float DetailFrequency { get; set; }
    public float WaterThreshold { get; set; }
    public float TreeDensity { get; set; }
    public int ChunkSize { get; set; } = 8;
    public float BlockSpacing { get; set; } = 2.0f;
    public string SurfaceBlock { get; set; } = "grass";
    public string SurfaceEdgeBlock { get; set; } = "dirt_grass";
    public string SoilBlock { get; set; } = "dirt";
    public string StoneBlock { get; set; } = "stone";
    public string DarkStoneBlock { get; set; } = "stone_dark";
    public string SandBlock { get; set; } = "sand";
    public string WaterBlock { get; set; } = "water";
    public string CopperBlock { get; set; } = "copper";
    public string SilverBlock { get; set; } = "silver";
    public string GoldBlock { get; set; } = "gold";

    public int MaxCoordinate => (int)MathF.Ceiling(BaseRadius + TerrainAmplitude + DetailAmplitude + 2.0f);
}

public sealed class WorldCatalog
{
    public const int SupportedSchemaVersion = 1;

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public List<WorldProfile> Worlds { get; set; } = new();
    }

    private readonly Dictionary<string, WorldProfile> _worlds;

    private WorldCatalog(Dictionary<string, WorldProfile> worlds)
    {
        _worlds = worlds;
    }

    public IReadOnlyDictionary<string, WorldProfile> Worlds => _worlds;

    public static WorldCatalog Load(string path = "res://data/worlds/worlds.json")
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new InvalidOperationException($"World catalog was not found: {path}");
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        Document? document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        if (document is null)
        {
            throw new InvalidOperationException($"World catalog could not be parsed: {path}");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported world schema version {document.SchemaVersion}. Expected {SupportedSchemaVersion}.");
        }

        var worlds = new Dictionary<string, WorldProfile>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (WorldProfile world in document.Worlds)
        {
            Validate(world, errors);
            if (!string.IsNullOrWhiteSpace(world.Id) && !worlds.TryAdd(world.Id, world))
            {
                errors.Add($"Duplicate world id '{world.Id}'.");
            }
        }

        if (worlds.Count == 0)
        {
            errors.Add("World catalog contains no worlds.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("World catalog validation failed:\n - " + string.Join("\n - ", errors));
        }

        GD.Print($"Loaded {worlds.Count} world profiles from {path}.");
        return new WorldCatalog(worlds);
    }

    public WorldProfile Get(string id)
    {
        if (!_worlds.TryGetValue(id, out WorldProfile? profile))
        {
            throw new KeyNotFoundException($"Unknown world profile '{id}'.");
        }

        return profile;
    }

    private static void Validate(WorldProfile profile, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            errors.Add("World profile has an empty id.");
        }

        if (profile.LogicalWidth <= 0 || profile.LogicalHeight <= 0 || profile.LogicalDepth <= 0)
        {
            errors.Add($"World '{profile.Id}' must have positive logical dimensions.");
        }

        if (profile.BaseRadius <= 0.0f || profile.ChunkSize <= 0 || profile.BlockSpacing <= 0.0f)
        {
            errors.Add($"World '{profile.Id}' has invalid radius/chunk/spacing settings.");
        }

        if (profile.TreeDensity < 0.0f || profile.TreeDensity > 1.0f)
        {
            errors.Add($"World '{profile.Id}' tree density must be between 0 and 1.");
        }
    }
}
