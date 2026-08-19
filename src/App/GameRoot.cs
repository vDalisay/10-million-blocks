using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Content;
using TenMillionBlocks.Diagnostics;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Progression;
using TenMillionBlocks.Save;
using TenMillionBlocks.Skills;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.App;

public partial class GameRoot : Node3D
{
    private ContentDatabase _content = null!;
    private BlockAssetRegistry _assets = null!;
    private WorldCatalog _worlds = null!;
    private MinerCatalog _minerCatalog = null!;
    private SkillTreeCatalog _skillCatalog = null!;
    private MiningPatternRegistry _patterns = null!;
    private WorldProgressionService _progression = null!;
    private SaveService _saveService = null!;
    private GameSaveData _save = null!;

    private OrbitCameraController _camera = null!;
    private CloudField _clouds = null!;
    private WorldCompleteView _completionView = null!;
    private Node3D? _sessionRoot;
    private VirtualWorld? _world;
    private WorldView? _worldView;
    private MiningService? _mining;
    private SkillTreeService? _skills;
    private ManualMiningController? _manualMining;
    private MinerSimulationService? _miners;
    private MinerPlacementController? _placement;
    private SkillTreeView? _skillTree;
    private PerformanceHud? _performanceHud;
    private StressBenchmarkController? _stressBenchmark;

    private long _manualBlocksThisWorld;
    private long _automatedBlocksThisWorld;
    private bool _completionShown;
    private bool _autosaveDirty;
    private double _autosaveTimer;
    private long _loadedSaveTimestamp;
    private bool _sessionPersists = true;

