using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController
{
    /// <summary>
    /// Automation placement reuses the cheap MultiMesh selection renderer but supplies its own physical
    /// footprint instead of showing the player's current manual-mining footprint.
    /// </summary>
    public void ShowPlacementHighlight(IReadOnlyList<Vector3I> voxels)
    {
        _highlight.ShowVoxels(voxels);
    }

    public void HidePlacementHighlight()
    {
        _highlight.HideVoxel();
    }

    public void RestoreMiningHighlight()
    {
        _hoverRayCacheValid = false;
        if (!InputEnabled)
        {
            ClearHover();
            return;
        }

        UpdateHover(GetViewport().GetMousePosition(), force: true);
    }
}
