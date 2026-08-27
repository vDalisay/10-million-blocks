using System;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Late-game active/idle bridge. One capacitor is charged by player clicks and Hover Mining auto-actions.
/// At full charge it automatically fires a wide cursor-tracked beam, then enters a hard cooldown. The
/// optional Resource Furnace can extend a natural burst by spending ordinary currency, making the laser
/// a deliberate late resource sink without changing the free charge/cooldown loop.
/// </summary>
public partial class LaserMiningController : Node3D
{
    private const double DamageTickSeconds = 0.10;
    private const int MaxDamageTicksPerFrame = 8;

    private VirtualWorld _world = null!;
    private OrbitCameraController _camera = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private ManualMiningController _manual = null!;

    private double _charge;
    private double _cooldownRemaining;
    private double _activeRemaining;
    private double _damageAccumulator;
    private double _resourceBurnAccumulator;
    private bool _overburning;
    private bool _resourceBurnEnabled;

    private MeshInstance3D _beam = null!;
    private MeshInstance3D _beamGlow = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _detail = null!;
    private ProgressBar _bar = null!;
    private Button _burnToggle = null!;

    public event Action? StateChanged;

    public bool InputEnabled { get; set; } = true;
    public double Charge => Math.Clamp(_charge, 0.0, 1.0);
    public double CooldownRemainingForSave
        => _overburning ? Math.Max(1.0, _skills.Derived.LaserCooldownSeconds) : Math.Max(0.0, _cooldownRemaining);
    public double ActiveRemainingForSave => _overburning ? 0.0 : Math.Max(0.0, _activeRemaining);
    public bool ResourceBurnEnabled => _resourceBurnEnabled;

    public void Initialize(
        VirtualWorld world,
        OrbitCameraController camera,
        WorldView view,
        MiningService mining,
        SkillTreeService skills,
        ManualMiningController manual)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _manual = manual ?? throw new ArgumentNullException(nameof(manual));

