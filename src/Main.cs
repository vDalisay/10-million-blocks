using System.Threading.Tasks;
using Godot;
using TenMillionBlocks.Core;
using TenMillionBlocks.Gameplay;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;

namespace TenMillionBlocks;

public sealed partial class Main : Node3D
{
    private readonly GameState _state = new();
    private readonly UpgradeSystem _upgrades = new();

    private VoxelWorld? _world;
    private OrbitCameraController? _camera;
    private ManualMiningController? _manualMining;
    private AutoMiningController? _autoMining;
    private HudController? _hud;
    private MiningService? _miningService;

    private bool _transitioning;

    public override void _Ready()
    {
        BuildLighting();
        BuildClouds();

        _world = new VoxelWorld { Name = "VoxelWorld" };
        AddChild(_world);

        _camera = new OrbitCameraController { Name = "OrbitCamera" };
        AddChild(_camera);

        _miningService = new MiningService(_world, _state, _upgrades);

        _manualMining = new ManualMiningController { Name = "ManualMining" };
        AddChild(_manualMining);
        _manualMining.Initialize(_world, _camera, _miningService, _upgrades);

        _autoMining = new AutoMiningController { Name = "AutoMining" };
        AddChild(_autoMining);
        _autoMining.Initialize(_world, _miningService, _upgrades, _state.CurrentSeed);

        _hud = new HudController { Name = "HUD" };
        AddChild(_hud);
        _hud.Initialize(_state, _upgrades, _world, _miningService);

        _world.WorldCleared += OnWorldCleared;
        _world.WorldGenerated += () => _camera.Frame(_world.Bounds);

        StartCurrentStage();
    }

    private void StartCurrentStage()
    {
        if (_world is null || _autoMining is null || _hud is null)
        {
            return;
        }

        _hud.HideBanner();
        _world.GenerateWorld(_state.CurrentStageTarget, _state.CurrentSeed);
        _autoMining.Reseed(_state.CurrentSeed);
        _manualMining?.SetEnabled(true);
        _autoMining.SetEnabled(true);
    }

    private async void OnWorldCleared()
    {
        if (_transitioning || _world is null || _hud is null)
        {
            return;
        }

        _transitioning = true;
        _manualMining?.SetEnabled(false);
        _autoMining?.SetEnabled(false);

        int clearedTarget = _world.TargetBlockCount;
        int bonus = GameConfig.StageCompletionBonus(clearedTarget);
        _state.AddCurrency(bonus);

        bool hasNextStage = _state.StageIndex < GameConfig.StageBlockCounts.Length - 1;
        if (hasNextStage)
        {
            int nextTarget = GameConfig.StageBlockCounts[_state.StageIndex + 1];
            _hud.ShowBanner($"WORLD CLEARED\n+{bonus:N0} ▣\n\nNEXT: {nextTarget:N0} BLOCKS");
        }
        else
        {
            _hud.ShowBanner($"10,000 BLOCKS CLEARED\n+{bonus:N0} ▣\n\nENDLESS WORLD INCOMING");
        }

        await WaitSeconds(1.65f);

        if (!_state.AdvanceStage())
        {
            _state.AdvanceEndlessSeed();
        }

        StartCurrentStage();
        _transitioning = false;
    }

    private async Task WaitSeconds(float seconds)
    {
        SceneTreeTimer timer = GetTree().CreateTimer(seconds);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }

    private void BuildLighting()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.008f, 0.012f, 0.035f));

        var keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-52.0f, -32.0f, 0.0f),
            LightColor = new Color(1.0f, 0.95f, 0.84f),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 90.0f,
        };
        AddChild(keyLight);

        var fill = new OmniLight3D
        {
            Name = "CoolFill",
            Position = new Vector3(-12.0f, 10.0f, 15.0f),
            LightColor = new Color(0.34f, 0.55f, 1.0f),
            LightEnergy = 5.0f,
            OmniRange = 45.0f,
            ShadowEnabled = false,
        };
        AddChild(fill);

        var rim = new OmniLight3D
        {
            Name = "WarmRim",
            Position = new Vector3(14.0f, -5.0f, -10.0f),
            LightColor = new Color(0.40f, 0.78f, 0.62f),
            LightEnergy = 2.4f,
            OmniRange = 38.0f,
            ShadowEnabled = false,
        };
        AddChild(rim);
    }

    private void BuildClouds()
    {
        var clouds = new CloudField
        {
            Name = "CloudField",
        };
        AddChild(clouds);
        clouds.Build(7_301);
    }
}
