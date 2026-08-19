using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TenMillionBlocks.Content;

public sealed class BlockCatalogDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("blocks")]
    public List<BlockDefinition> Blocks { get; init; } = [];
}

public sealed class BlockDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("asset_path")]
    public string AssetPath { get; init; } = string.Empty;

    [JsonPropertyName("hardness")]
    public float Hardness { get; init; } = 1.0f;

    [JsonPropertyName("base_value")]
    public long BaseValue { get; init; } = 1;

    [JsonPropertyName("render_class")]
    public string RenderClass { get; init; } = "terrain";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}
