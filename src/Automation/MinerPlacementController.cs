using Godot;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Automation;

public partial class MinerPlacementController : Node
{
    private ManualMiningController _manual = null!;
    private MinerSimulationService _miners = null!;

    public bool InputEnabled { get; set; } = true;

    public void Initialize(ManualMiningController manual, MinerSimulationService miners)
    {
        _manual = manual;
        _miners = miners;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!InputEnabled
            || @event is not InputEventKey key
            || !key.Pressed
            || key.Echo)
        {
            return;
        }

        string? minerId = key.Keycode switch
        {
            Key.M => "line_miner",
            Key.N => "shovel_miner",
            Key.B => "disc_miner",
            _ => null,
        };
        if (minerId is null)
        {
            return;
        }

        if (_manual.HoveredVoxel is not Vector3I voxel)
        {
            GD.Print($"Placement '{minerId}' ignored: no exposed block is under the cursor.");
            GetViewport().SetInputAsHandled();
            return;
        }

        MinerInstance? placed = _miners.PlaceMiner(minerId, voxel);
        if (placed is null)
        {
            GD.Print($"Could not place '{minerId}' on {voxel}. Check the unlock and tool-specific material requirement.");
        }

        // Placement keys are commands, not mining input. Consume them whether placement succeeds or
        // fails so another controller cannot interpret the same key press.
        GetViewport().SetInputAsHandled();
    }
}
