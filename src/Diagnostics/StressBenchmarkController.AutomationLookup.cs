using TenMillionBlocks.Automation;

namespace TenMillionBlocks.Diagnostics;

public partial class StressBenchmarkController
{
    public override void _Ready()
    {
        // GameRoot initializes this controller before adding it to the world-session tree, so resolve
        // the sibling automation service once the parent exists. This keeps the benchmark report aware
        // of fleet size/presentation/rate without expanding the world-session construction API.
        _miners ??= GetParent()?.GetNodeOrNull<MinerSimulationService>("MinerSimulation");
    }
}
