using System;

namespace TenMillionBlocks.Core;

public sealed class GameState
{
    public event Action? Changed;

    public int StageIndex { get; private set; }
    public int Currency { get; private set; }
    public int TotalBlocksMined { get; private set; }
    public int CurrentSeed { get; private set; } = 10_000_031;

    public int CurrentStageTarget => GameConfig.StageBlockCounts[StageIndex];

    public void AddCurrency(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Currency += amount;
        Changed?.Invoke();
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || Currency < amount)
        {
            return false;
        }

        Currency -= amount;
        Changed?.Invoke();
        return true;
    }

    public void RecordBlockMined(int reward)
    {
        TotalBlocksMined++;
        Currency += Math.Max(0, reward);
        Changed?.Invoke();
    }

    public bool AdvanceStage()
    {
        if (StageIndex >= GameConfig.StageBlockCounts.Length - 1)
        {
            return false;
        }

        StageIndex++;
        CurrentSeed += 977;
        Changed?.Invoke();
        return true;
    }

    public void AdvanceEndlessSeed()
    {
        CurrentSeed += 7_919;
        Changed?.Invoke();
    }
}
