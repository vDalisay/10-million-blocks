using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Mining;

public partial class ManualMiningController : Node3D
{
    private VirtualWorld _world = null!;
    private OrbitCameraController _camera = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private SelectionHighlight _highlight = null!;

    private bool _leftPressed;
    private Vector2 _pressPosition;
    private Vector3I? _hoveredVoxel;

    public Vector3I? HoveredVoxel => _hoveredVoxel;
    public bool InputEnabled { get; set; } = true;

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

        mining.BlockMined += OnBlockMined;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (InputEnabled)
        {
            UpdateHover();
        }
        else
        {
            _hoveredVoxel = null;
            _highlight.HideVoxel();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled || @event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (button.Pressed)
        {
            _leftPressed = true;
            _pressPosition = button.Position;
            return;
        }

        if (!_leftPressed) return;
        _leftPressed = false;

        if (button.Position.DistanceTo(_pressPosition) > 5.0f || _hoveredVoxel is not Vector3I voxel)
        {
            return;
        }

        int actions = MineBurst(voxel, _skills.Derived.ManualBlocksPerClick);
        if (actions > 0)
        {
            UpdateHover();
            _highlight.PulseMine();
            GetViewport().SetInputAsHandled();
        }
    }

    private int MineBurst(Vector3I initial, int requestedBlocks)
    {
        requestedBlocks = Math.Max(1, requestedBlocks);
        var queue = new Queue<Vector3I>();
        var visited = new HashSet<Vector3I>();
        queue.Enqueue(initial);
        visited.Add(initial);
        int actions = 0;
        int presentationBursts = 0;

        while (queue.Count > 0 && actions < requestedBlocks)
        {
            Vector3I candidate = queue.Dequeue();
            MiningResult result = _mining.TryMine(candidate);
            if (!result.Success) continue;

            // Hitting an unstable block counts as this click's mining action even before it is
            // removed. Do not enqueue neighbours because the block is still physically present.
            if (!result.Removed)
            {
                actions++;
                continue;
            }

            actions++;
            MarkEffectDirty(result);
            if (presentationBursts < 3)
            {
                EmitDebris(result, presentationBursts++);
            }

            // A blast is already a complete high-impact action; do not let a multi-block manual
            // upgrade immediately chain from its newly exposed rim in the same click.
            if (result.EffectRadius > 0)
            {
                break;
            }

            foreach (Vector3I direction in VoxelMath.Neighbors)
            {
                Vector3I neighbor = candidate + direction;
                if (visited.Add(neighbor) && _world.IsPresent(neighbor) && _world.IsExposed(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
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
        Vector3I outwardI = _world.Source.GetOutwardNormal(result.Voxel);
        Vector3 outward = (Vector3)outwardI;
        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = _view.VoxelToWorld(result.Voxel) + outward * spacing * 0.48f;
        int seed = unchecked(result.Voxel.X * 73856093
            ^ result.Voxel.Y * 19349663
            ^ result.Voxel.Z * 83492791
            ^ burstIndex * 265443576);

        var burst = new DrillDebrisBurst { Name = result.EffectRadius > 0 ? "BlastDebris" : "ManualMiningDebris" };
        AddChild(burst);
        burst.Initialize(position, outward, result.BlockId, spacing, seed);
    }

    private void UpdateHover()
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        float rayDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(_world, _camera.Camera, mouse, rayDistance, out Vector3I voxel))
        {
            _hoveredVoxel = voxel;
            _highlight.ShowVoxel(voxel);
        }
        else
        {
            _hoveredVoxel = null;
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
}
