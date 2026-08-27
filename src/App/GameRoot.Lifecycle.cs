using Godot;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;

        // Autosave normally runs on a short cadence, but closing the window immediately after mining or
        // buying something should not discard the last few seconds of progress. Reuse the same
        // authoritative capture/save path used by world transitions before allowing the process to exit.
        if (_sessionPersists && _world is not null)
        {
            CaptureCurrentSession();
            TrySaveCurrentSession(captureFirst: false);
        }

        GetTree().Paused = false;
        GetTree().Quit();
    }
}
