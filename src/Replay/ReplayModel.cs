using System;
using System.Collections.Generic;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Replay;

public enum ReplayEventKind : byte
{
    RemoveVoxel = 1,
    RemoveVoxelBatch = 2,
}

public enum ReplayMiningSource : byte
{
    Manual = 0,
    Automation = 1,
    WorldEvent = 2,
    Other = 3,
}

public readonly record struct ReplayRemovalEvent(
    uint Tick,
    long LinearIndex,
    ReplayMiningSource Source);

public sealed class ReplayHeader
{
    // Schema 3 adds packed same-tick/same-source RemoveVoxelBatch records. The logical decoded event
    // model remains a flat removal stream, so playback and old schema-1/2 files need no migration.
    public const int CurrentSchemaVersion = 3;
    public const int MinimumReadableSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string WorldId { get; init; } = string.Empty;
    public int WorldVersion { get; init; }
    public int GenerationVersion { get; init; }
    public string WorldContentHash { get; init; } = string.Empty;
    public int MinCoordinate { get; init; }
    public int AxisSize { get; init; }
    public int TickRate { get; init; } = 20;
    public long EventCount { get; init; }
    public long FinalMinedCount { get; init; }
    public byte[] EventChecksum { get; init; } = Array.Empty<byte>();

    public bool HasFrozenBaselineIdentity
        => SchemaVersion >= 2 && WorldVersion > 0 && !string.IsNullOrWhiteSpace(WorldContentHash);
}

public sealed class ReplayData
{
    public ReplayHeader Header { get; init; } = new();
    public IReadOnlyList<ReplayRemovalEvent> Events { get; init; } = Array.Empty<ReplayRemovalEvent>();
}

public static class ReplaySourceMapper
{
    public static ReplayMiningSource FromMiningSource(MiningSource source)
        => source switch
        {
            MiningSource.Manual => ReplayMiningSource.Manual,
            MiningSource.Automated or MiningSource.Offline => ReplayMiningSource.Automation,
            MiningSource.WorldEvent => ReplayMiningSource.WorldEvent,
            _ => ReplayMiningSource.Other,
        };
}
