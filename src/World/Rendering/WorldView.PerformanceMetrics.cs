namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Cumulative CPU time spent constructing chunk presentation since this WorldView was created.
    /// Diagnostics use the delta around a benchmark run so initial loading does not pollute the result.
    /// </summary>
    public double TotalChunkBuildMilliseconds => _chunkBuildTotalMilliseconds;
}
