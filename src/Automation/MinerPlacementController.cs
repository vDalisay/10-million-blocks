using Godot;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Automation;

public partial class MinerPlacementController : Node
{
    private ManualMiningController _manual = null!;
    private MinerSimulationService _miners = null!;

    public void Initialize(ManualMiningController manual, MinerSimulationService miners)
    {
        _manual = manual;
        _miners = miners;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.M)
        {
            return;
        }

        if (_manual.HoveredVoxel is Vector3I voxel && _miners.PlaceLineMiner(voxel) is not null)
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
