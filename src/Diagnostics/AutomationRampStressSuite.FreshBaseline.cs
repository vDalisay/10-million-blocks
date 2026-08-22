using Godot;
using TenMillionBlocks.App;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Diagnostics;

public partial class AutomationRampStressSuite
{
    /// <summary>
    /// F11 is a comparative benchmark, so repeated runs must not inherit the previous run's tunnels.
    /// The old behavior made a second/third run progressively hollow the same non-persistent stress cube;
    /// a genuine line-miner tunnel through the cube could then be mistaken for missing renderer geometry.
    ///
    /// _Input runs before _UnhandledKeyInput. Only intercept a new F11 start when stress_1000 already has
    /// mined state. Cancellation while running/waiting continues through the existing unhandled-input path.
    /// After GameRoot rebuilds the deterministic stress session, defer the normal TryStart() one idle turn
    /// so it resolves the replacement nodes and then uses its existing initial-presentation readiness gate.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!OS.IsDebugBuild()
            || _running
            || _pendingStart
            || @event is not InputEventKey key
            || !key.Pressed
            || key.Echo
            || key.Keycode != Key.F11)
        {
            return;
        }

        Node? session = GetTree().Root.FindChild("WorldSession_stress_1000", recursive: true, owned: false);
        WorldView? existingView = session?.GetNodeOrNull<WorldView>("WorldView");
        if (existingView is null
            || existingView.DiagnosticWorldId != "stress_1000"
            || existingView.DiagnosticMinedVoxelCount <= 0)
        {
            return;
        }

        GameRoot? gameRoot = GetTree().CurrentScene as GameRoot
            ?? GetTree().Root.FindChild("GameRoot", recursive: true, owned: false) as GameRoot;
        if (gameRoot?.ReloadStressWorldForDiagnostics() != true)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        GD.Print(
            $"F11 discarded an already-mined stress_1000 state ({existingView.DiagnosticMinedVoxelCount:N0} removals) " +
            "and rebuilt a clean deterministic baseline before benchmarking.");

        Callable.From(() => TryStart()).CallDeferred();
    }
}
