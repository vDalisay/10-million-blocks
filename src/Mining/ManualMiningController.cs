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

        if (!_leftPressed)
        {
            return;
        }

        _leftPressed = false;
        if (button.Position.DistanceTo(_pressPosition) > 5.0f || _hoveredVoxel is not Vector3I voxel)
        {
            return;
        }

        int mined = MineBurst(voxel, _skills.Derived.ManualBlocksPerClick);
        if (mined > 0)
        {
            UpdateHover();
            GetViewport().SetInputAsHandled();
        }
    }

    private int MineBurst(Vector3I initial, int requestedBlocks)
    {
        requestedBlocks = System.Math.Max(1, requestedBlocks);
        var queue = new Queue<Vector3I>();
        var visited = new HashSet<Vector3I>();
        queue.Enqueue(initial);
        visited.Add(initial);
        int minedCount = 0;

        while (queue.Count > 0 && minedCount < requestedBlocks)
        {
            Vector3I candidate = queue.Dequeue();
            MiningResult result = _mining.TryMine(candidate);
            if (!result.Success)
            {
                continue;
            }

            minedCount++;
            _view.MarkDirtyAround(candidate);

            foreach (Vector3I direction in VoxelMath.Neighbors)
            {
                Vector3I neighbor = candidate + direction;
                if (visited.Add(neighbor) && _world.IsPresent(neighbor) && _world.IsExposed(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return minedCount;
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
