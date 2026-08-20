using System;
using System.Linq;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Automation;

public partial class MinerPlacementController : Node
{
    private ManualMiningController _manual = null!;
    private MinerSimulationService _miners = null!;
    private SkillTreeService _skills = null!;
    private OrbitCameraController _camera = null!;

    private Node3D? _ghost;
    private string? _pendingPurchaseSkillId;
    private MinerInstance? _movingMiner;
    private bool _attentionClickHeld;

    public bool InputEnabled { get; set; } = true;
    public string? PendingMinerId { get; private set; }
    public bool IsPlacing => PendingMinerId is not null;
    public bool IsMoving => _movingMiner is not null;
    public bool IsDeferredPurchase => _pendingPurchaseSkillId is not null;

    public event Action? Changed;
    public event Action<string>? Feedback;

    public void Initialize(ManualMiningController manual, MinerSimulationService miners)
    {
        _manual = manual;
        _miners = miners;
        _skills = manual.SkillTree;
        _camera = manual.CameraController;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!InputEnabled)
        {
            _manual.PlacementMode = false;
            _miners.HidePlacementGhost(_ghost);
            return;
        }

        if (PendingMinerId is string minerId)
        {
            _manual.PlacementMode = true;
            EnsureGhost(minerId);
            if (_manual.HoveredVoxel is Vector3I voxel)
            {
                bool valid = _miners.CanPlaceMiner(
                    minerId,
                    voxel,
                    requireUnlocked: _pendingPurchaseSkillId is null,
                    ignoreInstanceId: _movingMiner?.InstanceId);
                _miners.UpdatePlacementGhost(_ghost!, minerId, voxel, valid);
            }
            else
            {
                _miners.HidePlacementGhost(_ghost);
            }
            return;
        }

