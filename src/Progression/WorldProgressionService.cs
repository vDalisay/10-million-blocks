using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.Progression;

public sealed class WorldProgressionDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> WorldIds { get; set; } = new();
}

public sealed class WorldProgressionService
{
    private readonly WorldProgressionDefinition _definition;
    private readonly WorldCatalog _worlds;

    private WorldProgressionService(WorldProgressionDefinition definition, WorldCatalog worlds)
    {
        _definition = definition;
        _worlds = worlds;
    }

    public int CurrentIndex { get; private set; }
    public string CurrentWorldId => _definition.WorldIds[CurrentIndex];
    public bool HasNext => CurrentIndex + 1 < _definition.WorldIds.Count;
    public string? NextWorldId => HasNext ? _definition.WorldIds[CurrentIndex + 1] : null;

    public static WorldProgressionService Load(
        WorldCatalog worlds,
        string path = "res://data/progression/world_progression.json")
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new InvalidOperationException($"World progression file was not found: {path}");
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        WorldProgressionDefinition? definition = JsonSerializer.Deserialize<WorldProgressionDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        if (definition is null || definition.SchemaVersion != 1)
        {
            throw new InvalidOperationException("World progression has an unsupported or unreadable schema.");
        }

        if (definition.WorldIds.Count == 0)
        {
            throw new InvalidOperationException("World progression contains no worlds.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string worldId in definition.WorldIds)
        {
            _ = worlds.Get(worldId);
            if (!seen.Add(worldId))
            {
                throw new InvalidOperationException($"World progression contains duplicate world id '{worldId}'.");
            }
        }

        return new WorldProgressionService(definition, worlds);
    }

    public WorldProfile CurrentProfile() => _worlds.Get(CurrentWorldId);
    public WorldProfile? NextProfile() => NextWorldId is string id ? _worlds.Get(id) : null;

    public bool Advance()
    {
        if (!HasNext) return false;
        CurrentIndex++;
        return true;
    }

    public void RestoreWorld(string? worldId)
    {
        if (string.IsNullOrWhiteSpace(worldId))
        {
            CurrentIndex = 0;
            return;
        }

        int index = _definition.WorldIds.FindIndex(id => id.Equals(worldId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException($"Save references unavailable world '{worldId}'.");
        }

        CurrentIndex = index;
    }
}
