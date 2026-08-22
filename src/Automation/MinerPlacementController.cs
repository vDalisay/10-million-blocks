using System;
using System.Collections.Generic;
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
    private bool _pendingUnitPurchase;
    private MinerInstance? _movingMiner;
    private MinerInstance? _ambientHoveredMiner;
    private bool _movingFromAmbientHover;
    private bool _attentionClickHeld;
    private CanvasLayer? _cancelLayer;
    private Button? _cancelButton;
    private readonly List<Vector3I> _placementFootprint = new(9);

    public bool InputEnabled { get; set; } = true;
    public string? PendingMinerId { get; private set; }
    public bool IsPlacing => PendingMinerId is not null;
    public bool IsMoving => _movingMiner is not null;
    public bool IsDeferredPurchase => _pendingPurchaseSkillId is not null || _pendingUnitPurchase;
    public bool IsUnitPurchase => _pendingUnitPurchase;

    public event Action? Changed;
    public event Action<string>? Feedback;

    public void Initialize(ManualMiningController manual, MinerSimulationService miners)
    {
        _manual = manual;
        _miners = miners;
        _skills = manual.SkillTree;
        _camera = manual.CameraController;
    }

    public override void _Ready()
    {
        BuildCancelUi();
        RefreshCancelUi();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!InputEnabled)
        {
            _manual.PlacementMode = false;
            _manual.HidePlacementHighlight();
            _miners.HidePlacementGhost(_ghost);
            ClearAmbientHoverHighlight();
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

                // Placement owns the selection visual while active. Reuse the existing batched highlight,
                // but shape it like the physical automation instead of the player's mining upgrade.
                _miners.FillPlacementFootprint(minerId, voxel, _placementFootprint);
                _manual.ShowPlacementHighlight(_placementFootprint);
            }
            else
            {
                _miners.HidePlacementGhost(_ghost);
                _manual.HidePlacementHighlight();
            }
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();

        if (_ambientHoveredMiner is not null
            && _miners.HighlightedAttentionMiner?.InstanceId != _ambientHoveredMiner.InstanceId)
        {
            _ambientHoveredMiner = null;
        }

        if (_ambientHoveredMiner is not null)
        {
            bool stillHovered = _miners.UpdateAttentionHover(mouse, _camera.Camera);
            if (!stillHovered)
            {
                _miners.SetAttentionHighlight(null);
                _ambientHoveredMiner = null;
                _manual.PlacementMode = false;
            }
            else
            {
                _manual.PlacementMode = true;
                _manual.HidePlacementHighlight();
            }
            return;
        }

        if (_miners.HighlightedAttentionMiner is not null)
        {
            _manual.PlacementMode = _miners.UpdateAttentionHover(mouse, _camera.Camera);
            if (_manual.PlacementMode) _manual.HidePlacementHighlight();
            return;
        }

        MinerInstance? ambient = _miners.FindVisibleStoppedMinerUnderMouse(mouse, _camera.Camera);
        if (ambient is not null)
        {
            _ambientHoveredMiner = ambient;
            _miners.SetAttentionHighlight(ambient);
            _manual.PlacementMode = _miners.UpdateAttentionHover(mouse, _camera.Camera);
            if (_manual.PlacementMode) _manual.HidePlacementHighlight();
        }
        else
        {
            _manual.PlacementMode = false;
        }
    }

    /// <summary>
    /// Free placement is retained for explicit debug/internal callers. Player-facing automation uses
    /// BeginUnitPurchasePlacement so each physical instance has its fixed world-local price.
    /// </summary>
    public bool BeginPlacement(string minerId)
    {
        if (!InputEnabled || !_miners.IsMinerUnlocked(minerId))
        {
            return false;
        }

        StartPlacement(minerId, purchaseSkillId: null, unitPurchase: false, movingMiner: null);
        return true;
    }

    public bool BeginUnitPurchasePlacement(string minerId)
    {
        if (!InputEnabled || !_miners.IsMinerUnlocked(minerId))
        {
            return false;
        }

        StartPlacement(minerId, purchaseSkillId: null, unitPurchase: true, movingMiner: null);
        return true;
    }

    /// <summary>
    /// Legacy transactional unlock-and-place path retained for compatibility while authored content
    /// migrates to capability-only skill effects. New progression content should unlock first and then
    /// buy physical units through BeginUnitPurchasePlacement.
    /// </summary>
    public bool BeginPurchasePlacement(string minerId, string purchaseSkillId)
    {
        if (!InputEnabled
            || _miners.IsMinerUnlocked(minerId)
            || !_skills.Catalog.Nodes.ContainsKey(purchaseSkillId))
        {
            return false;
        }

        StartPlacement(minerId, purchaseSkillId, unitPurchase: false, movingMiner: null);
        return true;
    }

    public bool BeginMove(MinerInstance miner)
    {
        if (!InputEnabled || !miner.Exhausted || !_miners.Miners.Contains(miner))
        {
            return false;
        }

        bool fromAmbientHover = _ambientHoveredMiner?.InstanceId == miner.InstanceId;
        _ambientHoveredMiner = null;
        _miners.SetMinerHiddenForMove(miner, hidden: true);
        _miners.SetAttentionHighlight(null);
        StartPlacement(miner.DefinitionId, purchaseSkillId: null, unitPurchase: false, movingMiner: miner);
        _movingFromAmbientHover = fromAmbientHover;
        RefreshCancelUi();
        Feedback?.Invoke($"Moving {miner.DefinitionId}. RMB still orbits the camera; Esc or the Cancel button returns it to its original position.");
        return true;
    }

    public void CancelPlacement()
    {
        bool changed = PendingMinerId is not null;
        MinerInstance? moving = _movingMiner;
        bool restoreAttentionFocus = moving is not null && !_movingFromAmbientHover;

        ResetPlacementState();
        if (moving is not null)
        {
            _miners.SetMinerHiddenForMove(moving, hidden: false);
            if (moving.Exhausted && restoreAttentionFocus)
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

        if (button.ButtonIndex == MouseButton.Right)
        {
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

        if (_pendingUnitPurchase)
        {
            MinerDefinition definition = _miners.GetDefinition(minerId);
            if (_miners.PurchaseAndPlaceMiner(minerId, voxel) is null)
            {
                Feedback?.Invoke($"Could not buy {definition.DisplayName}. Need {definition.UnitPrice:N0} resources and a valid placement.");
                GetViewport().SetInputAsHandled();
                return;
            }

            CompletePlacement($"Bought and placed {definition.DisplayName} for {definition.UnitPrice:N0} resources.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_pendingPurchaseSkillId is string purchaseSkillId)
        {
            SkillPurchaseResult purchase = _skills.PurchaseAfterCommit(
                purchaseSkillId,
                () => _miners.PlaceMiner(minerId, voxel) is not null);
            if (!purchase.Success)
            {
                Feedback?.Invoke(PurchaseFailureText(purchase));
                GetViewport().SetInputAsHandled();
                return;
            }

            CompletePlacement($"Unlocked and placed {minerId}.");
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

    private void StartPlacement(
        string minerId,
        string? purchaseSkillId,
        bool unitPurchase,
        MinerInstance? movingMiner)
    {
        ResetPlacementState();
        PendingMinerId = minerId;
        _pendingPurchaseSkillId = purchaseSkillId;
        _pendingUnitPurchase = unitPurchase;
        _movingMiner = movingMiner;
        _manual.PlacementMode = true;
        _manual.HidePlacementHighlight();
        EnsureGhost(minerId);
        RefreshCancelUi();
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
        _pendingUnitPurchase = false;
        _movingMiner = null;
        _movingFromAmbientHover = false;
        _attentionClickHeld = false;
        _placementFootprint.Clear();
        _manual.PlacementMode = false;
        if (_ghost is not null)
        {
            _miners.DestroyPlacementGhost(_ghost);
            _ghost = null;
        }
        _manual.RestoreMiningHighlight();
        RefreshCancelUi();
    }

    private void EnsureGhost(string minerId)
    {
        if (_ghost is not null && GodotObject.IsInstanceValid(_ghost)) return;
        _ghost = _miners.CreatePlacementGhost(minerId);
    }

    private void ClearAmbientHoverHighlight()
    {
        if (_ambientHoveredMiner is null) return;
        if (_miners.HighlightedAttentionMiner?.InstanceId == _ambientHoveredMiner.InstanceId)
        {
            _miners.SetAttentionHighlight(null);
        }
        _ambientHoveredMiner = null;
    }

    private void BuildCancelUi()
    {
        _cancelLayer = new CanvasLayer
        {
            Name = "PlacementCancelLayer",
            Layer = 35,
        };
        AddChild(_cancelLayer);

        var root = new Control
        {
            Name = "PlacementCancelRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _cancelLayer.AddChild(root);

        _cancelButton = new Button
        {
            Text = "CANCEL [ESC]",
            AnchorLeft = 1.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -210.0f,
            OffsetTop = -66.0f,
            OffsetRight = -18.0f,
            OffsetBottom = -18.0f,
            CustomMinimumSize = new Vector2(192.0f, 48.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _cancelButton.Pressed += CancelPlacement;
        root.AddChild(_cancelButton);
    }

    private void RefreshCancelUi()
    {
        if (_cancelButton is null) return;
        _cancelButton.Visible = IsPlacing;
        _cancelButton.Text = IsMoving ? "CANCEL MOVE [ESC]" : "CANCEL [ESC]";
    }

    private static string PurchaseFailureText(SkillPurchaseResult result)
        => result.Failure switch
        {
            SkillPurchaseFailure.InsufficientResources => "Not enough resources to complete this placement.",
            SkillPurchaseFailure.InsufficientSpecialResources => "Missing special resources to complete this placement.",
            SkillPurchaseFailure.MissingPrerequisite => "Automation prerequisites are no longer met.",
            SkillPurchaseFailure.MaxRank => "Automation capability is already owned.",
            SkillPurchaseFailure.CommitRejected => "The placement could not be committed; no resources were spent.",
            _ => "Automation purchase could not be completed.",
        };
}
