using System;

namespace TenMillionBlocks.Core;

public enum UpgradeKind
{
    PickaxePower,
    MiningSpeed,
    AutoMiners,
}

public sealed class UpgradeSystem
{
    public event Action? Changed;

    public int PickaxePowerLevel { get; private set; }
    public int MiningSpeedLevel { get; private set; }
    public int AutoMinerLevel { get; private set; }

    public float ManualDamage => MathF.Pow(2.0f, PickaxePowerLevel);
    public float AutoDamage => MathF.Max(1.0f, ManualDamage * 0.80f);

    public float ManualCooldownSeconds
        => MathF.Max(0.045f, GameConfig.BaseManualCooldownSeconds * MathF.Pow(0.82f, MiningSpeedLevel));

    public int AutoBatchSize
        => AutoMinerLevel <= 0 ? 0 : 1 << Math.Min(AutoMinerLevel - 1, 6);

    public int GetLevel(UpgradeKind kind)
        => kind switch
        {
            UpgradeKind.PickaxePower => PickaxePowerLevel,
            UpgradeKind.MiningSpeed => MiningSpeedLevel,
            UpgradeKind.AutoMiners => AutoMinerLevel,
            _ => 0,
        };

    public int GetCost(UpgradeKind kind)
    {
        int level = GetLevel(kind);
        return kind switch
        {
            UpgradeKind.PickaxePower => ScaleCost(6, 1.95f, level),
            UpgradeKind.MiningSpeed => ScaleCost(10, 2.05f, level),
            UpgradeKind.AutoMiners => ScaleCost(18, 2.0f, level),
            _ => int.MaxValue,
        };
    }

    public bool TryPurchase(UpgradeKind kind, GameState state)
    {
        int cost = GetCost(kind);
        if (!state.TrySpend(cost))
        {
            return false;
        }

        switch (kind)
        {
            case UpgradeKind.PickaxePower:
                PickaxePowerLevel++;
                break;
            case UpgradeKind.MiningSpeed:
                MiningSpeedLevel++;
                break;
            case UpgradeKind.AutoMiners:
                AutoMinerLevel++;
                break;
            default:
                return false;
        }

        Changed?.Invoke();
        return true;
    }

    private static int ScaleCost(int baseCost, float growth, int level)
        => (int)MathF.Ceiling(baseCost * MathF.Pow(growth, level));
}
