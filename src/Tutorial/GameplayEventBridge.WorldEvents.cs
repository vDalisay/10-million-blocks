using System;
using TenMillionBlocks.WorldEvents;

namespace TenMillionBlocks.Tutorial;

public partial class GameplayEventBridge
{
    private WorldEventController? _worldEvents;

    public void AttachWorldEvents(WorldEventController worldEvents)
    {
        ArgumentNullException.ThrowIfNull(worldEvents);
        if (ReferenceEquals(_worldEvents, worldEvents)) return;
        DetachWorldEvents();
        _worldEvents = worldEvents;
        _worldEvents.LightningCharged += OnLightningCharged;
        _worldEvents.LightningImpact += OnLightningImpact;
        _worldEvents.MeteorSpawned += OnMeteorSpawned;
        _worldEvents.MeteorGrabbed += OnMeteorGrabbed;
        _worldEvents.MeteorImpact += OnMeteorImpact;
    }

    private void DetachWorldEvents()
    {
        if (_worldEvents is null) return;
        _worldEvents.LightningCharged -= OnLightningCharged;
        _worldEvents.LightningImpact -= OnLightningImpact;
        _worldEvents.MeteorSpawned -= OnMeteorSpawned;
        _worldEvents.MeteorGrabbed -= OnMeteorGrabbed;
        _worldEvents.MeteorImpact -= OnMeteorImpact;
        _worldEvents = null;
    }

    private void OnLightningCharged()
        => _hub.Publish(new GameplayEvent(GameplayEventKind.LightningCharged, _profile.Id));

    private void OnLightningImpact()
        => _hub.Publish(new GameplayEvent(GameplayEventKind.LightningImpact, _profile.Id));

    private void OnMeteorSpawned()
        => _hub.Publish(new GameplayEvent(GameplayEventKind.MeteorSpawned, _profile.Id));

    private void OnMeteorGrabbed()
        => _hub.Publish(new GameplayEvent(GameplayEventKind.MeteorGrabbed, _profile.Id));

    private void OnMeteorImpact()
        => _hub.Publish(new GameplayEvent(GameplayEventKind.MeteorImpact, _profile.Id));
}
