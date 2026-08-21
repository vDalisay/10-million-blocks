using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Storage;
using TenMillionBlocks.WorldEvents;

namespace TenMillionBlocks.Save;

public sealed class WorldSaveData
{
    public string WorldId { get; set; } = string.Empty;
    public int WorldVersion { get; set; }
    public int GenerationVersion { get; set; }
    public long TutorialLocalCurrency { get; set; }
    public long ManualBlocksMined { get; set; }
    public long AutomatedBlocksMined { get; set; }
    public bool HoverMiningEnabled { get; set; }
    public bool Completed { get; set; }
    public long FirstStartedUnixSeconds { get; set; }
    public long LastPlayedUnixSeconds { get; set; }
    public long CompletedUnixSeconds { get; set; }
    public string ReplayFile { get; set; } = string.Empty;
    public WorldEventSnapshot? WorldEvents { get; set; }
    public List<MinedChunkSnapshot> MinedChunks { get; set; } = new();
    public List<ExhaustedRegionSnapshot> ExhaustedRegions { get; set; } = new();
    public List<MinerSnapshot> Miners { get; set; } = new();
}

public sealed class GameSaveData
{
    public int SchemaVersion { get; set; } = SaveService.SupportedSchemaVersion;
    public long SavedAtUnixSeconds { get; set; }
    public string CurrentWorldId { get; set; } = string.Empty;
    public long PersistentMainCurrency { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Currency { get; set; }

    public Dictionary<string, long> SpecialResources { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SkillRanks { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> UnlockedWorldIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> CompletedWorldIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> SeenTutorialEvents { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, WorldSaveData> Worlds { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SaveService
{
    public const int SupportedSchemaVersion = 3;
    public const string DefaultPath = "user://savegame_v3.json";
    public const string LegacyV2Path = "user://savegame_v2.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public GameSaveData LoadOrCreate(WorldCatalog worlds, string path = DefaultPath)
    {
        string sourcePath = path;
        if (!Godot.FileAccess.FileExists(sourcePath))
        {
            if (string.Equals(path, DefaultPath, StringComparison.Ordinal)
                && Godot.FileAccess.FileExists(LegacyV2Path))
            {
                sourcePath = LegacyV2Path;
            }
            else
            {
                return NewSave();
            }
        }

        string json = Godot.FileAccess.GetFileAsString(sourcePath);
        GameSaveData? data = JsonSerializer.Deserialize<GameSaveData>(json, _jsonOptions);
        if (data is null) throw new InvalidOperationException("Save file parsed to null.");

        bool migrated = false;
        if (data.SchemaVersion == 2)
        {
            MigrateSchema2(data, worlds);
            migrated = true;
        }
        else if (data.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported save schema {data.SchemaVersion}; expected {SupportedSchemaVersion} or migratable schema 2.");
        }

        Normalize(data, worlds);
        if (migrated || !string.Equals(sourcePath, path, StringComparison.Ordinal))
        {
            Save(data, path);
        }
        return data;
    }

    public void Save(GameSaveData data, string path = DefaultPath)
    {
        data.SchemaVersion = SupportedSchemaVersion;
        data.Currency = 0L;
        data.SavedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!string.IsNullOrWhiteSpace(data.CurrentWorldId)
            && data.Worlds.TryGetValue(data.CurrentWorldId, out WorldSaveData? activeWorld))
        {
            activeWorld.LastPlayedUnixSeconds = data.SavedAtUnixSeconds;
        }
        string json = JsonSerializer.Serialize(data, _jsonOptions);

        string tempPath = path + ".tmp";
        using (Godot.FileAccess file = Godot.FileAccess.Open(tempPath, Godot.FileAccess.ModeFlags.Write))
        {
            if (file is null) throw new InvalidOperationException($"Could not open temporary save file '{tempPath}'.");
            file.StoreString(json);
        }

        string absolute = ProjectSettings.GlobalizePath(path);
        string tempAbsolute = ProjectSettings.GlobalizePath(tempPath);
        System.IO.File.Move(tempAbsolute, absolute, overwrite: true);
    }

    public static GameSaveData NewSave()
        => new()
        {
            SchemaVersion = SupportedSchemaVersion,
            SavedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

    private static void MigrateSchema2(GameSaveData data, WorldCatalog worlds)
    {
        long legacyCurrency = Math.Max(0L, data.Currency);
        if (legacyCurrency > 0L
            && !string.IsNullOrWhiteSpace(data.CurrentWorldId)
            && worlds.Worlds.TryGetValue(data.CurrentWorldId, out WorldProfile? active)
            && active.UsesTutorialLocalWallet)
        {
            if (!data.Worlds.TryGetValue(active.Id, out WorldSaveData? world))
            {
                world = new WorldSaveData
                {
                    WorldId = active.Id,
                    WorldVersion = active.WorldVersion,
                    GenerationVersion = active.GenerationVersion,
                };
                data.Worlds[active.Id] = world;
            }
            world.TutorialLocalCurrency = Math.Max(world.TutorialLocalCurrency, legacyCurrency);
        }
        else
        {
            data.PersistentMainCurrency = Math.Max(data.PersistentMainCurrency, legacyCurrency);
        }

        data.Currency = 0L;
        data.SchemaVersion = SupportedSchemaVersion;
    }

    private static void Normalize(GameSaveData data, WorldCatalog worlds)
    {
        data.PersistentMainCurrency = Math.Max(0L, data.PersistentMainCurrency);
        data.Currency = 0L;
        data.SpecialResources ??= new Dictionary<string, long>(StringComparer.Ordinal);
        data.SkillRanks ??= new Dictionary<string, int>(StringComparer.Ordinal);
        data.UnlockedWorldIds ??= new HashSet<string>(StringComparer.Ordinal);
        data.CompletedWorldIds ??= new HashSet<string>(StringComparer.Ordinal);
        data.SeenTutorialEvents ??= new HashSet<string>(StringComparer.Ordinal);
        data.Worlds ??= new Dictionary<string, WorldSaveData>(StringComparer.Ordinal);

        foreach ((string worldId, WorldSaveData world) in data.Worlds)
        {
            world.WorldId = string.IsNullOrWhiteSpace(world.WorldId) ? worldId : world.WorldId;
            if (worlds.Worlds.TryGetValue(world.WorldId, out WorldProfile? profile))
            {
                if (world.WorldVersion <= 0) world.WorldVersion = profile.WorldVersion;
                if (world.GenerationVersion <= 0) world.GenerationVersion = profile.GenerationVersion;
            }
            world.TutorialLocalCurrency = Math.Max(0L, world.TutorialLocalCurrency);
            if (world.LastPlayedUnixSeconds <= 0) world.LastPlayedUnixSeconds = world.FirstStartedUnixSeconds;
            world.MinedChunks ??= new List<MinedChunkSnapshot>();
            world.ExhaustedRegions ??= new List<ExhaustedRegionSnapshot>();
            world.Miners ??= new List<MinerSnapshot>();
            if (world.Completed) data.CompletedWorldIds.Add(world.WorldId);
        }
    }
}
