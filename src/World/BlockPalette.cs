using Godot;

namespace TenMillionBlocks.World;

public readonly record struct BlockDefinition(Color Color, float Hardness, int Reward);

public static class BlockPalette
{
    public static BlockDefinition Get(BlockType type)
        => type switch
        {
            BlockType.Grass => new(new Color(0.29f, 0.72f, 0.20f), 1.0f, 1),
            BlockType.Dirt => new(new Color(0.48f, 0.29f, 0.14f), 1.6f, 1),
            BlockType.Stone => new(new Color(0.48f, 0.52f, 0.55f), 3.0f, 2),
            BlockType.Sand => new(new Color(0.83f, 0.74f, 0.44f), 1.2f, 1),
            BlockType.Water => new(new Color(0.14f, 0.45f, 0.76f), 1.0f, 2),
            BlockType.Crystal => new(new Color(0.24f, 0.86f, 0.95f), 5.0f, 10),
            _ => new(new Color(1, 0, 1), 1.0f, 1),
        };
}
