using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController : Node3D
{
    private const double BaseHoverMiningIntervalSeconds = 0.5;

    private VirtualWorld _world = null!;
    private OrbitCameraController _camera = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private SelectionHighlight _highlight = null!;
    private HoverMiningCursorIndicator? _hoverIndicator;
    private Button? _hoverToggle;

    private Vector3I? _hoveredVoxel;
    private IReadOnlyList<Vector3I> _hoverTargets = Array.Empty<Vector3I>();
    private Vector3I? _lastHoverMiningVoxel;
    private double _hoverMiningAccumulator;
    private bool _hoverMiningEnabled;

    // Voxel DDA is cheap compared with physics picking, but there is no reason to repeat it every
    // rendered frame while both the pointer and camera are stationary. Large worlds make this more
    // valuable because a ray can cross many logical cells. Mining/skill changes explicitly invalidate
    // the cache so the next deeper surface or upgraded footprint is still resolved immediately.
    private bool _hoverRayCacheValid;
    private Vector2 _lastHoverMouse;
    private Vector2 _lastHoverViewportSize;
    private Vector3 _lastHoverCameraPosition;
    private Vector3 _lastHoverCameraForward;
    private Vector3 _lastHoverCameraUp;
    private float _lastHoverCameraFov;

    public Vector3I? HoveredVoxel => _hoveredVoxel;
    public bool InputEnabled { get; set; } = true;
    public bool PlacementMode { get; set; }
    public bool HoverMiningEnabled => _hoverMiningEnabled && _skills.Derived.HoverMiningUnlocked;

    public void Initialize(
        VirtualWorld world,
        OrbitCameraController camera,
        WorldView view,
        MiningService mining,
        SkillTreeService skills)
    {
        _world = world;
        _camera = camera;
        _view = view;
        _mining = mining;
        _skills = skills;

        _highlight = new SelectionHighlight { Name = "SelectionHighlight" };
        _highlight.Initialize(world.Profile.BlockSpacing);
        AddChild(_highlight);

        BuildHoverMiningUi();
        mining.BlockMined += OnBlockMined;
        skills.Changed += OnSkillsChanged;
        RefreshHoverMiningUi();
    }

    public override void _ExitTree()
    {
        if (_mining is not null) _mining.BlockMined -= OnBlockMined;
        if (_skills is not null) _skills.Changed -= OnSkillsChanged;
    }

    public override void _Process(double delta)
    {
        if (InputEnabled)
        {
            UpdateHover(GetViewport().GetMousePosition());
            ProcessHoverMining(delta);
        }
        else
        {
            ResetHoverMiningCadence();
            ClearHover();
            _hoverIndicator?.SetState(false, GetViewport().GetMousePosition(), _skills.Derived.ManualFootprint, 0.0f);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (PlacementMode
            || !InputEnabled
            || @event is not InputEventMouseButton button
            || button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (!button.Pressed)
        {
            return;
        }

        UpdateHover(button.Position);
        if (_hoveredVoxel is null || _hoverTargets.Count == 0) return;

        int actions = MineManualTick(_hoverTargets, hoverMining: false);
        if (actions > 0)
        {
            UpdateHover(button.Position, force: true);
            _highlight.PulseMine();
            GetViewport().SetInputAsHandled();
        }
    }

    public void RestoreHoverMiningEnabled(bool enabled)
    {
        _hoverMiningEnabled = enabled && _skills.Derived.HoverMiningUnlocked;
        ResetHoverMiningCadence();
        RefreshHoverMiningUi();
    }

    public void SetHoverMiningEnabled(bool enabled)
    {
        bool next = enabled && _skills.Derived.HoverMiningUnlocked;
        if (_hoverMiningEnabled == next) return;
        _hoverMiningEnabled = next;
        ResetHoverMiningCadence();
        RefreshHoverMiningUi();
    }

    private void ProcessHoverMining(double delta)
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        ManualMiningFootprintKind footprint = _skills.Derived.ManualFootprint;
        if (!HoverMiningEnabled
            || PlacementMode
            || _camera.IsManipulating
            || _hoveredVoxel is not Vector3I voxel
            || _hoverTargets.Count == 0)
        {
            ResetHoverMiningCadence();
            _hoverIndicator?.SetState(false, mouse, footprint, 0.0f);
            return;
        }

        if (_lastHoverMiningVoxel != voxel)
        {
            _lastHoverMiningVoxel = voxel;
            _hoverMiningAccumulator = 0.0;
        }

        double rate = Math.Max(0.05, _skills.Derived.ManualMiningRateMultiplier);
        double interval = BaseHoverMiningIntervalSeconds / rate;
        _hoverMiningAccumulator += Math.Max(0.0, delta);
        _hoverIndicator?.SetState(
            true,
            mouse,
            footprint,
            (float)Math.Clamp(_hoverMiningAccumulator / interval, 0.0, 1.0));
        if (_hoverMiningAccumulator < interval) return;

        // Cap catch-up to one action per rendered frame. Hover mining is intentionally a controlled
        // cadence, not a backlog that explodes after a hitch or menu pause.
        _hoverMiningAccumulator %= interval;
        _hoverIndicator?.Pulse();
        if (MineManualTick(_hoverTargets, hoverMining: true) > 0)
        {
            _highlight.PulseMine();
            UpdateHover(mouse, force: true);
        }
        _hoverIndicator?.SetState(
            true,
            mouse,
            footprint,
            (float)Math.Clamp(_hoverMiningAccumulator / interval, 0.0, 1.0));
    }

    private int MineManualTick(IReadOnlyList<Vector3I> targets, bool hoverMining)
    {
        if (targets.Count == 0) return 0;

        int actions = 0;
        int presentationBursts = 0;
        _mining.BeginCurrencyNotificationBatch();
        try
        {
            foreach (Vector3I candidate in targets)
            {
                MiningResult result = _mining.TryMine(candidate);
                if (!result.Success) continue;

                actions++;
                if (!result.Removed) continue;

                MarkEffectDirty(result);
                if (presentationBursts < 5)
                {
                    _view.SpawnManualMinePop(
                        result.Voxel,
                        result.BlockId,
                        hoverMining ? 1.24f : 1.12f);
                    EmitDebris(result, presentationBursts++);
                }

                // An unstable-block blast is already a complete high-impact action. Other targets in the
                // same footprint are left for the next manual tick rather than chaining through the crater.
                if (result.EffectRadius > 0) break;
            }
        }
        finally
        {
            _mining.EndCurrencyNotificationBatch();
        }

        return actions;
    }

    private void MarkEffectDirty(MiningResult result)
    {
        int radius = Math.Max(0, result.EffectRadius);
        if (radius == 0)
        {
            _view.MarkDirtyVoxel(result.Voxel);
            return;
        }

        // A blast can remove dozens of neighbouring voxels. Coalesce the entire sphere into unique
        // cross-chunk rebuilds once instead of submitting MarkDirtyAround separately for every cell.
        _view.MarkDirtySphere(result.Voxel, radius);
    }

    private void EmitDebris(MiningResult result, int burstIndex)
    {
        int seed = unchecked(result.Voxel.X * 73856093
            ^ result.Voxel.Y * 19349663
            ^ result.Voxel.Z * 83492791
            ^ burstIndex * 265443576);
        _view.SpawnMiningDebris(
            result.Voxel,
            result.BlockId,
            seed,
            result.EffectRadius > 0 ? "BlastDebris" : "ManualMiningDebris");
    }

    private void UpdateHover(Vector2 mouse, bool force = false)
    {
        Camera3D camera = _camera.Camera;
        Vector3 cameraPosition = camera.GlobalPosition;
        Vector3 cameraForward = -camera.GlobalBasis.Z.Normalized();
        Vector3 cameraUp = camera.GlobalBasis.Y.Normalized();
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float cameraFov = camera.Fov;

        if (!force
            && _hoverRayCacheValid
            && mouse.DistanceSquaredTo(_lastHoverMouse) < 0.01f
            && viewportSize.DistanceSquaredTo(_lastHoverViewportSize) < 0.01f
            && MathF.Abs(cameraFov - _lastHoverCameraFov) < 0.0001f
            && cameraPosition.DistanceSquaredTo(_lastHoverCameraPosition) < 0.000001f
            && cameraForward.Dot(_lastHoverCameraForward) > 0.999999f
            && cameraUp.Dot(_lastHoverCameraUp) > 0.999999f)
        {
            return;
        }

        _hoverRayCacheValid = true;
        _lastHoverMouse = mouse;
        _lastHoverViewportSize = viewportSize;
        _lastHoverCameraFov = cameraFov;
        _lastHoverCameraPosition = cameraPosition;
        _lastHoverCameraForward = cameraForward;
        _lastHoverCameraUp = cameraUp;

        float rayDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(
            _world,
            camera,
            mouse,
            rayDistance,
            out Vector3I voxel,
            out Vector3I surfaceNormal))
        {
            _hoveredVoxel = voxel;
            _hoverTargets = ManualMiningFootprint.ResolveFromCenter(
                _world,
                voxel,
                _skills.Derived.ManualFootprint,
                surfaceNormal);
            _highlight.ShowVoxels(_hoverTargets);
        }
        else
        {
            // Keep the miss cached as long as the pointer/camera do not move. Otherwise an empty patch
            // of space would still force a full DDA attempt every frame.
            ClearHover(invalidateRayCache: false);
        }
    }

    private void ClearHover(bool invalidateRayCache = true)
    {
        _hoveredVoxel = null;
        _hoverTargets = Array.Empty<Vector3I>();
        _highlight.HideVoxel();
        if (invalidateRayCache) _hoverRayCacheValid = false;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (_hoveredVoxel == result.Voxel)
        {
            ClearHover();
        }
    }

    private void OnSkillsChanged()
    {
        if (!_skills.Derived.HoverMiningUnlocked)
        {
            _hoverMiningEnabled = false;
            ResetHoverMiningCadence();
        }
        RefreshHoverMiningUi();
        if (InputEnabled)
        {
            UpdateHover(GetViewport().GetMousePosition(), force: true);
        }
    }

    private void BuildHoverMiningUi()
    {
        var layer = new CanvasLayer
        {
            Name = "HoverMiningUi",
            Layer = 24,
        };
        AddChild(layer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        _hoverIndicator = new HoverMiningCursorIndicator
        {
            Name = "HoverMiningCursorIndicator",
            ZIndex = 100,
        };
        root.AddChild(_hoverIndicator);

        _hoverToggle = new Button
        {
            AnchorLeft = 1.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -238.0f,
            OffsetTop = -124.0f,
            OffsetRight = -18.0f,
            OffsetBottom = -78.0f,
            CustomMinimumSize = new Vector2(220.0f, 46.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _hoverToggle.Pressed += () => SetHoverMiningEnabled(!HoverMiningEnabled);
        root.AddChild(_hoverToggle);
    }

    private void RefreshHoverMiningUi()
    {
        if (_hoverToggle is null) return;
        bool unlocked = _skills.Derived.HoverMiningUnlocked;
        _hoverToggle.Visible = unlocked;
        _hoverToggle.Text = $"HOVER MINING: {(HoverMiningEnabled ? "ON" : "OFF")}";
        _hoverToggle.TooltipText = unlocked
            ? "Toggle automatic manual mining while the cursor rests on a block. Camera movement and placement pause it."
            : string.Empty;
        if (!HoverMiningEnabled)
        {
            _hoverIndicator?.SetState(false, GetViewport().GetMousePosition(), _skills.Derived.ManualFootprint, 0.0f);
        }
    }

    private void ResetHoverMiningCadence()
    {
        _hoverMiningAccumulator = 0.0;
        _lastHoverMiningVoxel = null;
    }
}
