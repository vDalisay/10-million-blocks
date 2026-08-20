using System;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.WorldEvents;

/// <summary>
/// Active mining accelerators introduced in the 40-cube world. They deliberately resolve their
/// damage through MiningService so currency, special resources, completion, saves and replay all see
/// the same authoritative block-removal stream as the rest of the game.
/// </summary>
public partial class WorldEventController : Node3D
{
    private const int CloudClicksToCharge = 5;
    private const int LightningRadius = 2;
    private const int MeteorRadius = 3;
    private const double MeteorInitialDelaySeconds = 12.0;
    private const double MeteorRespawnSeconds = 28.0;
    private const double MeteorWindowSeconds = 20.0;

    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private OrbitCameraController _camera = null!;
    private bool _cloudEnabled;
    private bool _meteorEnabled;

    private Node3D? _cloud;
    private StandardMaterial3D? _cloudMaterial;
    private int _cloudCharge;
    private float _cloudPhase;

    private Node3D? _meteor;
    private StandardMaterial3D? _meteorMaterial;
    private float _meteorPhase;
    private double _meteorCooldown = MeteorInitialDelaySeconds;
    private double _meteorWindow;
    private bool _meteorGrabbed;
    private Vector3 _meteorDragPlanePoint;
    private Vector2 _lastDragMouse;
    private Vector2 _dragVelocity;
    private Vector3I? _meteorImpactVoxel;
    private Vector3 _meteorImpactStart;
    private double _meteorImpactProgress;

    private CanvasLayer? _uiLayer;
    private Label? _status;

    public bool CloudEnabled => _cloudEnabled;
    public bool MeteorEnabled => _meteorEnabled;
    public int CloudCharge => _cloudCharge;
    public bool MeteorActive => _meteor is not null;

    public void Initialize(
        VirtualWorld world,
        WorldView view,
        MiningService mining,
        OrbitCameraController camera,
        bool cloudEnabled,
        bool meteorEnabled)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _cloudEnabled = cloudEnabled;
        _meteorEnabled = meteorEnabled;

