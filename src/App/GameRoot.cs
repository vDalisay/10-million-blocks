using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Content;
using TenMillionBlocks.Diagnostics;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Progression;
using TenMillionBlocks.Replay;
using TenMillionBlocks.Save;
using TenMillionBlocks.Skills;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;
using TenMillionBlocks.WorldEvents;

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
    private SpecialResourceInventory _specialResources = null!;

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
    private WorldEventController? _worldEvents;
    private ReplayRecorder? _replayRecorder;
    private ReplayPlayer? _replayPlayer;
    private ReplayView? _replayView;
    private string _replayPath = string.Empty;

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
            GD.Print("Gameplay ready. LMB mines; RMB/MMB/wheel control the camera. World-specific systems appear when available. Debug: [F8] stress world, [F9] performance HUD, [F7] stress benchmark.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to initialize 10 Million Blocks gameplay slice:\n{exception}");
            ShowFatalError(exception.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (!_autosaveDirty || _world is null) return;
        _autosaveTimer += delta;
        if (_autosaveTimer >= 10.0) TrySaveCurrentSession();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        if (key.Keycode == Key.F10 && OS.IsDebugBuild() && _sessionPersists && !_completionShown && _world is not null)
        {
            ShowCompletion(debugPreview: true);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.F8 && OS.IsDebugBuild() && _sessionPersists && _world is not null)
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
        _save = _saveService.LoadOrCreate(_worlds);
        _loadedSaveTimestamp = _save.SavedAtUnixSeconds;
        _progression.RestoreWorld(_save.CurrentWorldId);
        _save.UnlockedWorldIds.Add(_progression.CurrentWorldId);

        _specialResources = new SpecialResourceInventory();
        _specialResources.Restore(_save.SpecialResources);
        _specialResources.Changed += MarkAutosaveDirty;
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
        _completionView.ReplayRequested += OnReplayRequested;
        AddChild(_completionView);
    }

    private void BuildWorldSession(WorldProfile profile, bool applyOfflineProgress, bool persistSession)
    {
        if (_sessionPersists) CaptureCurrentSession();
        TearDownWorldSession();
        _sessionPersists = persistSession;
        _completionView.HideCompletion();
        _completionShown = false;
        ConfigureWorldPresentation(profile);

        _sessionRoot = new Node3D { Name = $"WorldSession_{profile.Id}" };
        AddChild(_sessionRoot);

        _world = new VirtualWorld(profile);
        long blockCount = _world.InitializeMineableBlockCount();
        GD.Print($"World '{profile.Id}' contains {blockCount:N0} authoritative logical mineable blocks across {_world.TotalLogicalRegionCount:N0} addressable regions.");

        WorldSaveData? savedWorld = null;
        if (persistSession && _save.Worlds.TryGetValue(profile.Id, out WorldSaveData? existing))
        {
            if (existing.WorldVersion != profile.WorldVersion || existing.GenerationVersion != profile.GenerationVersion)
            {
                throw new InvalidOperationException(
                    $"Save world '{profile.Id}' uses world/generation version {existing.WorldVersion}/{existing.GenerationVersion}; " +
                    $"this build requires {profile.WorldVersion}/{profile.GenerationVersion}. Reset or migrate the save explicitly.");
            }

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

        _mining = new MiningService(_world, _content, _specialResources);
        long startingCurrency = persistSession
            ? profile.UsesTutorialLocalWallet
                ? savedWorld?.TutorialLocalCurrency ?? 0L
                : _save.PersistentMainCurrency
            : 0L;
        _mining.RestoreCurrency(startingCurrency);
        // BlockMined is attached after ResourceCollectionField so deferred rewards exist before
        // completion/stat observers see the removal.
        _mining.BulkMined += OnBulkMined;
        _mining.CurrencyChanged += _ => MarkAutosaveDirty();

        if (persistSession)
        {
            _replayPath = string.IsNullOrWhiteSpace(savedWorld?.ReplayFile) ? ReplayPath(profile) : savedWorld!.ReplayFile;
            string replayAbsolute = ProjectSettings.GlobalizePath(_replayPath);
            _replayRecorder = new ReplayRecorder(
                _world,
                _mining,
                System.IO.File.Exists(replayAbsolute) ? replayAbsolute : null);
        }
        else
        {
            _replayPath = string.Empty;
            _replayRecorder = null;
        }

        _skills = new SkillTreeService(_skillCatalog, _mining, _specialResources);
        if (persistSession) _skills.RestoreRanks(_save.SkillRanks);
        _skills.Changed += MarkAutosaveDirty;

        _manualMining = new ManualMiningController { Name = "ManualMining" };
        _manualMining.Initialize(_world, _camera, _worldView, _mining, _skills);
        if (savedWorld is not null) _manualMining.RestoreHoverMiningEnabled(savedWorld.HoverMiningEnabled);
        _sessionRoot.AddChild(_manualMining);

        _resourceCollection = new ResourceCollectionField { Name = "ResourceCollectionField" };
        _resourceCollection.Initialize(_world, _mining, _skills, _camera, _manualMining);
        if (savedWorld is not null) _resourceCollection.RestoreSnapshot(savedWorld.PendingPickups);
        _resourceCollection.PendingChanged += OnPendingCollectionChanged;
        _sessionRoot.AddChild(_resourceCollection);
        _mining.BlockMined += OnBlockMined;

        _miners = new MinerSimulationService { Name = "MinerSimulation" };
        _miners.Initialize(_world, _mining, _worldView, _minerCatalog, _patterns, _skills);
        _sessionRoot.AddChild(_miners);
        if (savedWorld is not null) _miners.RestoreSnapshot(savedWorld.Miners);
        _miners.Changed += MarkAutosaveDirty;

        _placement = new MinerPlacementController { Name = "MinerPlacement" };
        _placement.Initialize(_manualMining, _miners);
        _placement.InputEnabled = profile.AutomationAvailable;
        _sessionRoot.AddChild(_placement);

        if (profile.SkillTreeAvailable)
        {
            _skillTree = new SkillTreeView { Name = "SkillTreeView" };
            _skillTree.Initialize(_skills, _mining, _manualMining, _specialResources);
            _sessionRoot.AddChild(_skillTree);
        }

        var hud = new MiningHud { Name = "MiningHud" };
        hud.Initialize(_world, _mining, _worldView, _skills, _miners, _manualMining, _placement);
        _sessionRoot.AddChild(hud);

        // Incremental-game feedback remains presentation-only. ResourceCollectionField has already
        // decided whether ordinary manual/live-automation rewards are banked or deferred pickups.
        var incrementalFeedback = new IncrementalFeedbackView { Name = "IncrementalFeedbackView" };
        incrementalFeedback.Initialize(_world, _worldView, _mining, _specialResources, _assets);
        _sessionRoot.AddChild(incrementalFeedback);

        if (profile.AutomationAvailable)
        {
            var automationAttention = new AutomationAttentionView { Name = "AutomationAttentionView" };
            automationAttention.Initialize(_miners, _worldView);
            _sessionRoot.AddChild(automationAttention);
        }

        if (persistSession && IsActiveWorldEventProfile(profile))
        {
            _worldEvents = new WorldEventController { Name = "WorldEventController" };
            _worldEvents.Initialize(_world, _worldView, _mining, _camera, cloudEnabled: true, meteorEnabled: true);
            _worldEvents.AttachSkills(_skills);
            _worldEvents.PersistentStateChanged += MarkAutosaveDirty;
            _sessionRoot.AddChild(_worldEvents);
            if (savedWorld?.WorldEvents is WorldEventSnapshot eventSnapshot)
            {
                _worldEvents.RestoreSnapshot(eventSnapshot);
            }
        }

        _performanceHud = new PerformanceHud { Name = "PerformanceHud" };
        _performanceHud.Initialize(_world, _worldView, _camera);
        _sessionRoot.AddChild(_performanceHud);

        _stressBenchmark = new StressBenchmarkController { Name = "StressBenchmark" };
        _stressBenchmark.Initialize(_world, _worldView, _mining, _camera);
        _sessionRoot.AddChild(_stressBenchmark);

        ApplyDefaultCameraPreset(profile);

        if (profile.AutomationAvailable && persistSession && applyOfflineProgress && savedWorld is not null && _loadedSaveTimestamp > 0)
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

        if (persistSession
            && _world.RemainingMineableBlocks == 0
            && (_resourceCollection?.PendingCount ?? 0) == 0)
        {
            ShowCompletion(debugPreview: false);
        }
    }

    private void BuildReplaySession(WorldProfile profile, ReplayData replay)
    {
        TearDownWorldSession();
        _sessionPersists = false;
        _completionView.HideCompletion();
        _completionShown = false;
        ConfigureWorldPresentation(profile);

        _sessionRoot = new Node3D { Name = $"ReplaySession_{profile.Id}" };
        AddChild(_sessionRoot);
        _world = new VirtualWorld(profile);
        _world.InitializeMineableBlockCount();
        _worldView = new WorldView { Name = "ReplayWorldView" };
        _sessionRoot.AddChild(_worldView);
        _worldView.Initialize(_assets, _world, _camera);

        _replayPlayer = new ReplayPlayer { Name = "ReplayPlayer" };
        _replayPlayer.Initialize(_world, _worldView, replay);
        _sessionRoot.AddChild(_replayPlayer);

        _replayView = new ReplayView { Name = "ReplayView" };
        _replayView.Initialize(_replayPlayer, profile);
        _replayView.ExitRequested += OnReplayExitRequested;
        _sessionRoot.AddChild(_replayView);
        ApplyDefaultCameraPreset(profile);
        GD.Print($"Replay viewer opened for '{profile.Id}' with {replay.Events.Count:N0} recorded removals.");
    }

    private void TearDownWorldSession()
    {
        _replayRecorder?.Dispose();
        _replayRecorder = null;
        _replayPlayer = null;
        _replayView = null;
        if (_worldEvents is not null) _worldEvents.PersistentStateChanged -= MarkAutosaveDirty;
        _worldEvents = null;
        _replayPath = string.Empty;
        if (_resourceCollection is not null) _resourceCollection.PendingChanged -= OnPendingCollectionChanged;

        if (_sessionRoot is null) return;
        RemoveChild(_sessionRoot);
        _sessionRoot.QueueFree();
        _sessionRoot = null;
        _world = null;
        _worldView = null;
        _mining = null;
        _skills = null;
        _manualMining = null;
        _resourceCollection = null;
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
        if (result.Source is MiningSource.Automated or MiningSource.Offline) _automatedBlocksThisWorld++;
        else if (result.Source == MiningSource.Manual) _manualBlocksThisWorld++;

        MarkAutosaveDirty();
        if (_sessionPersists
            && result.Remaining == 0
            && (_resourceCollection?.PendingCount ?? 0) == 0
            && !_completionShown)
        {
            ShowCompletion(debugPreview: false);
        }
    }

    private void OnBulkMined(BulkMiningResult result)
    {
        if (result.Source is MiningSource.Automated or MiningSource.Offline)
        {
            _automatedBlocksThisWorld = checked(_automatedBlocksThisWorld + result.BlocksMined);
        }
        _worldView?.MarkRegionDirty(result.Region);
        MarkAutosaveDirty();
        if (_sessionPersists && result.Remaining == 0 && !_completionShown) ShowCompletion(debugPreview: false);
    }

    private void ShowCompletion(bool debugPreview)
    {
        if (!_sessionPersists || _world is null || _mining is null || _manualMining is null || _miners is null || _placement is null) return;

        _completionShown = true;
        _skillTree?.Close();
        _manualMining.InputEnabled = false;
        _placement.InputEnabled = false;
        _miners.ProcessMode = ProcessModeEnum.Disabled;
        if (_worldEvents is not null) _worldEvents.ProcessMode = ProcessModeEnum.Disabled;

        WorldProfile? next = _progression.NextProfile();
        _completionView.ShowCompletion(
            _world.Profile,
            next,
            _mining.TotalMined,
            _mining.Currency,
            _manualBlocksThisWorld,
            _automatedBlocksThisWorld,
            replayAvailable: !debugPreview && ReplayAvailableForCurrentWorld());

        if (debugPreview)
        {
            GD.Print("DEBUG: showing completion-flow preview without marking the world cleared. Continue still tests the next-world transition.");
        }
        else
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _save.CompletedWorldIds.Add(_world.Profile.Id);
            if (next is not null) _save.UnlockedWorldIds.Add(next.Id);
            if (_save.Worlds.TryGetValue(_world.Profile.Id, out WorldSaveData? existing))
            {
                existing.Completed = true;
                if (existing.CompletedUnixSeconds <= 0) existing.CompletedUnixSeconds = now;
            }
            CaptureCurrentSession();
            TrySaveCurrentSession(captureFirst: false);
        }
    }

    private void OnContinueRequested()
    {
        if (!_sessionPersists || _world is null) return;
        CaptureCurrentSession();

        if (!_progression.Advance())
        {
            _save.CurrentWorldId = _progression.CurrentWorldId;
            _saveService.Save(_save);
            _completionView.HideCompletion();
            GD.Print("Steam demo progression complete.");
            return;
        }

        _save.CurrentWorldId = _progression.CurrentWorldId;
        _save.UnlockedWorldIds.Add(_progression.CurrentWorldId);
        _saveService.Save(_save);
        BuildWorldSession(_progression.CurrentProfile(), applyOfflineProgress: false, persistSession: true);
        MarkAutosaveDirty();
    }

    private void OnReplayRequested()
    {
        if (!_sessionPersists || _world is null || !_save.CompletedWorldIds.Contains(_world.Profile.Id)) return;

        string worldId = _world.Profile.Id;
        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);

        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? savedWorld) || string.IsNullOrWhiteSpace(savedWorld.ReplayFile))
        {
            GD.PushWarning($"Completed world '{worldId}' has no replay file.");
            return;
        }

        string absolute = ProjectSettings.GlobalizePath(savedWorld.ReplayFile);
        if (!System.IO.File.Exists(absolute))
        {
            GD.PushWarning($"Replay file is missing for '{worldId}': {savedWorld.ReplayFile}");
            return;
        }

        _replayReturnWorldId = worldId;
        ReplayData replay = ReplayBinaryCodec.Read(absolute);
        BuildReplaySession(_worlds.Get(worldId), replay);
    }

    private void OnReplayExitRequested()
    {
        if (_world is null) return;

        string replayWorldId = _world.Profile.Id;
        string returnWorldId = string.IsNullOrWhiteSpace(_replayReturnWorldId)
            ? replayWorldId
            : _replayReturnWorldId;
        _replayReturnWorldId = string.Empty;

        _progression.RestoreWorld(returnWorldId);
        _save.CurrentWorldId = returnWorldId;
        _saveService.Save(_save);
        BuildWorldSession(_worlds.Get(returnWorldId), applyOfflineProgress: false, persistSession: true);
    }

    private void CaptureCurrentSession()
    {
        if (!_sessionPersists || _world is null || _mining is null || _skills is null || _miners is null || _manualMining is null) return;

        _save.SpecialResources = _specialResources.CreateSnapshot();
        _save.CurrentWorldId = _progression.CurrentWorldId;
        _save.UnlockedWorldIds.Add(_world.Profile.Id);

        _save.Worlds.TryGetValue(_world.Profile.Id, out WorldSaveData? previous);
        long tutorialLocalCurrency = previous?.TutorialLocalCurrency ?? 0L;
        if (_world.Profile.UsesTutorialLocalWallet) tutorialLocalCurrency = _mining.Currency;
        else _save.PersistentMainCurrency = _mining.Currency;

        var skillRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string id, int rank) in _skills.Ranks) skillRanks[id] = rank;
        _save.SkillRanks = skillRanks;

        bool completed = _save.CompletedWorldIds.Contains(_world.Profile.Id) || (previous?.Completed ?? false);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long started = previous?.FirstStartedUnixSeconds > 0 ? previous.FirstStartedUnixSeconds : now;
        long completedAt = previous?.CompletedUnixSeconds ?? 0L;
        if (completed && completedAt <= 0) completedAt = now;

        string replayFile = _replayPath;
        if (_replayRecorder is not null && !string.IsNullOrWhiteSpace(_replayPath))
        {
            replayFile = _replayRecorder.FlushToUserPath(_replayPath);
        }

        _save.Worlds[_world.Profile.Id] = new WorldSaveData
        {
            WorldId = _world.Profile.Id,
            WorldVersion = _world.Profile.WorldVersion,
            GenerationVersion = _world.Profile.GenerationVersion,
            InitialMineableBlocks = _world.InitialMineableBlocks,
            TutorialLocalCurrency = tutorialLocalCurrency,
            ManualBlocksMined = _manualBlocksThisWorld,
            AutomatedBlocksMined = _automatedBlocksThisWorld,
            HoverMiningEnabled = _manualMining.HoverMiningEnabled,
            Completed = completed,
            FirstStartedUnixSeconds = started,
            CompletedUnixSeconds = completedAt,
            ReplayFile = replayFile,
            WorldEvents = _worldEvents?.CreateSnapshot() ?? previous?.WorldEvents,
            MinedChunks = _world.State.CreateSnapshot(),
            ExhaustedRegions = _world.State.CreateExhaustedRegionSnapshot(),
            Miners = _miners.CreateSnapshot(),
            PendingPickups = _resourceCollection?.CreateSnapshot() ?? previous?.PendingPickups ?? new(),
        };
    }

    private bool ReplayAvailableForCurrentWorld()
    {
        if (_world is null) return false;
        if (_replayRecorder is not null && _replayRecorder.EventCount > 0) return true;
        if (!_save.Worlds.TryGetValue(_world.Profile.Id, out WorldSaveData? saved) || string.IsNullOrWhiteSpace(saved.ReplayFile)) return false;
        return System.IO.File.Exists(ProjectSettings.GlobalizePath(saved.ReplayFile));
    }

    private static bool IsActiveWorldEventProfile(WorldProfile profile)
        => profile.Id is "reference_lakes" or "reference_ridges";

    private void ConfigureWorldPresentation(WorldProfile profile)
    {
        float worldExtent = profile.BlockSpacing * (
            profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + MathF.Max(0.0f, profile.SeaLevelOffset));
        _clouds.Visible = !profile.UsesSingleBlockGenerator && !profile.UsesSolidCubeGenerator;
        _clouds.SetWorldExtent(worldExtent);
        _camera.ConfigureWorldExtent(worldExtent, profile.UsesFullSurfaceRenderer);
    }

    private void ApplyDefaultCameraPreset(WorldProfile profile)
    {
        _camera.ApplyPreset(
            profile.UsesSingleBlockGenerator || profile.UsesSolidCubeGenerator
                ? OrbitCameraController.NearPreset
                : OrbitCameraController.MediumPreset,
            immediate: true);
    }

    private void MarkAutosaveDirty()
    {
        if (_sessionPersists) _autosaveDirty = true;
    }

    private void TrySaveCurrentSession(bool captureFirst = true)
    {
        try
        {
            if (captureFirst) CaptureCurrentSession();
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

    private static string ReplayPath(WorldProfile profile)
        => $"user://replays/{profile.Id}_v{profile.WorldVersion}_g{profile.GenerationVersion}.cmbr";

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
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });

        AddChild(new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-52.0f, -34.0f, 0.0f),
            LightColor = new Color(1.0f, 0.98f, 0.94f),
            LightEnergy = 1.05f,
            LightSpecular = 0.0f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 140.0f,
            ShadowBlur = 1.4f,
        });

        AddChild(new DirectionalLight3D
        {
            Name = "FillLight",
            RotationDegrees = new Vector3(24.0f, 146.0f, 0.0f),
            LightColor = new Color(0.72f, 0.82f, 1.0f),
            LightEnergy = 0.45f,
            LightSpecular = 0.0f,
            ShadowEnabled = false,
        });
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
        panel.AddChild(new Label
        {
            Text = "Startup validation failed\n\n" + message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
    }
}
