using System;

namespace TenMillionBlocks.WorldEvents;

public readonly record struct WorldEventSnapshot(
    int CloudCharge,
    float CloudPhase,
    float MeteorPhase,
    double MeteorCooldownSeconds,
    double MeteorWindowSeconds,
    bool MeteorActive);

public partial class WorldEventController
{
    public event Action? PersistentStateChanged;

    public WorldEventSnapshot CreateSnapshot()
        => new(
            _cloudCharge,
            _cloudPhase,
            _meteorPhase,
            Math.Max(0.0, _meteorCooldown),
            Math.Max(0.0, _meteorWindow),
            _meteor is not null);

    public void RestoreSnapshot(WorldEventSnapshot snapshot)
    {
        _cloudCharge = Math.Clamp(snapshot.CloudCharge, 0, CloudClicksToCharge);
        _cloudPhase = Mathf.Wrap(snapshot.CloudPhase, 0.0f, Mathf.Tau);
        _meteorPhase = Mathf.Wrap(snapshot.MeteorPhase, 0.0f, Mathf.Tau);
        _meteorCooldown = Math.Max(0.0, snapshot.MeteorCooldownSeconds);
        _meteorWindow = Math.Max(0.0, snapshot.MeteorWindowSeconds);
        _meteorGrabbed = false;
        _impactVoxel = null;
        _impactProgress = 0.0;

        RefreshCloudMaterial();
        if (_meteorEnabled && snapshot.MeteorActive)
        {
            if (_meteor is null) SpawnMeteor();
            if (snapshot.MeteorWindowSeconds > 0.0)
            {
                _meteorWindow = snapshot.MeteorWindowSeconds;
            }
        }
        else if (_meteor is not null)
        {
            _meteor.QueueFree();
            _meteor = null;
        }

        RefreshStatus();
    }

    /// <summary>
    /// Called by the gameplay host after a player-visible event transition (charge/catch/strike) to
    /// request an ordinary autosave. Keeping the event state in the world save prevents save/load
    /// from rerolling a meteor opportunity or resetting a partially charged cloud.
    /// </summary>
    public void RequestPersistence()
        => PersistentStateChanged?.Invoke();
}
