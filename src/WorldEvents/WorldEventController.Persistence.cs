using System;
using Godot;

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
    private double _persistencePulse;
    private bool _semanticBaselineCaptured;
    private int _lastSemanticCloudCharge;
    private bool _lastSemanticMeteorActive;
    private bool _lastSemanticMeteorGrabbed;
    private Vector3I? _lastSemanticImpactVoxel;

    public event Action? PersistentStateChanged;
    public event Action? LightningCharged;
    public event Action? LightningImpact;
    public event Action? MeteorSpawned;
    public event Action? MeteorGrabbed;
    public event Action? MeteorImpact;

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

        // Restoring an opportunity must not masquerade as a newly spawned/charged gameplay event.
        _semanticBaselineCaptured = false;
        RefreshStatus();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_cloudEnabled && !_meteorEnabled) return;

        ObserveSemanticTransitions();

        _persistencePulse += Math.Max(0.0, delta);
        if (_persistencePulse < 5.0) return;
        _persistencePulse %= 5.0;
        PersistentStateChanged?.Invoke();
    }

    public void RequestPersistence()
        => PersistentStateChanged?.Invoke();

    private void ObserveSemanticTransitions()
    {
        bool meteorActive = _meteor is not null;
        if (!_semanticBaselineCaptured)
        {
            _lastSemanticCloudCharge = _cloudCharge;
            _lastSemanticMeteorActive = meteorActive;
            _lastSemanticMeteorGrabbed = _meteorGrabbed;
            _lastSemanticImpactVoxel = _impactVoxel;
            _semanticBaselineCaptured = true;
            return;
        }

        bool changed = false;

        if (_cloudCharge != _lastSemanticCloudCharge)
        {
            // ChargeCloud increments to five and resolves/reset-to-zero synchronously. The observable
            // transition from a near-full charge to zero therefore means the strike actually fired.
            if (_lastSemanticCloudCharge >= CloudClicksToCharge - 1 && _cloudCharge == 0)
            {
                LightningCharged?.Invoke();
                LightningImpact?.Invoke();
            }
            changed = true;
        }

        if (!_lastSemanticMeteorActive && meteorActive)
        {
            MeteorSpawned?.Invoke();
            changed = true;
        }

        if (!_lastSemanticMeteorGrabbed && _meteorGrabbed)
        {
            MeteorGrabbed?.Invoke();
            changed = true;
        }

        if (_lastSemanticImpactVoxel is not null && !meteorActive)
        {
            // A timed-out meteor has no impact voxel. Only a meteor that spent frames travelling to
            // an accepted impact target can produce this transition.
            MeteorImpact?.Invoke();
            changed = true;
        }

        _lastSemanticCloudCharge = _cloudCharge;
        _lastSemanticMeteorActive = meteorActive;
        _lastSemanticMeteorGrabbed = _meteorGrabbed;
        _lastSemanticImpactVoxel = _impactVoxel;

        if (changed)
        {
            PersistentStateChanged?.Invoke();
        }
    }
}
