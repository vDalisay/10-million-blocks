namespace TenMillionBlocks.Core;

public static class GameConfig
{
    public static readonly int[] StageBlockCounts = [1, 100, 1_000, 10_000];

    public const int ChunkSize = 8;
    public const float BlockSize = 1.0f;
    public const float ManualMineDistance = 120.0f;
    public const float BaseManualCooldownSeconds = 0.24f;
    public const float AutoMinerTickSeconds = 0.55f;
    public const int MaxDirtyChunksPerFrame = 10;

    public static int StageCompletionBonus(int targetBlocks)
        => targetBlocks switch
        {
            <= 1 => 8,
            <= 100 => 30,
            <= 1_000 => 160,
            _ => 1_200,
        };
}
