using Godot;
using TenMillionBlocks.Core;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Gameplay;

public sealed partial class AutoMiningController : Node
{
    private readonly RandomNumberGenerator _rng = new();

    private VoxelWorld? _world;
    private MiningService? _mining;
    private UpgradeSystem? _upgrades;
    private float _timer;
    private bool _enabled = true;

    public void Initialize(VoxelWorld world, MiningService mining, UpgradeSystem upgrades, int seed)
    {
        _world = world;
        _mining = mining;
        _upgrades = upgrades;
        _rng.Seed = (ulong)(uint)seed;
    }

    public override void _Process(double delta)
    {
        if (!_enabled || _world is null || _mining is null || _upgrades is null || _upgrades.AutoBatchSize <= 0)
        {
            return;
        }

        _timer -= (float)delta;
        if (_timer > 0.0f)
        {
            return;
        }

        _timer += GameConfig.AutoMinerTickSeconds;

        int actions = _upgrades.AutoBatchSize;
        for (int i = 0; i < actions; i++)
        {
            if (!_world.TryGetRandomSurfaceBlock(_rng, out Vector3I target))
            {
                break;
            }

            _mining.Mine(target, automated: true);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _timer = 0.0f;
        }
    }

    public void Reseed(int seed)
        => _rng.Seed = (ulong)(uint)seed;
}
