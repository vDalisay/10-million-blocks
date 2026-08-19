using Godot;
using TenMillionBlocks.Presentation;
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
    private SelectionHighlight _highlight = null!;

    private bool _leftPressed;
    private Vector2 _pressPosition;
    private Vector3I? _hoveredVoxel;

    public void Initialize(VirtualWorld world, OrbitCameraController camera, WorldView view, MiningService mining)
    {
        _world = world;
        _camera = camera;
        _view = view;
        _mining = mining;

        _highlight = new SelectionHighlight { Name = "SelectionHighlight" };
        _highlight.Initialize(world.Profile.BlockSpacing);
        AddChild(_highlight);

        mining.BlockMined += OnBlockMined;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        UpdateHover();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
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

        MiningResult result = _mining.TryMine(voxel);
        if (result.Success)
        {
            _view.MarkDirtyAround(voxel);
            UpdateHover();
            GetViewport().SetInputAsHandled();
        }
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
