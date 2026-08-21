using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Generation;

public sealed class WorldVoxelOverrideDefinition
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Present { get; set; } = true;
    public string BlockId { get; set; } = string.Empty;
    public bool Mineable { get; set; } = true;
}

public sealed class WorldFeatureOverrideDefinition
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Present { get; set; } = true;
    public string BlockId { get; set; } = "tree";
    public int NormalX { get; set; }
    public int NormalY { get; set; } = 1;
    public int NormalZ { get; set; }
}

/// <summary>
/// Sparse authored corrections layered over a deterministic base generator. Approved worlds keep a
/// compact set of hand-authored replacements/carves plus support-owned surface features without
/// materializing the untouched cube into save or content data. Feature entries may explicitly suppress
/// a generated feature, which lets the authoring tool remove an unwanted procedural tree without
/// changing the generator or filling the world with special-case voxel data.
/// </summary>
public sealed class WorldOverrideSet
{
    public const int SupportedSchemaVersion = 1;
    public const string ResourceRootEnvironmentVariable = "TEN_MILLION_BLOCKS_RESOURCE_ROOT";

    private sealed class Document
    {
        public int SchemaVersion { get; set; }
        public string WorldId { get; set; } = string.Empty;
        public int GenerationVersion { get; set; }
        public List<WorldVoxelOverrideDefinition> Overrides { get; set; } = new();
        public List<WorldFeatureOverrideDefinition> Features { get; set; } = new();
    }

    private readonly Dictionary<Vector3I, BlockSample> _voxels;
    private readonly Dictionary<Vector3I, FeatureSample> _features;
    private readonly HashSet<Vector3I> _suppressedFeatures;

    private WorldOverrideSet(
        Dictionary<Vector3I, BlockSample> voxels,
        Dictionary<Vector3I, FeatureSample> features,
        HashSet<Vector3I> suppressedFeatures)
    {
        _voxels = voxels;
        _features = features;
        _suppressedFeatures = suppressedFeatures;
    }

    public int Count => _voxels.Count;
    public int FeatureCount => _features.Count;
    public int SuppressedFeatureCount => _suppressedFeatures.Count;

    public static WorldOverrideSet? Load(WorldProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.OverrideFile)) return null;

        string json = ReadOverrideJson(profile.OverrideFile);
        Document? document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (document is null || document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"World override file '{profile.OverrideFile}' is unreadable or has an unsupported schema.");
        }

        document.Overrides ??= new List<WorldVoxelOverrideDefinition>();
        document.Features ??= new List<WorldFeatureOverrideDefinition>();

        if (!string.Equals(document.WorldId, profile.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"World override file '{profile.OverrideFile}' targets '{document.WorldId}', expected '{profile.Id}'.");
        }
        if (document.GenerationVersion != profile.GenerationVersion)
        {
            throw new InvalidOperationException(
                $"World override file '{profile.OverrideFile}' targets generation {document.GenerationVersion}, " +
                $"expected {profile.GenerationVersion}.");
        }

        var voxels = new Dictionary<Vector3I, BlockSample>();
        foreach (WorldVoxelOverrideDefinition item in document.Overrides)
        {
            var coordinate = new Vector3I(item.X, item.Y, item.Z);
            if (voxels.ContainsKey(coordinate))
            {
                throw new InvalidOperationException(
                    $"World override file '{profile.OverrideFile}' contains duplicate voxel {coordinate}.");
            }
            if (item.Present && string.IsNullOrWhiteSpace(item.BlockId))
            {
                throw new InvalidOperationException(
                    $"World override {coordinate} is present but has no block id.");
            }

            voxels.Add(coordinate, item.Present
                ? new BlockSample(true, item.BlockId, item.Mineable)
                : BlockSample.Empty);
        }

        var features = new Dictionary<Vector3I, FeatureSample>();
        var suppressedFeatures = new HashSet<Vector3I>();
        var seenFeatures = new HashSet<Vector3I>();
        foreach (WorldFeatureOverrideDefinition item in document.Features)
        {
            var anchor = new Vector3I(item.X, item.Y, item.Z);
            if (!seenFeatures.Add(anchor))
            {
                throw new InvalidOperationException(
                    $"World override file '{profile.OverrideFile}' contains duplicate feature anchor {anchor}.");
            }

            if (!item.Present)
            {
                suppressedFeatures.Add(anchor);
                continue;
            }

            var normal = new Vector3I(item.NormalX, item.NormalY, item.NormalZ);
            if (string.IsNullOrWhiteSpace(item.BlockId))
            {
                throw new InvalidOperationException($"World feature {anchor} has no block id.");
            }
            if (Math.Abs(normal.X) + Math.Abs(normal.Y) + Math.Abs(normal.Z) != 1)
            {
                throw new InvalidOperationException(
                    $"World feature {anchor} must use a cardinal outward normal, got {normal}.");
            }

            features.Add(anchor, new FeatureSample(item.BlockId, anchor, normal));
        }

        GD.Print(
            $"Loaded {voxels.Count} sparse voxel overrides, {features.Count} authored features and " +
            $"{suppressedFeatures.Count} feature suppressions for '{profile.Id}'.");
        return new WorldOverrideSet(voxels, features, suppressedFeatures);
    }

    public BlockSample Apply(Vector3I coordinate, BlockSample generated)
        => _voxels.TryGetValue(coordinate, out BlockSample authored) ? authored : generated;

    public bool TryGet(Vector3I coordinate, out BlockSample sample)
        => _voxels.TryGetValue(coordinate, out sample);

    public bool TryGetFeature(Vector3I anchor, out FeatureSample feature)
    {
        if (_features.TryGetValue(anchor, out feature)) return true;
        if (_suppressedFeatures.Contains(anchor))
        {
            // ProceduralWorldSource treats any authored non-tree feature result as a deliberate
            // rejection before it reaches generated feature fallback. The sentinel therefore means
            // "there is intentionally no feature here" without widening the runtime feature type.
            feature = new FeatureSample("__suppressed__", anchor, Vector3I.Up);
            return true;
        }

        feature = default;
        return false;
    }

    public bool SuppressesFeature(Vector3I anchor)
        => _suppressedFeatures.Contains(anchor);

    private static string ReadOverrideJson(string resourcePath)
    {
        // Console contract tools reference the same runtime assembly but do not boot the Godot engine.
        // Calling Godot.FileAccess from such a process can enter native engine code without an initialized
        // project and terminate the process. Tools therefore opt into a managed res:// root explicitly;
        // normal gameplay/export behavior still uses FileAccess so packed resources continue to work.
        string? managedRoot = System.Environment.GetEnvironmentVariable(ResourceRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(managedRoot)
            && resourcePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            string relative = resourcePath["res://".Length..]
                .Replace('/', Path.DirectorySeparatorChar);
            string absolute = Path.Combine(managedRoot, relative);
            if (!File.Exists(absolute))
            {
                throw new InvalidOperationException($"World override file was not found: {absolute}");
            }
            return File.ReadAllText(absolute);
        }

        if (!Godot.FileAccess.FileExists(resourcePath))
        {
            throw new InvalidOperationException($"World override file was not found: {resourcePath}");
        }
        return Godot.FileAccess.GetFileAsString(resourcePath);
    }
}
