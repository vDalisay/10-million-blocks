using Godot;

namespace TenMillionBlocks.Automation;

public sealed class MinerInstance
{
    public long InstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public Vector3I Origin { get; init; }
    public Vector3I Direction { get; init; }
    public int CandidateIndex { get; set; }
    public long BlocksMined { get; set; }
    public double WorkAccumulator { get; set; }
    public bool Exhausted { get; set; }
}
