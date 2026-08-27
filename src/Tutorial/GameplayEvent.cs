using System;
using Godot;

namespace TenMillionBlocks.Tutorial;

public enum GameplayEventKind
{
    WorldStarted,
    FirstManualMine,
    HoverMiningUnlocked,
    FirstAreaMine,
    AutomationClassUnlocked,
    AutomationPlaced,
    AutomationStopped,
    ShovelStoppedByWater,
    ShovelStoppedByStone,
    TreeBlockedShovel,
    SpecialResourceFound,
    TransformationPurchased,
    LightningCharged,
    LightningImpact,
    MeteorSpawned,
    MeteorGrabbed,
    MeteorImpact,
    WorldCompleted,
}

public readonly record struct GameplayEvent(
    GameplayEventKind Kind,
    string WorldId,
    string Detail = "",
    Vector3I Voxel = default,
    long Amount = 0L);

/// <summary>
/// Session-scoped semantic event stream. Mechanics and tutorial wording remain independent: the game
/// can run with no TutorialDirector attached, and consumers may use the same events for analytics or
/// accessibility without changing simulation behavior.
/// </summary>
public sealed class GameplayEventHub
{
    public event Action<GameplayEvent>? EventPublished;

    public void Publish(GameplayEvent gameplayEvent)
        => EventPublished?.Invoke(gameplayEvent);
}
