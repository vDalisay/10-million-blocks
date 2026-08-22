using System;

namespace TenMillionBlocks.Automation;

/// <summary>
/// Giant voxel worlds need a simulation-distance style CPU budget: authoritative automation may build a
/// backlog, but one rendered frame must never try to catch up an arbitrary amount of work. The existing
/// round-robin scheduler already preserves WorkAccumulator for later frames, so lowering its work-unit
/// ceiling on million-block profiles changes only catch-up scheduling under extreme load, not normal
/// machine rates.
///
/// A wide drill can remove up to nine blocks in one work unit. 24 work units therefore bounds the normal
/// worst case to roughly 216 exact removals in one frame while still allowing over 12,000 removals/sec at
/// 60 FPS -- far above authored automation rates. Smaller/demo worlds keep the original ceiling.
/// </summary>
public partial class MinerSimulationService
{
    private const int MillionBlockMaxWorkUnitsPerFrame = 24;
    private const int LargeWorldMaxWorkUnitsPerFrame = 48;

    public override void _Ready()
    {
        long target = _world.Profile.TargetMineableBlocks;
        long logicalVolume = checked(
            (long)_world.Profile.LogicalWidth
            * _world.Profile.LogicalHeight
            * _world.Profile.LogicalDepth);

        if (target >= 1_000_000L || logicalVolume >= 1_000_000L)
        {
            MaxMiningOperationsPerFrame = Math.Min(MaxMiningOperationsPerFrame, MillionBlockMaxWorkUnitsPerFrame);
        }
        else if (target >= 100_000L || logicalVolume >= 100_000L)
        {
            MaxMiningOperationsPerFrame = Math.Min(MaxMiningOperationsPerFrame, LargeWorldMaxWorkUnitsPerFrame);
        }
    }
}
