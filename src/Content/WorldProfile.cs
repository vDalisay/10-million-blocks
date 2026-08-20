using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TenMillionBlocks.Content;

public sealed class WorldProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IntroText { get; set; } = string.Empty;
    public int WorldVersion { get; set; } = 1;
    public int GenerationVersion { get; set; } = 1;
    public string GenerationMode { get; set; } = "procedural";
    public string OverrideFile { get; set; } = string.Empty;
    public string CurrencyScope { get; set; } = "persistent_main";
    public bool SkillTreeAvailable { get; set; } = true;
    public bool AutomationAvailable { get; set; } = true;
    public List<string> VisibleSkillCategories { get; set; } = new();
    public List<string> VisibleSkillIds { get; set; } = new();
    public int Seed { get; set; }
    public int LogicalWidth { get; set; }
    public int LogicalHeight { get; set; }
    public int LogicalDepth { get; set; }
    public float BaseRadius { get; set; }
    public float TerrainAmplitude { get; set; }
    public float DetailAmplitude { get; set; }
    public float MacroFrequency { get; set; }
    public float DetailFrequency { get; set; }

    public float ClimateFrequency { get; set; } = 0.92f;
    public float ErosionFrequency { get; set; } = 1.15f;
    public float RidgeFrequency { get; set; } = 2.15f;
    public float OceanThreshold { get; set; } = -0.16f;
    public float SeaLevelOffset { get; set; } = 0.65f;
    public float ShoreBand { get; set; } = 0.16f;
    public float PlateauStep { get; set; } = 0.5f;
    public float ForestThreshold { get; set; } = 0.08f;
    public float WaterThreshold { get; set; }
    public float TreeDensity { get; set; }

    public long TargetMineableBlocks { get; set; }
    public long AggregateRewardPerBlock { get; set; } = 1;
    public int ChunkSize { get; set; } = 8;
    public int RegionSizeInChunks { get; set; } = 8;
    public float BlockSpacing { get; set; } = 2.0f;

    public string RendererMode { get; set; } = "auto";
    public int StreamingThresholdMaxCoordinate { get; set; } = 96;
    public int StreamingChunkRadius { get; set; } = 1;
    public int DetailedSurfaceDepthChunks { get; set; } = 1;
    public int MacroResolution { get; set; } = 24;

    public string SurfaceBlock { get; set; } = "grass";
    public string SurfaceEdgeBlock { get; set; } = "dirt_grass";
    public string SoilBlock { get; set; } = "dirt";
    public string StoneBlock { get; set; } = "stone";
    public string DarkStoneBlock { get; set; } = "stone_dark";
    public string SandBlock { get; set; } = "sand";
    public string WaterBlock { get; set; } = "water";
    public string ShallowWaterBlock { get; set; } = "water_shallow";
    public string DeepWaterBlock { get; set; } = "water_deep";
    public string CopperBlock { get; set; } = "copper";
    public string SilverBlock { get; set; } = "silver";
    public string GoldBlock { get; set; } = "gold";

    public int MaxCoordinate => UsesSolidCubeGenerator
        ? Math.Max(Math.Max(LogicalWidth, LogicalHeight), LogicalDepth) / 2 + 1
        : (int)MathF.Ceiling(
            BaseRadius + TerrainAmplitude + DetailAmplitude + MathF.Max(0.0f, SeaLevelOffset) + 3.0f);

    public bool UsesFullSurfaceRenderer
        => RendererMode.Equals("full_surface", StringComparison.OrdinalIgnoreCase);

    public bool UsesSingleBlockGenerator
        => string.Equals(GenerationMode, "single_block", StringComparison.OrdinalIgnoreCase);

    public bool UsesSolidCubeGenerator
        => string.Equals(GenerationMode, "solid_cube", StringComparison.OrdinalIgnoreCase);

    public bool UsesStreamingRenderer
        => UsesFullSurfaceRenderer || MaxCoordinate > StreamingThresholdMaxCoordinate;

    public bool UsesTutorialLocalWallet
        => CurrencyScope.Equals("tutorial_local", StringComparison.OrdinalIgnoreCase);

    public bool IsSkillCategoryVisible(string category)
        => VisibleSkillCategories.Count == 0 || VisibleSkillCategories.Contains(category, StringComparer.Ordinal);

    public bool IsSkillVisible(string skillId, string category)
    {
        // Empty filters mean the normal unrestricted authored world. Once either filter is populated,
        // a node is visible when its whole category is staged OR that exact node is deliberately
        // introduced. This lets tutorials teach Forest Cutter without exposing every future tool.
        if (VisibleSkillCategories.Count == 0 && VisibleSkillIds.Count == 0) return true;
        return VisibleSkillCategories.Contains(category, StringComparer.Ordinal)
            || VisibleSkillIds.Contains(skillId, StringComparer.Ordinal);
    }
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
            world.VisibleSkillCategories ??= new List<string>();
            world.VisibleSkillIds ??= new List<string>();
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

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            errors.Add($"World '{profile.Id}' has an empty display name.");
        }

        if (profile.WorldVersion <= 0)
        {
            errors.Add($"World '{profile.Id}' must have a positive world version.");
        }

        if (profile.GenerationVersion <= 0)
        {
            errors.Add($"World '{profile.Id}' must have a positive generation version.");
        }

        if (!string.Equals(profile.GenerationMode, "procedural", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(profile.GenerationMode, "single_block", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(profile.GenerationMode, "solid_cube", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"World '{profile.Id}' has unknown generation mode '{profile.GenerationMode}'.");
        }

        if (!profile.CurrencyScope.Equals("tutorial_local", StringComparison.OrdinalIgnoreCase)
            && !profile.CurrencyScope.Equals("persistent_main", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"World '{profile.Id}' has unknown currency scope '{profile.CurrencyScope}'.");
        }

        if (!string.IsNullOrWhiteSpace(profile.OverrideFile))
        {
            if (!profile.OverrideFile.StartsWith("res://", StringComparison.Ordinal))
            {
                errors.Add($"World '{profile.Id}' override file must use a res:// path.");
            }
            else if (!Godot.FileAccess.FileExists(profile.OverrideFile))
            {
                errors.Add($"World '{profile.Id}' override file does not exist: {profile.OverrideFile}");
            }
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

        if (profile.PlateauStep <= 0.0f || profile.ShoreBand < 0.0f)
        {
            errors.Add($"World '{profile.Id}' has invalid plateau/shore settings.");
        }

        if (profile.TargetMineableBlocks < 0 || profile.AggregateRewardPerBlock < 0)
        {
            errors.Add($"World '{profile.Id}' has invalid aggregate counter/reward settings.");
        }

        if (profile.RegionSizeInChunks <= 0 || profile.StreamingChunkRadius < 0
            || profile.DetailedSurfaceDepthChunks <= 0 || profile.MacroResolution < 4)
        {
            errors.Add($"World '{profile.Id}' has invalid region/streaming settings.");
        }

        if (!profile.RendererMode.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !profile.RendererMode.Equals("full_surface", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"World '{profile.Id}' has unknown renderer_mode '{profile.RendererMode}'.");
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string category in profile.VisibleSkillCategories)
        {
            if (string.IsNullOrWhiteSpace(category) || !categories.Add(category))
            {
                errors.Add($"World '{profile.Id}' has an empty or duplicate visible skill category.");
            }
        }

        var skillIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string skillId in profile.VisibleSkillIds)
        {
            if (string.IsNullOrWhiteSpace(skillId) || !skillIds.Add(skillId))
            {
                errors.Add($"World '{profile.Id}' has an empty or duplicate visible skill id.");
            }
        }
    }
}
