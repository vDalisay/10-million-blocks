namespace TenMillionBlocks.Presentation;

public partial class OrbitCameraController
{
    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (GraphicsSettingsRuntime.Current?.IdleCameraOrbitEnabled == false)
        {
            // The normal _Process implementation only starts ambient orbit after 30 seconds of mouse
            // inactivity. Keeping that timer at zero disables the showcase motion without touching any
            // manual orbit/pan/zoom behavior. Re-enabling the preference starts a fresh 30-second wait.
            _mouseIdleSeconds = 0.0;
        }
    }
}