        BuildBeam();
        BuildHud();
        _manual.MiningActionPerformed += OnMiningActionPerformed;
        _skills.Changed += OnSkillsChanged;
        RefreshHud();
    }

    public override void _ExitTree()
    {
        if (_manual is not null) _manual.MiningActionPerformed -= OnMiningActionPerformed;
        if (_skills is not null) _skills.Changed -= OnSkillsChanged;
    }

    public void RestoreState(double charge, double cooldownSeconds, double activeSeconds, bool resourceBurnEnabled)
    {
        _charge = Math.Clamp(charge, 0.0, 1.0);
        _cooldownRemaining = Math.Clamp(cooldownSeconds, 0.0, Math.Max(60.0, _skills.Derived.LaserCooldownSeconds));
        _activeRemaining = Math.Clamp(activeSeconds, 0.0, Math.Max(5.0, _skills.Derived.LaserDurationSeconds));
        _resourceBurnEnabled = resourceBurnEnabled && _skills.Derived.LaserResourceBurnUnlocked;
        _overburning = false;
        if (_activeRemaining > 0.0) _cooldownRemaining = 0.0;
        else if (_cooldownRemaining > 0.0) _charge = 0.0;
        RefreshHud();
    }

    public override void _Process(double delta)
    {
        double dt = Math.Max(0.0, delta);
        bool unlocked = _skills.Derived.LaserUnlocked;
        _panel.Visible = unlocked;
        if (!unlocked)
        {
            HideBeam();
            return;
        }

        if (!InputEnabled || !_manual.InputEnabled)
        {
            HideBeam();
            RefreshHud();
            return;
        }

        if (_cooldownRemaining > 0.0)
        {
            double before = _cooldownRemaining;
            _cooldownRemaining = Math.Max(0.0, _cooldownRemaining - dt);
            HideBeam();
            if (before > 0.0 && _cooldownRemaining <= 0.0) StateChanged?.Invoke();
            RefreshHud();
            return;
        }

        if (_activeRemaining > 0.0)
        {
            // Consume only the authored natural-burst slice that actually remains. A long or final
            // render frame must not turn a 5.0-second burst into 5.0s + one frame of free damage.
            double activeDt = Math.Min(dt, _activeRemaining);
            _activeRemaining = Math.Max(0.0, _activeRemaining - activeDt);
            FireLaser(activeDt);
            if (_activeRemaining <= 0.0)
            {
                if (_skills.Derived.LaserResourceBurnUnlocked && _resourceBurnEnabled && _mining.Currency > 0)
                {
                    _overburning = true;
                    _resourceBurnAccumulator = 0.0;
                    StateChanged?.Invoke();
                }
                else
                {
                    BeginCooldown();
                }
            }
            RefreshHud();
            return;
        }

        if (_overburning)
        {
            if (!_skills.Derived.LaserResourceBurnUnlocked
                || !_resourceBurnEnabled
                || !PayForOverburn(dt))
            {
                _overburning = false;
                BeginCooldown();
                HideBeam();
                RefreshHud();
                return;
            }

            FireLaser(dt);
            RefreshHud();
            return;
        }

        if (_charge >= 1.0 && TryResolveTarget(out _, out _, out _))
        {
            StartBurst();
        }
        else
        {
            HideBeam();
        }
        RefreshHud();
    }

    private bool CanCharge()
        => _skills.Derived.LaserUnlocked
           && InputEnabled
           && _manual.InputEnabled
           && _cooldownRemaining <= 0.0
           && _activeRemaining <= 0.0
           && !_overburning
           && _charge < 1.0;

    private void OnMiningActionPerformed(bool automatic)
    {
        if (!CanCharge()) return;
        AddCharge(automatic
            ? _skills.Derived.LaserAutoChargePerAction
            : _skills.Derived.LaserManualChargePerAction);
    }

    private void AddCharge(double amount)
    {
        if (amount <= 0.0 || !CanCharge()) return;
        double before = _charge;
        _charge = Math.Clamp(_charge + amount, 0.0, 1.0);
        if (_charge > before) StateChanged?.Invoke();
        RefreshHud();
    }

    private void StartBurst()
    {
        _charge = 1.0;
        _activeRemaining = Math.Max(0.5, _skills.Derived.LaserDurationSeconds);
        _damageAccumulator = 0.0;
        _resourceBurnAccumulator = 0.0;
        _overburning = false;
        StateChanged?.Invoke();
    }

    private void BeginCooldown()
    {
        _charge = 0.0;
        _activeRemaining = 0.0;
        _damageAccumulator = 0.0;
        _resourceBurnAccumulator = 0.0;
        _cooldownRemaining = Math.Max(1.0, _skills.Derived.LaserCooldownSeconds);
        StateChanged?.Invoke();
    }

    private bool PayForOverburn(double delta)
    {
        double perSecond = Math.Max(1.0, _skills.Derived.LaserResourceCostPerSecond);
        _resourceBurnAccumulator += perSecond * Math.Max(0.0, delta);
        long due = Math.Max(0L, (long)Math.Floor(_resourceBurnAccumulator));
        if (due <= 0) return true;
        if (!_mining.TrySpend(due)) return false;
        _resourceBurnAccumulator -= due;
        return true;
    }

    private void FireLaser(double delta)
    {
        if (!TryResolveTarget(out Vector3I center, out Vector3I normal, out Vector3 hitPoint))
        {
            HideBeam();
            return;
        }

        ShowBeam(hitPoint, normal);
        _damageAccumulator += Math.Max(0.0, delta);
        int ticks = 0;
        while (_damageAccumulator >= DamageTickSeconds && ticks < MaxDamageTicksPerFrame)
        {
            _damageAccumulator -= DamageTickSeconds;
            ApplyDamageTick(center, normal, DamageTickSeconds);
            ticks++;
        }
        if (ticks >= MaxDamageTicksPerFrame) _damageAccumulator = Math.Min(_damageAccumulator, DamageTickSeconds);
    }

    private void ApplyDamageTick(Vector3I center, Vector3I normal, double tickSeconds)
    {
        ManualMiningFootprintKind footprint = _skills.Derived.LaserBeamRadius >= 2
            ? ManualMiningFootprintKind.Square5
            : ManualMiningFootprintKind.Square3;
        var targets = ManualMiningFootprint.ResolveFromCenter(_world, center, footprint, normal);
        double damage = Math.Max(0.01, _skills.Derived.LaserDamagePerSecond * tickSeconds);
        int presentation = 0;

        _mining.BeginCurrencyNotificationBatch();
        try
        {
            foreach (Vector3I target in targets)
            {
                MiningResult result = _mining.TryMineManual(target, damage);
                if (!result.Success || !result.Removed) continue;
                _view.MarkDirtyVoxel(result.Voxel);
                if (presentation++ < 3)
                    _view.SpawnManualMinePop(result.Voxel, result.BlockId, 1.36f);
            }
        }
        finally
        {
            _mining.EndCurrencyNotificationBatch();
        }
    }

    private bool TryResolveTarget(out Vector3I voxel, out Vector3I normal, out Vector3 point)
    {
        Camera3D camera = _camera.Camera;
        Vector2 mouse = GetViewport().GetMousePosition();
        float maxDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(_world, camera, mouse, maxDistance, out voxel, out normal))
        {
            float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
            point = (Vector3)voxel * spacing + (Vector3)normal * (spacing * 0.54f);
            return true;
        }

        voxel = default;
        normal = default;
        point = default;
        return false;
    }

    private void BuildBeam()
    {
        var coreMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.96f, 1.0f, 0.92f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.36f, 0.90f, 1.0f),
            EmissionEnergyMultiplier = 7.0f,
        };
        var glowMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.72f, 1.0f, 0.20f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.12f, 0.60f, 1.0f),
            EmissionEnergyMultiplier = 3.5f,
        };

        _beamGlow = new MeshInstance3D
        {
            Name = "FluxLaserGlow",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = glowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_beamGlow);

        _beam = new MeshInstance3D
        {
            Name = "FluxLaserCore",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = coreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_beam);
    }

    private void ShowBeam(Vector3 target, Vector3I surfaceNormal)
    {
        Camera3D camera = _camera.Camera;
        Vector3 start = camera.GlobalPosition + (-camera.GlobalBasis.Z.Normalized()) * 0.35f;
        Vector3 end = target;
        Vector3 delta = end - start;
        float length = Math.Max(0.05f, delta.Length());
        Vector3 middle = start + delta * 0.5f;
        float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
        float width = spacing * (0.055f + 0.012f * Math.Max(1, _skills.Derived.LaserBeamRadius));
        Vector3 up = camera.GlobalBasis.Y.Normalized();
        if (MathF.Abs(delta.Normalized().Dot(up)) > 0.985f) up = camera.GlobalBasis.X.Normalized();

        PositionBeam(_beamGlow, middle, end, up, width * 2.8f, length);
        PositionBeam(_beam, middle, end, up, width, length);
    }

    private static void PositionBeam(MeshInstance3D beam, Vector3 middle, Vector3 target, Vector3 up, float width, float length)
    {
        beam.Visible = true;
        beam.GlobalPosition = middle;
        beam.LookAt(target, up);
        beam.Scale = new Vector3(width, width, length);
    }

    private void HideBeam()
    {
        if (_beam is not null) _beam.Visible = false;
        if (_beamGlow is not null) _beamGlow.Visible = false;
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "FluxLaserHud", Layer = 25 };
        AddChild(layer);
        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -230.0f,
            OffsetTop = 18.0f,
            OffsetRight = 230.0f,
            OffsetBottom = 116.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        layer.AddChild(_panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 3);
        margin.AddChild(column);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _title.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(_title);

        _bar = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 100.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0.0f, 12.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(_bar);

        var row = new HBoxContainer();
        _detail = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _detail.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(_detail);

        _burnToggle = new Button
        {
            Text = "OVERBURN OFF",
            CustomMinimumSize = new Vector2(116.0f, 28.0f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _burnToggle.Pressed += ToggleResourceBurn;
        row.AddChild(_burnToggle);
        column.AddChild(row);
    }

    private void ToggleResourceBurn()
    {
        if (!_skills.Derived.LaserResourceBurnUnlocked) return;
        _resourceBurnEnabled = !_resourceBurnEnabled;
        if (!_resourceBurnEnabled && _overburning)
        {
            _overburning = false;
            BeginCooldown();
        }
        StateChanged?.Invoke();
        RefreshHud();
    }

    private void OnSkillsChanged()
    {
        if (!_skills.Derived.LaserResourceBurnUnlocked)
        {
            _resourceBurnEnabled = false;
            if (_overburning)
            {
                _overburning = false;
                BeginCooldown();
            }
        }
        RefreshHud();
    }

    private void RefreshHud()
    {
        if (_panel is null || _skills is null) return;
        bool unlocked = _skills.Derived.LaserUnlocked;
        _panel.Visible = unlocked;
        if (!unlocked) return;

        _burnToggle.Visible = _skills.Derived.LaserResourceBurnUnlocked;
        _burnToggle.Text = _resourceBurnEnabled ? "OVERBURN ON" : "OVERBURN OFF";

        if (_cooldownRemaining > 0.0)
        {
            double total = Math.Max(1.0, _skills.Derived.LaserCooldownSeconds);
            _bar.Value = Math.Clamp((1.0 - _cooldownRemaining / total) * 100.0, 0.0, 100.0);
            _title.Text = $"FLUX LASER // COOLDOWN  {Math.Ceiling(_cooldownRemaining):0}s";
            _detail.Text = "Capacitor locked until radiator cycle completes";
            return;
        }

        if (_overburning)
        {
            _bar.Value = 100.0;
            _title.Text = "FLUX LASER // RESOURCE OVERBURN";
            _detail.Text = $"{_skills.Derived.LaserDamagePerSecond:0.##} dmg/s  |  burn {_skills.Derived.LaserResourceCostPerSecond:N0} resources/s";
            return;
        }

        if (_activeRemaining > 0.0)
        {
            double duration = Math.Max(0.5, _skills.Derived.LaserDurationSeconds);
            _bar.Value = Math.Clamp(_activeRemaining / duration * 100.0, 0.0, 100.0);
            int width = _skills.Derived.LaserBeamRadius * 2 + 1;
            _title.Text = $"FLUX LASER // ACTIVE  {_activeRemaining:0.0}s";
            _detail.Text = $"{width}x{width} beam  |  {_skills.Derived.LaserDamagePerSecond:0.##} block-damage/s";
            return;
        }

        _bar.Value = Math.Clamp(_charge * 100.0, 0.0, 100.0);
        if (_charge >= 1.0)
        {
            _title.Text = "FLUX LASER // PRIMED";
            _detail.Text = "Aim at a mineable surface to fire automatically";
        }
        else
        {
            _title.Text = $"FLUX LASER // CHARGING  {_charge * 100.0:0}%";
            _detail.Text = $"click +{_skills.Derived.LaserManualChargePerAction * 100.0:0.##}%  |  hover auto +{_skills.Derived.LaserAutoChargePerAction * 100.0:0.##}%";
        }
    }
}