        bool attentionHovered = _miners.HighlightedAttentionMiner is not null
            && _miners.UpdateAttentionHover(GetViewport().GetMousePosition(), _camera.Camera);
        // While the cursor is over the x-ray automation silhouette, reserve LMB for selecting/moving it
        // rather than allowing the surface block underneath to be mined by ManualMiningController.
        _manual.PlacementMode = attentionHovered;
    }

    public bool BeginPlacement(string minerId)
    {
        if (!InputEnabled || !_miners.IsMinerUnlocked(minerId))
        {
            return false;
        }

        StartPlacement(minerId, purchaseSkillId: null, movingMiner: null);
        return true;
    }

    /// <summary>
    /// Starts a buy-and-place preview without spending anything. The skill purchase is committed only
    /// after the player clicks a green placement ghost.
    /// </summary>
    public bool BeginPurchasePlacement(string minerId, string purchaseSkillId)
    {
        if (!InputEnabled
            || _miners.IsMinerUnlocked(minerId)
            || !_skills.Catalog.Nodes.ContainsKey(purchaseSkillId))
        {
            return false;
        }

        StartPlacement(minerId, purchaseSkillId, movingMiner: null);
        return true;
    }

    public bool BeginMove(MinerInstance miner)
    {
        if (!InputEnabled || !miner.Exhausted || !_miners.Miners.Contains(miner))
        {
            return false;
        }

        _miners.SetMinerHiddenForMove(miner, hidden: true);
        _miners.SetAttentionHighlight(null);
        StartPlacement(miner.DefinitionId, purchaseSkillId: null, movingMiner: miner);
        Feedback?.Invoke($"Moving {miner.DefinitionId}. Place the green ghost; RMB/Esc cancels.");
        return true;
    }

    public void CancelPlacement()
    {
        bool changed = PendingMinerId is not null;
        MinerInstance? moving = _movingMiner;

        ResetPlacementState();
        if (moving is not null)
        {
            _miners.SetMinerHiddenForMove(moving, hidden: false);
            if (moving.Exhausted)
            {
                _miners.SetAttentionHighlight(moving);
            }
        }

        if (changed) Changed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
        {
            return;
        }

        if (PendingMinerId is not null)
        {
            HandlePlacementInput(@event);
            return;
        }

        if (_miners.HighlightedAttentionMiner is not MinerInstance highlighted)
        {
            _attentionClickHeld = false;
            return;
        }

        if (@event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        bool hovered = _miners.UpdateAttentionHover(button.Position, _camera.Camera);
        if (button.Pressed)
        {
            if (!hovered) return;
            _attentionClickHeld = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_attentionClickHeld) return;
        _attentionClickHeld = false;
        if (hovered)
        {
            _ = BeginMove(highlighted);
        }
        GetViewport().SetInputAsHandled();
    }

    private void HandlePlacementInput(InputEvent @event)
    {
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

        string minerId = PendingMinerId!;
        if (_manual.HoveredVoxel is not Vector3I voxel)
        {
            Feedback?.Invoke($"Select a visible surface for {minerId}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        bool valid = _miners.CanPlaceMiner(
            minerId,
            voxel,
            requireUnlocked: _pendingPurchaseSkillId is null,
            ignoreInstanceId: _movingMiner?.InstanceId);
        if (!valid)
        {
            Feedback?.Invoke($"{minerId} cannot be placed on this block.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_movingMiner is MinerInstance moving)
        {
            if (!_miners.TryMoveStoppedMiner(moving, voxel))
            {
                Feedback?.Invoke($"{minerId} could not be moved there.");
                GetViewport().SetInputAsHandled();
                return;
            }

            CompletePlacement($"Moved {minerId}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_pendingPurchaseSkillId is string purchaseSkillId)
        {
            // Temporarily expose the unlock to PlaceMiner, create the accepted unit, then charge only
            // after that callback succeeds. A cancel/red placement never reaches this transaction.
            SkillPurchaseResult purchase = _skills.PurchaseAfterCommit(
                purchaseSkillId,
                () => _miners.PlaceMiner(minerId, voxel) is not null);
            if (!purchase.Success)
            {
                Feedback?.Invoke(PurchaseFailureText(purchase));
                GetViewport().SetInputAsHandled();
                return;
            }

            CompletePlacement($"Bought and placed {minerId}.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_miners.PlaceMiner(minerId, voxel) is null)
        {
            Feedback?.Invoke($"{minerId} placement changed before it could be committed. Try again.");
            GetViewport().SetInputAsHandled();
            return;
        }

        CompletePlacement($"Placed {minerId}.");
        GetViewport().SetInputAsHandled();
    }

    private void StartPlacement(string minerId, string? purchaseSkillId, MinerInstance? movingMiner)
    {
        ResetPlacementState();
        PendingMinerId = minerId;
        _pendingPurchaseSkillId = purchaseSkillId;
        _movingMiner = movingMiner;
        _manual.PlacementMode = true;
        EnsureGhost(minerId);
        Changed?.Invoke();
    }

    private void CompletePlacement(string message)
    {
        ResetPlacementState();
        Changed?.Invoke();
        Feedback?.Invoke(message);
    }

    private void ResetPlacementState()
    {
        PendingMinerId = null;
        _pendingPurchaseSkillId = null;
        _movingMiner = null;
        _attentionClickHeld = false;
        _manual.PlacementMode = false;
        if (_ghost is not null)
        {
            _miners.DestroyPlacementGhost(_ghost);
            _ghost = null;
        }
    }

    private void EnsureGhost(string minerId)
    {
        if (_ghost is not null && GodotObject.IsInstanceValid(_ghost)) return;
        _ghost = _miners.CreatePlacementGhost(minerId);
    }

    private static string PurchaseFailureText(SkillPurchaseResult result)
        => result.Failure switch
        {
            SkillPurchaseFailure.InsufficientResources => "Not enough resources to complete this placement.",
            SkillPurchaseFailure.MissingPrerequisite => "Automation prerequisites are no longer met.",
            SkillPurchaseFailure.MaxRank => "Automation is already owned; select it again to place it.",
            SkillPurchaseFailure.CommitRejected => "The placement could not be committed; no resources were spent.",
            _ => "Automation purchase could not be completed.",
        };
}