        // The phase is content-deterministic. Reloading the same world does not select a different
        // starting orbit merely because a RandomNumberGenerator happened to be called at a new time.
        _cloudPhase = DeterministicPhase(world.Profile.Seed + 401);
        _meteorPhase = DeterministicPhase(world.Profile.Seed + 1709);
    }

    public override void _Ready()
    {
        if (!_cloudEnabled && !_meteorEnabled)
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }

        BuildUi();
        if (_cloudEnabled)
        {
            _cloud = BuildCloud();
            AddChild(_cloud);
        }
        RefreshStatus();
    }

    public override void _Process(double delta)
    {
        if (_cloudEnabled && _cloud is not null)
        {
            _cloudPhase = Mathf.Wrap(_cloudPhase + (float)delta * 0.085f, 0.0f, Mathf.Tau);
            _cloud.GlobalPosition = OrbitPosition(_cloudPhase, 1.30f, 0.22f);
            FaceWorld(_cloud);
        }

        if (_meteorEnabled)
        {
            ProcessMeteor(delta);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_camera.IsManipulating) return;

        if (@event is InputEventMouseMotion motion && _meteorGrabbed && _meteor is not null)
        {
            _dragVelocity = motion.Position - _lastDragMouse;
            _lastDragMouse = motion.Position;
            MoveMeteorToPointer(motion.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (button.Pressed)
        {
            if (_meteorEnabled && _meteor is not null && _meteorImpactVoxel is null && HitScreenPoint(_meteor, button.Position, 42.0f))
            {
                _meteorGrabbed = true;
                _meteorDragPlanePoint = _meteor.GlobalPosition;
                _lastDragMouse = button.Position;
                _dragVelocity = Vector2.Zero;
                GetViewport().SetInputAsHandled();
                RefreshStatus();
                return;
            }

            if (_cloudEnabled && _cloud is not null && HitScreenPoint(_cloud, button.Position, 56.0f))
            {
                ChargeCloud();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_meteorGrabbed && _meteor is not null)
        {
            _meteorGrabbed = false;
            TryReleaseMeteor(button.Position);
            GetViewport().SetInputAsHandled();
            RefreshStatus();
        }
    }

    private void ChargeCloud()
    {
        _cloudCharge = Math.Min(CloudClicksToCharge, _cloudCharge + 1);
        RefreshCloudMaterial();

        if (_cloudCharge < CloudClicksToCharge)
        {
            RefreshStatus();
            return;
        }

        _cloudCharge = 0;
        RefreshCloudMaterial();
        if (_cloud is not null && TryResolveSurfaceBelow(_cloud.GlobalPosition, out Vector3I target))
        {
            ApplyWorldEventCrater(target, LightningRadius);
            SpawnImpactFlash(_view.VoxelToWorld(target), lightning: true);
        }
        RefreshStatus();
    }

    private void ProcessMeteor(double delta)
    {
        if (_meteor is null)
        {
            _meteorCooldown -= Math.Max(0.0, delta);
            if (_meteorCooldown <= 0.0)
            {
                SpawnMeteor();
            }
            return;
        }

        if (_meteorImpactVoxel is Vector3I impact)
        {
            _meteorImpactProgress = Math.Min(1.0, _meteorImpactProgress + Math.Max(0.0, delta) / 0.38);
            float t = Smooth01((float)_meteorImpactProgress);
            Vector3 target = _view.VoxelToWorld(impact);
            _meteor.GlobalPosition = _meteorImpactStart.Lerp(target, t);
            _meteor.Scale = Vector3.One * Mathf.Lerp(1.0f, 0.58f, t);
            if (_meteorImpactProgress >= 1.0)
            {
                ApplyWorldEventCrater(impact, MeteorRadius);
                SpawnImpactFlash(target, lightning: false);
                DespawnMeteor();
            }
            return;
        }

        if (_meteorGrabbed)
        {
            return;
        }

        _meteorWindow -= Math.Max(0.0, delta);
        if (_meteorWindow <= 0.0)
        {
            DespawnMeteor();
            return;
        }

        _meteorPhase = Mathf.Wrap(_meteorPhase + (float)delta * 0.19f, 0.0f, Mathf.Tau);
        _meteor.GlobalPosition = OrbitPosition(_meteorPhase, 1.48f, -0.31f);
        _meteor.RotateY((float)delta * 1.7f);
    }

    private void SpawnMeteor()
    {
        _meteor = new Node3D { Name = "CatchableMeteor" };
        _meteorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.32f, 0.26f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.30f, 0.08f),
            EmissionEnergyMultiplier = 1.8f,
            Roughness = 0.86f,
        };
        var mesh = new SphereMesh
        {
            Radius = MathF.Max(0.55f, _world.Profile.BlockSpacing * 0.45f),
            Height = MathF.Max(1.10f, _world.Profile.BlockSpacing * 0.90f),
            RadialSegments = 12,
            Rings = 8,
            Material = _meteorMaterial,
        };
        _meteor.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
        AddChild(_meteor);
        _meteorWindow = MeteorWindowSeconds;
        _meteor.GlobalPosition = OrbitPosition(_meteorPhase, 1.48f, -0.31f);
        RefreshStatus();
    }

    private void DespawnMeteor()
    {
        if (_meteor is not null)
        {
            _meteor.QueueFree();
        }
        _meteor = null;
        _meteorMaterial = null;
        _meteorGrabbed = false;
        _meteorImpactVoxel = null;
        _meteorImpactProgress = 0.0;
        _meteorCooldown = MeteorRespawnSeconds;
        RefreshStatus();
    }

    private void TryReleaseMeteor(Vector2 mousePosition)
    {
        if (_meteor is null) return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 predicted = mousePosition + _dragVelocity * 6.0f;
        predicted.X = Math.Clamp(predicted.X, 0.0f, viewportSize.X);
        predicted.Y = Math.Clamp(predicted.Y, 0.0f, viewportSize.Y);

        float maxDistance = _world.GetWorldBounds().Size.Length() * 3.0f;
        Vector3I target;
        bool hit = VoxelRaycaster.TryRaycast(_world, _camera.Camera, predicted, maxDistance, out target)
            || VoxelRaycaster.TryRaycast(_world, _camera.Camera, mousePosition, maxDistance, out target);

        if (!hit)
        {
            // Missed throws simply return to orbit. The mechanic stays forgiving; the meteor is not
            // consumed unless the assisted flick actually resolves to a cube-surface target.
            return;
        }

        _meteorImpactVoxel = target;
        _meteorImpactStart = _meteor.GlobalPosition;
        _meteorImpactProgress = 0.0;
    }

    private void MoveMeteorToPointer(Vector2 pointer)
    {
        if (_meteor is null) return;

        Vector3 origin = _camera.Camera.ProjectRayOrigin(pointer);
        Vector3 direction = _camera.Camera.ProjectRayNormal(pointer).Normalized();
        Vector3 planeNormal = _camera.Camera.GlobalTransform.Basis.Z.Normalized();
        float denominator = direction.Dot(planeNormal);
        if (MathF.Abs(denominator) < 0.0001f) return;

        float distance = (_meteorDragPlanePoint - origin).Dot(planeNormal) / denominator;
        if (distance <= 0.0f) return;
        _meteor.GlobalPosition = origin + direction * distance;
    }

    private void ApplyWorldEventCrater(Vector3I center, int radius)
    {
        AreaMiningResult result = _mining.TryMineCrater(center, radius, MiningSource.WorldEvent);
        if (!result.Success) return;

        foreach (Vector3I voxel in result.RemovedVoxels)
        {
            _view.MarkDirtyAround(voxel);
        }
    }

    private bool TryResolveSurfaceBelow(Vector3 worldPosition, out Vector3I voxel)
    {
        float spacing = _world.Profile.BlockSpacing;
        Vector3 grid = worldPosition / spacing;
        var probe = new Vector3I(
            Mathf.RoundToInt(grid.X),
            Mathf.RoundToInt(grid.Y),
            Mathf.RoundToInt(grid.Z));
        Vector3I normal = DominantNormal(probe);

        int u;
        int v;
        if (normal.X != 0)
        {
            u = probe.Y;
            v = probe.Z;
        }
        else if (normal.Y != 0)
        {
            u = probe.X;
            v = probe.Z;
        }
        else
        {
            u = probe.X;
            v = probe.Y;
        }

        return _world.Source.TrySampleOutermostSurfaceVoxel(normal, u, v, out voxel, out _);
    }

    private Node3D BuildCloud()
    {
        var root = new Node3D { Name = "ChargedCloud" };
        _cloudMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.94f, 0.97f, 1.0f),
            Roughness = 0.94f,
            EmissionEnabled = true,
            Emission = new Color(0.12f, 0.18f, 0.28f),
            EmissionEnergyMultiplier = 0.25f,
        };

        Vector3[] cells =
        [
            new Vector3(-1.35f, 0.0f, 0.0f),
            new Vector3(-0.45f, 0.18f, 0.18f),
            new Vector3(0.45f, 0.0f, -0.12f),
            new Vector3(1.35f, 0.12f, 0.12f),
            new Vector3(0.0f, 0.46f, 0.0f),
        ];
        float unit = MathF.Max(0.7f, _world.Profile.BlockSpacing * 0.55f);
        foreach (Vector3 cell in cells)
        {
            var mesh = new BoxMesh
            {
                Size = new Vector3(unit * 1.25f, unit * 0.42f, unit * 0.92f),
                Material = _cloudMaterial,
            };
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                Position = cell * unit,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        root.GlobalPosition = OrbitPosition(_cloudPhase, 1.30f, 0.22f);
        return root;
    }

    private void RefreshCloudMaterial()
    {
        if (_cloudMaterial is null) return;
        float t = _cloudCharge / (float)CloudClicksToCharge;
        _cloudMaterial.AlbedoColor = new Color(
            Mathf.Lerp(0.94f, 0.72f, t),
            Mathf.Lerp(0.97f, 0.78f, t),
            1.0f);
        _cloudMaterial.Emission = new Color(0.25f + t * 0.50f, 0.32f + t * 0.42f, 0.55f + t * 0.45f);
        _cloudMaterial.EmissionEnergyMultiplier = 0.25f + t * 1.9f;
    }

    private Vector3 OrbitPosition(float phase, float radiusMultiplier, float verticalBias)
    {
        float halfExtent = _world.GetWorldBounds().Size.MaxAxisIndex() switch
        {
            0 => _world.GetWorldBounds().Size.X * 0.5f,
            1 => _world.GetWorldBounds().Size.Y * 0.5f,
            _ => _world.GetWorldBounds().Size.Z * 0.5f,
        };
        float radius = MathF.Max(8.0f, halfExtent * radiusMultiplier + _world.Profile.BlockSpacing * 3.0f);
        float y = radius * verticalBias + MathF.Sin(phase * 0.73f) * radius * 0.18f;
        return new Vector3(MathF.Cos(phase) * radius, y, MathF.Sin(phase) * radius);
    }

    private static void FaceWorld(Node3D node)
    {
        Vector3 outward = node.GlobalPosition.Normalized();
        if (outward.LengthSquared() < 0.0001f) return;
        Vector3 reference = MathF.Abs(outward.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
        Vector3 xAxis = reference.Cross(outward).Normalized();
        Vector3 zAxis = xAxis.Cross(outward).Normalized();
        node.GlobalBasis = new Basis(xAxis, outward, zAxis).Orthonormalized();
    }

    private bool HitScreenPoint(Node3D node, Vector2 pointer, float radiusPixels)
    {
        if (_camera.Camera.IsPositionBehind(node.GlobalPosition)) return false;
        Vector2 screen = _camera.Camera.UnprojectPosition(node.GlobalPosition);
        return screen.DistanceSquaredTo(pointer) <= radiusPixels * radiusPixels;
    }

    private void SpawnImpactFlash(Vector3 position, bool lightning)
    {
        var light = new OmniLight3D
        {
            Name = lightning ? "LightningFlash" : "MeteorFlash",
            GlobalPosition = position,
            LightColor = lightning ? new Color(0.78f, 0.88f, 1.0f) : new Color(1.0f, 0.42f, 0.16f),
            LightEnergy = lightning ? 8.0f : 6.0f,
            OmniRange = _world.Profile.BlockSpacing * (lightning ? 7.0f : 9.0f),
            ShadowEnabled = false,
        };
        AddChild(light);
        Tween tween = CreateTween();
        tween.TweenProperty(light, "light_energy", 0.0f, lightning ? 0.20f : 0.34f);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    private void BuildUi()
    {
        _uiLayer = new CanvasLayer { Layer = 18, Name = "WorldEventUi" };
        AddChild(_uiLayer);
        _status = new Label
        {
            Position = new Vector2(16, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontSizeOverride("font_size", 15);
        _uiLayer.AddChild(_status);
    }

    private void RefreshStatus()
    {
        if (_status is null) return;
        string cloud = _cloudEnabled
            ? $"Charged cloud: {_cloudCharge}/{CloudClicksToCharge} clicks"
            : string.Empty;
        string meteor = _meteorEnabled
            ? _meteor is null
                ? $"Meteor: next pass in {Math.Max(0.0, _meteorCooldown):0}s"
                : _meteorGrabbed
                    ? "Meteor: drag and flick toward the cube"
                    : _meteorImpactVoxel is not null
                        ? "Meteor: impact locked"
                        : $"Meteor: catch it ({Math.Max(0.0, _meteorWindow):0}s)"
            : string.Empty;
        _status.Text = string.Join("   |   ", new[] { cloud, meteor }.Where(text => !string.IsNullOrEmpty(text)));
    }

    private static Vector3I DominantNormal(Vector3I coordinate)
    {
        int ax = Math.Abs(coordinate.X);
        int ay = Math.Abs(coordinate.Y);
        int az = Math.Abs(coordinate.Z);
        if (ax >= ay && ax >= az) return coordinate.X >= 0 ? Vector3I.Right : Vector3I.Left;
        if (ay >= ax && ay >= az) return coordinate.Y >= 0 ? Vector3I.Up : Vector3I.Down;
        return coordinate.Z >= 0 ? Vector3I.Back : Vector3I.Forward;
    }

    private static float DeterministicPhase(int seed)
    {
        unchecked
        {
            uint value = (uint)seed * 747796405u + 2891336453u;
            value = ((value >> ((int)(value >> 28) + 4)) ^ value) * 277803737u;
            value = (value >> 22) ^ value;
            return (value / (float)uint.MaxValue) * Mathf.Tau;
        }
    }

    private static float Smooth01(float value)
    {
        float t = Math.Clamp(value, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
