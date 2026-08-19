using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TenMillionBlocks.Content;

public sealed class ContentDatabase
{
    public const int SupportedBlockSchemaVersion = 1;

    private readonly Dictionary<string, BlockDefinition> _blocks;

    public IReadOnlyDictionary<string, BlockDefinition> Blocks => _blocks;

    private ContentDatabase(Dictionary<string, BlockDefinition> blocks)
    {
        _blocks = blocks;
    }

    public static ContentDatabase Load(string blockCatalogPath = "res://data/blocks/blocks.json")
    {
        if (!Godot.FileAccess.FileExists(blockCatalogPath))
        {
            throw new InvalidOperationException($"Block catalog was not found: {blockCatalogPath}");
        }

        string json = Godot.FileAccess.GetFileAsString(blockCatalogPath);
        var document = JsonSerializer.Deserialize<BlockCatalogDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        if (document is null)
        {
            throw new InvalidOperationException($"Block catalog could not be parsed: {blockCatalogPath}");
        }

        if (document.SchemaVersion != SupportedBlockSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported block schema version {document.SchemaVersion}. Expected {SupportedBlockSchemaVersion}.");
        }

        var errors = new List<string>();
        var blocks = new Dictionary<string, BlockDefinition>(StringComparer.Ordinal);

        foreach (BlockDefinition block in document.Blocks)
        {
            ValidateBlock(block, errors);
            if (!string.IsNullOrWhiteSpace(block.Id) && !blocks.TryAdd(block.Id, block))
            {
                errors.Add($"Duplicate block id '{block.Id}'.");
            }
        }

        if (blocks.Count == 0)
        {
            errors.Add("Block catalog contains no blocks.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Block catalog validation failed:\n - " + string.Join("\n - ", errors));
        }

        GD.Print($"Loaded {blocks.Count} block definitions from {blockCatalogPath}.");
        return new ContentDatabase(blocks);
    }

    public BlockDefinition GetBlock(string id)
    {
        if (!_blocks.TryGetValue(id, out BlockDefinition? block))
        {
            throw new KeyNotFoundException($"Unknown block id '{id}'.");
        }

        return block;
    }

    private static void ValidateBlock(BlockDefinition block, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(block.Id))
        {
            errors.Add("Block has an empty id.");
        }
        else if (block.Id.Any(char.IsWhiteSpace))
        {
            errors.Add($"Block id '{block.Id}' contains whitespace.");
        }

        if (string.IsNullOrWhiteSpace(block.DisplayName))
        {
            errors.Add($"Block '{block.Id}' has no display name.");
        }

        if (string.IsNullOrWhiteSpace(block.AssetPath) || !block.AssetPath.StartsWith("res://", StringComparison.Ordinal))
        {
            errors.Add($"Block '{block.Id}' has invalid asset path '{block.AssetPath}'.");
        }

        if (block.Hardness <= 0.0f)
        {
            errors.Add($"Block '{block.Id}' must have hardness > 0.");
        }

        if (block.BaseValue < 0)
        {
            errors.Add($"Block '{block.Id}' cannot have a negative base value.");
        }
    }
}
