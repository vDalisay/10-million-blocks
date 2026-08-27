using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Authoring;

public sealed class FrozenWorldManifest
{
    public int SchemaVersion { get; init; } = WorldFreezeService.ManifestSchemaVersion;
    public string WorldId { get; init; } = string.Empty;
    public int WorldVersion { get; init; }
    public int GenerationVersion { get; init; }
    public int Seed { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string OverrideFile { get; init; } = string.Empty;
    public long FrozenAtUnixSeconds { get; init; }
    public long MineableBlocks { get; init; }
    public long ExposedBlocks { get; init; }
    public long TreeCount { get; init; }
    public long GemCount { get; init; }
    public double WaterCoverage { get; init; }
    public double SoftTerrainCoverage { get; init; }
    public double ExposedStoneCoverage { get; init; }
}

/// <summary>
/// Versioned shipping-freeze backend. It hashes canonical profile + override JSON and refuses to
/// overwrite an existing frozen version. Replay compatibility uses the same hash routine, so a replay
/// cannot silently attach itself to a changed deterministic baseline.
/// </summary>
public static class WorldFreezeService
{
    public const int ManifestSchemaVersion = 1;

    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static FrozenWorldManifest BuildManifest(WorldProfile profile, int worldVersion)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (worldVersion <= 0) throw new ArgumentOutOfRangeException(nameof(worldVersion));

        string contentHash = ComputeContentHash(profile);
        WorldAuthoringMetrics metrics = WorldAuthoringAnalyzer.Analyze(profile);

        return new FrozenWorldManifest
        {
            WorldId = profile.Id,
            WorldVersion = worldVersion,
            GenerationVersion = profile.GenerationVersion,
            Seed = profile.Seed,
            ContentHash = contentHash,
            OverrideFile = profile.OverrideFile,
            FrozenAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            MineableBlocks = metrics.MineableBlocks,
            ExposedBlocks = metrics.ExposedBlocks,
            TreeCount = metrics.TreeCount,
            GemCount = metrics.GemCount,
            WaterCoverage = metrics.WaterCoverage,
            SoftTerrainCoverage = metrics.SoftTerrainCoverage,
            ExposedStoneCoverage = metrics.ExposedStoneCoverage,
        };
    }

    public static string ComputeContentHash(WorldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string canonicalProfile = CanonicalizeJson(JsonSerializer.Serialize(profile, ProfileJsonOptions));
        string canonicalOverride = string.Empty;
        if (!string.IsNullOrWhiteSpace(profile.OverrideFile))
        {
            if (!Godot.FileAccess.FileExists(profile.OverrideFile))
            {
                throw new InvalidOperationException(
                    $"Cannot hash '{profile.Id}': override file does not exist: {profile.OverrideFile}");
            }
            canonicalOverride = CanonicalizeJson(Godot.FileAccess.GetFileAsString(profile.OverrideFile));
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonicalProfile + "\n" + canonicalOverride))).ToLowerInvariant();
    }

    public static FrozenWorldManifest Freeze(
        WorldProfile profile,
        int worldVersion,
        string outputDirectory = "res://data/worlds/frozen")
    {
        FrozenWorldManifest manifest = BuildManifest(profile, worldVersion);
        string relative = $"{outputDirectory.TrimEnd('/')}/{profile.Id}_v{worldVersion}.json";
        if (Godot.FileAccess.FileExists(relative))
        {
            throw new InvalidOperationException(
                $"Frozen world manifest already exists and will not be overwritten: {relative}");
        }

        string directoryAbsolute = ProjectSettings.GlobalizePath(outputDirectory);
        System.IO.Directory.CreateDirectory(directoryAbsolute);
        using Godot.FileAccess file = Godot.FileAccess.Open(relative, Godot.FileAccess.ModeFlags.Write);
        if (file is null)
        {
            throw new InvalidOperationException($"Could not create frozen world manifest: {relative}");
        }

        file.StoreString(JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        return manifest;
    }

    public static string CanonicalizeJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = new System.Collections.Generic.List<JsonProperty>();
                foreach (JsonProperty property in element.EnumerateObject()) properties.Add(property);
                properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (JsonProperty property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidOperationException($"Unsupported JSON value kind {element.ValueKind} in canonical world data.");
        }
    }
}