    public override void _Ready()
    {
        try
        {
            LoadContentAndState();
            AddLightingAndEnvironment();
            BuildPersistentPresentation();
            BuildWorldSession(_progression.CurrentProfile(), applyOfflineProgress: true, persistSession: true);
            GD.Print("Gameplay ready. LMB mines, RMB drag orbits, MMB drag pans, wheel zooms, [K] skill tree, [M] places unlocked drill miner. Debug: [F8] stress world, [F9] performance HUD, [F7] stress benchmark.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to initialize 10 Million Blocks gameplay slice:\n{exception}");
            ShowFatalError(exception.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (!_autosaveDirty || _world is null)
        {
            return;
        }

        _autosaveTimer += delta;
        if (_autosaveTimer >= 10.0)
        {
            TrySaveCurrentSession();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        // Development-only shortcut so completion/Continue can be exercised without manually
        // removing several thousand blocks. It does not alter world state or count as a real clear.
        if (key.Keycode == Key.F10 && OS.IsDebugBuild() && _sessionPersists && !_completionShown && _world is not null)
        {
            ShowCompletion(debugPreview: true);
            GetViewport().SetInputAsHandled();
            return;
        }

        // The stress profile deliberately sits outside authored progression. F8 toggles it without
        // polluting the player's sparse save or progression index.
        if (key.Keycode == Key.F8 && OS.IsDebugBuild() && _world is not null)
        {
            if (_world.Profile.Id == "stress_1000")
            {
                BuildWorldSession(_progression.CurrentProfile(), applyOfflineProgress: false, persistSession: true);
                GD.Print("Returned from stress profile to authored progression world.");
            }
            else
            {
                BuildWorldSession(_worlds.Get("stress_1000"), applyOfflineProgress: false, persistSession: false);
                GD.Print("Loaded stress_1000. [F9] metrics, [F7] 20-second automated benchmark, [F8] return.");
            }
            GetViewport().SetInputAsHandled();
        }
    }

    private void LoadContentAndState()
    {
        _content = ContentDatabase.Load();
        _assets = new BlockAssetRegistry(_content);
        _assets.ValidateAndPreload();

        _worlds = WorldCatalog.Load();
        WorldSelfTest.Run(_worlds);
        _minerCatalog = MinerCatalog.Load();
        _skillCatalog = SkillTreeCatalog.Load();
        _patterns = new MiningPatternRegistry();
        ContentCrossValidator.Validate(_minerCatalog, _patterns, _skillCatalog);

        _progression = WorldProgressionService.Load(_worlds);
        _saveService = new SaveService();
        _save = _saveService.LoadOrCreate();
        _loadedSaveTimestamp = _save.SavedAtUnixSeconds;
        _progression.RestoreIndex(_save.ProgressionIndex);
    }

    private void BuildPersistentPresentation()
    {
        _clouds = new CloudField { Name = "SpacePresentation" };
        AddChild(_clouds);

        _camera = new OrbitCameraController { Name = "OrbitCamera" };
        AddChild(_camera);

        var harness = new ReferenceVisualHarness { Name = "ReferenceVisualHarness" };
        harness.Initialize(_camera);
        AddChild(harness);

        _completionView = new WorldCompleteView { Name = "WorldCompleteView" };
        _completionView.ContinueRequested += OnContinueRequested;
        AddChild(_completionView);
    }

    private void BuildWorldSession(WorldProfile profile, bool applyOfflineProgress, bool persistSession)
    {
        if (_sessionPersists)
        {
            CaptureCurrentSession();
        }
        TearDownWorldSession();
        _sessionPersists = persistSession;
        _completionView.HideCompletion();
        _completionShown = false;

        float worldExtent = profile.BlockSpacing * (profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + MathF.Max(0.0f, profile.SeaLevelOffset));
        _clouds.SetWorldExtent(worldExtent);
        _camera.ConfigureWorldExtent(worldExtent);

        _sessionRoot = new Node3D { Name = $"WorldSession_{profile.Id}" };
        AddChild(_sessionRoot);

        _world = new VirtualWorld(profile);
        long blockCount = _world.InitializeMineableBlockCount();
        GD.Print($"World '{profile.Id}' contains {blockCount:N0} authoritative logical mineable blocks across {_world.TotalLogicalRegionCount:N0} addressable regions.");

        WorldSaveData? savedWorld = null;
        if (persistSession && _save.Worlds.TryGetValue(profile.Id, out WorldSaveData? existing))
        {
            savedWorld = existing;
            _world.State.RestoreSnapshot(existing.MinedChunks, existing.ExhaustedRegions);
            _manualBlocksThisWorld = existing.ManualBlocksMined;
            _automatedBlocksThisWorld = existing.AutomatedBlocksMined;
        }
        else
        {
            _manualBlocksThisWorld = 0;
            _automatedBlocksThisWorld = 0;
        }

        _worldView = new WorldView { Name = "WorldView" };
        _sessionRoot.AddChild(_worldView);
        _worldView.Initialize(_assets, _world, _camera);

        _mining = new MiningService(_world, _content);
        _mining.RestoreCurrency(persistSession ? _save.Currency : 0L);
        _mining.BlockMined += OnBlockMined;
        _mining.BulkMined += OnBulkMined;
        _mining.CurrencyChanged += _ => MarkAutosaveDirty();

        _skills = new SkillTreeService(_skillCatalog, _mining);
        if (persistSession)
        {
            _skills.RestoreRanks(_save.SkillRanks);
        }
        _skills.Changed += MarkAutosaveDirty;

        _manualMining = new ManualMiningController { Name = "ManualMining" };
        _manualMining.Initialize(_world, _camera, _worldView, _mining, _skills);
        _sessionRoot.AddChild(_manualMining);

        _miners = new MinerSimulationService { Name = "MinerSimulation" };
        _miners.Initialize(_world, _mining, _worldView, _minerCatalog, _patterns, _skills);
        _sessionRoot.AddChild(_miners);
        if (savedWorld is not null)
        {
            _miners.RestoreSnapshot(savedWorld.Miners);
        }
        _miners.Changed += MarkAutosaveDirty;

        _placement = new MinerPlacementController { Name = "MinerPlacement" };
        _placement.Initialize(_manualMining, _miners);
        _sessionRoot.AddChild(_placement);

        _skillTree = new SkillTreeView { Name = "SkillTreeView" };
        _skillTree.Initialize(_skills, _mining, _manualMining);
        _sessionRoot.AddChild(_skillTree);

        var hud = new MiningHud { Name = "MiningHud" };
        hud.Initialize(_world, _mining, _worldView, _skills, _miners);
        _sessionRoot.AddChild(hud);

        _performanceHud = new PerformanceHud { Name = "PerformanceHud" };
        _performanceHud.Initialize(_world, _worldView, _camera);
        _sessionRoot.AddChild(_performanceHud);

        _stressBenchmark = new StressBenchmarkController { Name = "StressBenchmark" };
        _stressBenchmark.Initialize(_world, _worldView, _mining, _camera);
        _sessionRoot.AddChild(_stressBenchmark);

        _camera.ApplyPreset(OrbitCameraController.MediumPreset, immediate: true);

        if (persistSession && applyOfflineProgress && savedWorld is not null && _loadedSaveTimestamp > 0)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double elapsed = Math.Max(0L, now - _loadedSaveTimestamp);
            long offlineMined = _miners.ApplyOfflineProgress(elapsed);
            if (offlineMined > 0)
            {
                GD.Print($"Applied {offlineMined:N0} exact offline mining operations after {elapsed:0} seconds away.");
                MarkAutosaveDirty();
            }
        }

        if (persistSession && _world.RemainingMineableBlocks == 0)
        {
            ShowCompletion(debugPreview: false);
        }
    }

    private void TearDownWorldSession()
    {
        if (_sessionRoot is null)
        {
            return;
        }

        RemoveChild(_sessionRoot);
        _sessionRoot.QueueFree();
        _sessionRoot = null;
        _world = null;
        _worldView = null;
        _mining = null;
        _skills = null;
        _manualMining = null;
        _miners = null;
        _placement = null;
        _skillTree = null;
        _performanceHud = null;
        _stressBenchmark = null;
        _autosaveDirty = false;
        _autosaveTimer = 0.0;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (result.Source == MiningSource.Automated || result.Source == MiningSource.Offline)
        {
            _automatedBlocksThisWorld++;
        }
        else if (result.Source == MiningSource.Manual)
        {
            _manualBlocksThisWorld++;
        }

        MarkAutosaveDirty();
        if (_sessionPersists && result.Remaining == 0 && !_completionShown)
        {
            ShowCompletion(debugPreview: false);
        }
    }

    private void OnBulkMined(BulkMiningResult result)
    {
        if (result.Source == MiningSource.Automated || result.Source == MiningSource.Offline)
        {
            _automatedBlocksThisWorld = checked(_automatedBlocksThisWorld + result.BlocksMined);
        }

        _worldView?.MarkRegionDirty(result.Region);
        MarkAutosaveDirty();
        if (_sessionPersists && result.Remaining == 0 && !_completionShown)
        {
            ShowCompletion(debugPreview: false);
        }
    }

    private void ShowCompletion(bool debugPreview)
    {
        if (!_sessionPersists || _world is null || _mining is null || _manualMining is null || _miners is null || _placement is null)
        {
            return;
        }

        _completionShown = true;
        _skillTree?.Close();
        _manualMining.InputEnabled = false;
        _placement.InputEnabled = false;
        _miners.ProcessMode = ProcessModeEnum.Disabled;

        WorldProfile? next = _progression.NextProfile();
        _completionView.ShowCompletion(
            _world.Profile,
            next,
            _mining.TotalMined,
            _mining.Currency,
            _manualBlocksThisWorld,
            _automatedBlocksThisWorld);

        if (debugPreview)
        {
            GD.Print("DEBUG: showing completion-flow preview without marking the world cleared. Continue still tests the next-world transition.");
        }
        else
        {
            CaptureCurrentSession();
            TrySaveCurrentSession(captureFirst: false);
        }
    }

    private void OnContinueRequested()
    {
        if (!_sessionPersists || _world is null)
        {
            return;
        }

        CaptureCurrentSession();

        if (!_progression.Advance())
        {
            _save.ProgressionIndex = _progression.CurrentIndex;
            _saveService.Save(_save);
            _completionView.HideCompletion();
            GD.Print("Current authored test progression is complete.");
            return;
        }

        _save.ProgressionIndex = _progression.CurrentIndex;
        _saveService.Save(_save);
        BuildWorldSession(_progression.CurrentProfile(), applyOfflineProgress: false, persistSession: true);
        MarkAutosaveDirty();
    }

    private void CaptureCurrentSession()
    {
        if (!_sessionPersists || _world is null || _mining is null || _skills is null || _miners is null)
        {
            return;
        }

        _save.Currency = _mining.Currency;
        _save.ProgressionIndex = _progression.CurrentIndex;

        var skillRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string id, int rank) in _skills.Ranks)
        {
            skillRanks[id] = rank;
        }
        _save.SkillRanks = skillRanks;

        _save.Worlds[_world.Profile.Id] = new WorldSaveData
        {
            WorldId = _world.Profile.Id,
            ManualBlocksMined = _manualBlocksThisWorld,
            AutomatedBlocksMined = _automatedBlocksThisWorld,
            MinedChunks = _world.State.CreateSnapshot(),
            ExhaustedRegions = _world.State.CreateExhaustedRegionSnapshot(),
            Miners = _miners.CreateSnapshot(),
        };
    }

