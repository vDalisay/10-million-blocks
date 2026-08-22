using Godot;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController
{
    /// <summary>
    /// Executes one normal manual-mining click at the center of the current viewport. It deliberately
    /// uses the same hover target resolution, footprint, MiningService path, dirty propagation and
    /// presentation effects as a real player click, so a benchmark can reproduce fully-upgraded manual
    /// mining pressure without synthetic direct state mutation.
    /// </summary>
    public int DiagnosticMineScreenCenter()
    {
        if (_camera?.Camera is null) return 0;

        Vector2 center = GetViewport().GetVisibleRect().Size * 0.5f;
        UpdateHover(center, force: true);
        if (_hoveredVoxel is null || _hoverTargets.Count == 0) return 0;

        int actions = MineManualTick(_hoverTargets, hoverMining: false);
        if (actions > 0)
        {
            _highlight.PulseMine();
            UpdateHover(center, force: true);
        }
        return actions;
    }
}
