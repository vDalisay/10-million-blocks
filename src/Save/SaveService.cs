using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.World.Storage;

namespace TenMillionBlocks.Save;

public sealed class WorldSaveData
{
    public string WorldId { get; set; } = string.Empty;
    public int GenerationVersion { get; set; }
    public long ManualBlocksMined { get; set; }
    public long AutomatedBlocksMined { get; set; }
    public bool HoverMiningEnabled { get; set; }
    public bool Completed { get; set; }
    public long FirstStartedUnixSeconds { get; set; }
    public long CompletedUnixSeconds { get; set; }
    public string ReplayFile { get; set; } = string.Empty;
    public List<MinedChunkSnapshot> MinedChunks { get; set; } = new();
    public List<ExhaustedRegionSnapshot> ExhaustedRegions { get; set; } = new();
    public List<MinerSnapshot> Miners { get; set; } = new();
}

public sealed class GameSaveData
{
    public int SchemaVersion { get; set; } = SaveService.SupportedSchemaVersion;
    public long SavedAtUnixSeconds { get; set; }
    public string CurrentWorldId { get; set; } = string.Empty;
    public long Currency { get; set; }
    public Dictionary<string, long> SpecialResources { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> SkillRanks { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> UnlockedWorldIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> CompletedWorldIds { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, WorldSaveData> Worlds { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SaveService
{
    public const int SupportedSchemaVersion = 2;
    public const string DefaultPath = "user://savegame_v2.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public GameSaveData LoadOrCreate(string path = DefaultPath)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            return NewSave();
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        GameSaveData? data = JsonSerializer.Deserialize<GameSaveData>(json, _jsonOptions);
        if (data is null)
        {
            throw new InvalidOperationException("Save file parsed to null.");
        }

        if (data.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported save schema {data.SchemaVersion}; expected {SupportedSchemaVersion}.");
        }

        data.SpecialResources ??= new Dictionary<string, long>(StringComparer.Ordinal);
        data.SkillRanks ??= new Dictionary<string, int>(StringComparer.Ordinal);
        data.UnlockedWorldIds ??= new HashSet<string>(StringComparer.Ordinal);
        data.CompletedWorldIds ??= new HashSet<string>(StringComparer.Ordinal);
        data.Worlds ??= new Dictionary<string, WorldSaveData>(StringComparer.Ordinal);
        foreach ((string worldId, WorldSaveData world) in data.Worlds)
        {
            world.WorldId = string.IsNullOrWhiteSpace(world.WorldId) ? worldId : world.WorldId;
            world.MinedChunks ??= new List<MinedChunkSnapshot>();
            world.ExhaustedRegions ??= new List<ExhaustedRegionSnapshot>();
            world.Miners ??= new List<MinerSnapshot>();
            if (world.Completed) data.CompletedWorldIds.Add(world.WorldId);
        }
        return data;
    }

    public void Save(GameSaveData data, string path = DefaultPath)
    {
        data.SchemaVersion = SupportedSchemaVersion;
        data.SavedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string json = JsonSerializer.Serialize(data, _jsonOptions);

        string tempPath = path + ".tmp";
        using (Godot.FileAccess file = Godot.FileAccess.Open(tempPath, Godot.FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                throw new InvalidOperationException($"Could not open temporary save file '{tempPath}'.");
            }
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
}
