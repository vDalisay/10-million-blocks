using System;
using Godot;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// One process callback advances all pooled manual-mine pop visuals owned by a WorldView. Keeping this
/// separate from the main renderer loop avoids coupling chunk scheduling to cosmetic feedback while
/// still replacing dozens of transient per-node tweens/process callbacks with one lightweight ticker.
/// </summary>
public partial class WorldMiningFeedbackTicker : Node
{
    public Action<double>? Tick { get; set; }

    public override void _Process(double delta)
    {
        Tick?.Invoke(delta);
    }
}
