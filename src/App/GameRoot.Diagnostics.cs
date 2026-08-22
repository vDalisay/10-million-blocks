using Godot;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    /// <summary>
    /// Rebuilds the non-persistent one-million debug world from its deterministic baseline. This is
    /// intentionally unavailable to normal progression worlds: repeated F11 runs must not accumulate
    /// old drill tunnels and then mistake a genuine through-hole for a renderer regression.
    /// </summary>
    public bool ReloadStressWorldForDiagnostics()
    {
        if (!OS.IsDebugBuild()
            || _world is null
            || !string.Equals(_world.Profile.Id, "stress_1000", System.StringComparison.Ordinal))
        {
            return false;
        }

        BuildWorldSession(
            _worlds.Get("stress_1000"),
            applyOfflineProgress: false,
            persistSession: false);
        GD.Print("Reloaded stress_1000 from a clean deterministic baseline for diagnostics.");
        return true;
    }
}
