using System;
using System.Collections.Generic;

namespace TenMillionBlocks.Economy;

/// <summary>
/// Player-bound inventory for rare resources such as gem_red. Ordinary mining currency remains on
/// MiningService; this inventory is intentionally keyed by content ID so future transformation costs
/// do not require a new currency subsystem per gem/tool.
/// </summary>
public sealed class SpecialResourceInventory
{
    private readonly Dictionary<string, long> _balances = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyDictionary<string, long> Balances => _balances;

    public long Get(string resourceId)
        => string.IsNullOrWhiteSpace(resourceId) ? 0L : _balances.GetValueOrDefault(resourceId);

    public bool CanAfford(string resourceId, long amount)
        => amount >= 0 && Get(resourceId) >= amount;

    public void Grant(string resourceId, long amount = 1L)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || amount <= 0) return;
        _balances[resourceId] = checked(Get(resourceId) + amount);
        Changed?.Invoke();
    }

    public bool TrySpend(string resourceId, long amount)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return false;
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return true;

        long current = Get(resourceId);
        if (current < amount) return false;

        long remaining = current - amount;
        if (remaining == 0) _balances.Remove(resourceId);
        else _balances[resourceId] = remaining;
        Changed?.Invoke();
        return true;
    }

    public void Restore(IReadOnlyDictionary<string, long>? balances)
    {
        _balances.Clear();
        if (balances is not null)
        {
            foreach ((string id, long amount) in balances)
            {
                if (!string.IsNullOrWhiteSpace(id) && amount > 0)
                {
                    _balances[id] = amount;
                }
            }
        }
        Changed?.Invoke();
    }

    public Dictionary<string, long> CreateSnapshot()
        => new(_balances, StringComparer.Ordinal);
}
