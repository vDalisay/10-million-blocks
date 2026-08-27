using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    /// <summary>
    /// Fills a reusable list with the surface cells occupied by the current automation presentation.
    /// Shovels/axes/pickaxes/base drills occupy one cell. A transformed Wide Bore drill occupies the
    /// full 3x3 face perpendicular to its drilling direction. This is presentation-only and does not
    /// alter the existing placement-validity contract.
    /// </summary>
    public void FillPlacementFootprint(string definitionId, Vector3I surfaceVoxel, List<Vector3I> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        MinerDefinition definition = _catalog.Get(definitionId);
        int width = Math.Max(1, (int)MathF.Round(DrillFootprint(definition)));
        if (width <= 1)
        {
            output.Add(surfaceVoxel);
            return;
        }

        Vector3I outward = _world.Source.GetOutwardNormal(surfaceVoxel);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);
        int radius = width / 2;
        for (int a = -radius; a <= radius; a++)
        for (int b = -radius; b <= radius; b++)
        {
            output.Add(surfaceVoxel + tangentA * a + tangentB * b);
        }
    }
}