    private void MarkAutosaveDirty()
    {
        if (_sessionPersists)
        {
            _autosaveDirty = true;
        }
    }

    private void TrySaveCurrentSession(bool captureFirst = true)
    {
        try
        {
            if (captureFirst)
            {
                CaptureCurrentSession();
            }
            _saveService.Save(_save);
            _autosaveDirty = false;
            _autosaveTimer = 0.0;
        }
        catch (Exception exception)
        {
            _autosaveTimer = 0.0;
            GD.PushError($"Autosave failed: {exception}");
        }
    }

    private void AddLightingAndEnvironment()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.003f, 0.008f, 0.025f));

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.003f, 0.008f, 0.025f, 1.0f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.74f, 0.78f, 0.84f, 1.0f),
            AmbientLightEnergy = 0.42f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 2.0f,
            SsaoEnabled = true,
            SsaoRadius = 1.6f,
            SsaoIntensity = 2.6f,
            SsaoPower = 1.4f,
        };

        AddChild(new WorldEnvironment
        {
            Name = "WorldEnvironment",
            Environment = environment,
        });

        var keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-52.0f, -34.0f, 0.0f),
            LightColor = new Color(1.0f, 0.98f, 0.94f),
            LightEnergy = 1.05f,
            LightSpecular = 0.0f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 140.0f,
            ShadowBlur = 1.4f,
        };
        AddChild(keyLight);

        var fillLight = new DirectionalLight3D
        {
            Name = "FillLight",
            RotationDegrees = new Vector3(24.0f, 146.0f, 0.0f),
            LightColor = new Color(0.72f, 0.82f, 1.0f),
            LightEnergy = 0.45f,
            LightSpecular = 0.0f,
            ShadowEnabled = false,
        };
        AddChild(fillLight);
    }

    private void ShowFatalError(string message)
    {
        var canvas = new CanvasLayer { Layer = 100 };
        AddChild(canvas);

        var panel = new PanelContainer
        {
            OffsetLeft = 32.0f,
            OffsetTop = 32.0f,
            OffsetRight = 760.0f,
            OffsetBottom = 190.0f,
        };
        canvas.AddChild(panel);

        var label = new Label
        {
            Text = "Startup validation failed\n\n" + message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        panel.AddChild(label);
    }
}
