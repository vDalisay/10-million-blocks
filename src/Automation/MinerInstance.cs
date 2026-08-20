using Godot;

namespace TenMillionBlocks.Automation;

public enum MinerStopReason
{
    None = 0,
    RangeComplete = 1,
    BlockedMaterial = 2,
    NoReachableTarget = 3,
    NoTreeTarget = 4,
}

public sealed class MinerInstance
{
    public long InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;

    // Origin/direction are mutable because a stopped automation can be picked up and moved to a new
    // surface. Instance identity is preserved so save/HUD references continue to refer to the same unit.
    public Vector3I Origin { get; set; }
    public Vector3I Direction { get; set; }
    public Vector3I LastMinedVoxel { get; set; }
    public int CandidateIndex { get; set; }
    public long BlocksMined { get; set; }
    public double WorkAccumulator { get; set; }
    public bool Exhausted { get; set; }
    public MinerStopReason StopReason { get; set; }
    public Vector3I BlockedVoxel { get; set; }
    public string BlockedBlockId { get; set; } = string.Empty;

    public bool IsStopped => Exhausted;
}

public sealed class MinerSnapshot
{
    public long InstanceId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }
    public int DirectionX { get; set; }
    public int DirectionY { get; set; }
    public int DirectionZ { get; set; }
    public int LastX { get; set; }
    public int LastY { get; set; }
    public int LastZ { get; set; }
    public int CandidateIndex { get; set; }
    public long BlocksMined { get; set; }
    public double WorkAccumulator { get; set; }
    public bool Exhausted { get; set; }
    public MinerStopReason StopReason { get; set; }
    public int BlockedX { get; set; }
    public int BlockedY { get; set; }
    public int BlockedZ { get; set; }
    public string BlockedBlockId { get; set; } = string.Empty;
}
