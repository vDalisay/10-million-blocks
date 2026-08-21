using System;
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
    private Vector3I _hoverSurfaceNormal = Vector3I.Up;
    private Vector3I? _lastHoverMiningVoxel;
    private Vector3I? _lastHoverMiningNormal;
    private double _hoverMiningAccumulator;
    private bool _hoverMiningEnabled;

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
            _hoveredVoxel = null;
            _highlight.HideVoxel();
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
        if (_hoveredVoxel is not Vector3I voxel) return;

        int actions = MineManualTick(voxel, _hoverSurfaceNormal);
        if (actions > 0)
        {
            UpdateHover(button.Position);
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
            || _hoveredVoxel is not Vector3I voxel)
        {
            ResetHoverMiningCadence();
            _hoverIndicator?.SetState(false, mouse, footprint, 0.0f);
            return;
        }

        if (_lastHoverMiningVoxel != voxel || _lastHoverMiningNormal != _hoverSurfaceNormal)
        {
            _lastHoverMiningVoxel = voxel;
            _lastHoverMiningNormal = _hoverSurfaceNormal;
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
        if (MineManualTick(voxel, _hoverSurfaceNormal) > 0)
        {
            _highlight.PulseMine();
            UpdateHover(mouse);
        }
        _hoverIndicator?.SetState(
            true,
            mouse,
            footprint,
            (float)Math.Clamp(_hoverMiningAccumulator / interval, 0.0, 1.0));
    }

    private int MineManualTick(Vector3I initial, Vector3I viewNormal)
    {
        var targets = ManualMiningFootprint.ResolveHighestLayer(
            _world,
            initial,
            _skills.Derived.ManualFootprint,
            viewNormal);
        if (targets.Count == 0) return 0;

        int actions = 0;
        int presentationBursts = 0;
        foreach (Vector3I candidate in targets)
        {
            MiningResult result = _mining.TryMine(candidate);
            if (!result.Success) continue;

            actions++;
            if (!result.Removed) continue;

            MarkEffectDirty(result);
            if (presentationBursts < 5)
            {
                _view.SpawnManualMinePop(result.Voxel, result.BlockId);
                EmitDebris(result, presentationBursts++);
            }

            // An unstable-block blast is already a complete high-impact action. Other targets in the
            // same footprint are left for the next manual tick rather than chaining through the crater.
            if (result.EffectRadius > 0) break;
        }

        return actions;
    }

    private void MarkEffectDirty(MiningResult result)
    {
        int radius = Math.Max(0, result.EffectRadius);
        if (radius == 0)
        {
            _view.MarkDirtyAround(result.Voxel);
            return;
        }

        int radiusSquared = radius * radius;
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y + z * z > radiusSquared) continue;
            _view.MarkDirtyAround(result.Voxel + new Vector3I(x, y, z));
        }
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

    private void UpdateHover(Vector2 mouse)
    {
        float rayDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(
            _world,
            _camera.Camera,
            mouse,
            rayDistance,
            out Vector3I voxel,
            out Vector3I surfaceNormal))
        {
            _hoveredVoxel = voxel;
            _hoverSurfaceNormal = surfaceNormal;
            var targets = ManualMiningFootprint.ResolveHighestLayer(
                _world,
                voxel,
                _skills.Derived.ManualFootprint,
                surfaceNormal);
            _highlight.ShowVoxels(targets);
        }
        else
        {
            _hoveredVoxel = null;
            _hoverSurfaceNormal = Vector3I.Up;
            _highlight.HideVoxel();
        }
    }

    private void OnBlockMined(MiningResult result)
    {
        if (_hoveredVoxel == result.Voxel)
        {
            _hoveredVoxel = null;
            _highlight.HideVoxel();
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
            UpdateHover(GetViewport().GetMousePosition());
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
        _lastHoverMiningNormal = null;
    }
}
