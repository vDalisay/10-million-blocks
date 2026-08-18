using Godot;
using TenMillionBlocks.Core;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Gameplay;

public sealed partial class ManualMiningController : Node
{
    private VoxelWorld? _world;
    private OrbitCameraController? _cameraController;
    private MiningService? _mining;
    private UpgradeSystem? _upgrades;

    private MeshInstance3D? _selection;
    private bool _mineHeld;
    private float _cooldownRemaining;
    private bool _enabled = true;

    public void Initialize(
        VoxelWorld world,
        OrbitCameraController cameraController,
        MiningService mining,
        UpgradeSystem upgrades)
    {
        _world = world;
        _cameraController = cameraController;
        _mining = mining;
        _upgrades = upgrades;

        _selection = new MeshInstance3D
        {
            Name = "Selection",
            Mesh = BuildSelectionMesh(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        world.AddChild(_selection);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_enabled)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            _mineHeld = mouseButton.Pressed;
            if (mouseButton.Pressed)
            {
                TryMineAtCursor();
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_enabled || _world is null || _cameraController is null || _upgrades is null)
        {
            return;
        }

        _cooldownRemaining = Mathf.Max(0.0f, _cooldownRemaining - (float)delta);
        UpdateSelection();

        if (_mineHeld && _cooldownRemaining <= 0.0f)
        {
            TryMineAtCursor();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _mineHeld = false;
        if (_selection is not null)
        {
            _selection.Visible = enabled && _selection.Visible;
        }
    }

    private void TryMineAtCursor()
    {
        if (_world is null || _cameraController is null || _mining is null || _upgrades is null)
        {
            return;
        }

        if (!TryRaycast(out VoxelRayHit hit))
        {
            return;
        }

        if (_mining.Mine(hit.Coordinate, automated: false))
        {
            _cooldownRemaining = _upgrades.ManualCooldownSeconds;
        }
    }

    private void UpdateSelection()
    {
        if (_selection is null)
        {
            return;
        }

        if (TryRaycast(out VoxelRayHit hit))
        {
            _selection.Visible = true;
            _selection.Position = (Vector3)hit.Coordinate;
        }
        else
        {
            _selection.Visible = false;
        }
    }

    private bool TryRaycast(out VoxelRayHit hit)
    {
        hit = default;
        if (_world is null || _cameraController is null)
        {
            return false;
        }

        Camera3D camera = _cameraController.Camera;
        Vector2 mousePosition = GetViewport().GetMousePosition();
        Vector3 origin = camera.ProjectRayOrigin(mousePosition);
        Vector3 direction = camera.ProjectRayNormal(mousePosition);

        return VoxelRaycaster.TryRaycast(
            _world,
            origin,
            direction,
            GameConfig.ManualMineDistance,
            out hit);
    }

    private static ImmediateMesh BuildSelectionMesh()
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        float h = 0.515f;
        Vector3[] corners =
        [
            new(-h, -h, -h), new(h, -h, -h),
            new(h, h, -h), new(-h, h, -h),
            new(-h, -h, h), new(h, -h, h),
            new(h, h, h), new(-h, h, h),
        ];

        int[] edges =
        [
            0,1, 1,2, 2,3, 3,0,
            4,5, 5,6, 6,7, 7,4,
            0,4, 1,5, 2,6, 3,7,
        ];

        foreach (int index in edges)
        {
            mesh.SurfaceAddVertex(corners[index]);
        }

        mesh.SurfaceEnd();
        return mesh;
    }
}
