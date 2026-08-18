using System;
using Godot;
using TenMillionBlocks.Core;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Gameplay;

public readonly record struct MiningFeedback(
    Vector3I Coordinate,
    BlockType Type,
    bool Destroyed,
    float HealthRatio,
    int Reward,
    bool Automated);

public sealed class MiningService
{
    public event Action<MiningFeedback>? Feedback;

    private readonly VoxelWorld _world;
    private readonly GameState _state;
    private readonly UpgradeSystem _upgrades;

    public MiningService(VoxelWorld world, GameState state, UpgradeSystem upgrades)
    {
        _world = world;
        _state = state;
        _upgrades = upgrades;
    }

    public bool Mine(Vector3I coordinate, bool automated)
    {
        float damage = automated ? _upgrades.AutoDamage : _upgrades.ManualDamage;
        BlockDamageResult result = _world.DamageBlock(coordinate, damage);
        if (!result.Hit)
        {
            return false;
        }

        int reward = 0;
        if (result.Destroyed)
        {
            reward = result.Reward;
            _state.RecordBlockMined(reward);
        }

        float healthRatio = result.MaxHealth <= 0.0f ? 0.0f : result.RemainingHealth / result.MaxHealth;
        Feedback?.Invoke(new MiningFeedback(
            coordinate,
            result.Type,
            result.Destroyed,
            healthRatio,
            reward,
            automated));

        return true;
    }
}
