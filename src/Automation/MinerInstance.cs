using Godot;

namespace TenMillionBlocks.Automation;

public sealed class MinerInstance
{
    public long InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public Vector3I Origin { get; init; }
    public Vector3I Direction { get; init; }
    public Vector3I LastMinedVoxel { get; set; }
    public int CandidateIndex { get; set; }
    public long BlocksMined { get; set; }
    public double WorkAccumulator { get; set; }
    public bool Exhausted { get; set; }
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
}
