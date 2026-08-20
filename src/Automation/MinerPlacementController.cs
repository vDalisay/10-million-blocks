using System;
using Godot;
using TenMillionBlocks.Mining;

namespace TenMillionBlocks.Automation;

public partial class MinerPlacementController : Node
{
    private ManualMiningController _manual = null!;
    private MinerSimulationService _miners = null!;

    public bool InputEnabled { get; set; } = true;
    public string? PendingMinerId { get; private set; }
    public bool IsPlacing => PendingMinerId is not null;

    public event Action? Changed;
    public event Action<string>? Feedback;

    public void Initialize(ManualMiningController manual, MinerSimulationService miners)
    {
        _manual = manual;
        _miners = miners;
    }

    public bool BeginPlacement(string minerId)
    {
        if (!InputEnabled || !_miners.IsMinerUnlocked(minerId))
        {
            return false;
        }

        PendingMinerId = minerId;
        _manual.PlacementMode = true;
        Changed?.Invoke();
        return true;
    }

    public void CancelPlacement()
    {
        bool changed = PendingMinerId is not null;
        PendingMinerId = null;
        _manual.PlacementMode = false;
        if (changed) Changed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled || PendingMinerId is null)
        {
            return;
        }

        if (@event is InputEventKey key
            && key.Pressed
            && !key.Echo
            && key.Keycode == Key.Escape)
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton button)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.Right && button.Pressed)
        {
            CancelPlacement();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex != MouseButton.Left || button.Pressed)
        {
            return;
        }

        string minerId = PendingMinerId;
        if (_manual.HoveredVoxel is not Vector3I voxel)
        {
            Feedback?.Invoke($"Select a visible cube surface for {minerId}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_miners.PlaceMiner(minerId, voxel) is null)
        {
            Feedback?.Invoke($"{minerId} cannot be placed on this block.");
            GetViewport().SetInputAsHandled();
            return;
        }

        PendingMinerId = null;
        _manual.PlacementMode = false;
        Changed?.Invoke();
        Feedback?.Invoke($"Placed {minerId}.");
        GetViewport().SetInputAsHandled();
    }
}
