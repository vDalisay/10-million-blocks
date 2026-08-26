using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Collection;
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
    private WorldBrowserView? _worldBrowser;
    private ReplayRecorder? _replayRecorder;
    private ReplayPlaybackController? _replayPlayback;
    private MainMenuView? _mainMenu;
    private PauseMenuView? _pauseMenu;
    private DemoCompleteView? _demoCompleteView;
    private TutorialOverlay? _tutorialOverlay;
    private PhaseQTelemetry? _phaseQTelemetry;
    private WorldEventController? _worldEvents;
    private GameFlowState _flowState = GameFlowState.MainMenu;
    private string _activeWorldId = string.Empty;
    private string _replayReturnWorldId = string.Empty;
    private string _replayPath = string.Empty;
    private bool _sessionPersists;
    private bool _completionShown;
    private bool _debugPreview;
    private bool _pendingNextWorld;
    private bool _pendingReplayExit;
    private bool _pendingReplayRestore;
    private int _manualBlocksThisWorld;
    private int _automationBlocksThisWorld;
    private long _worldResourcesEarned;
    private bool _autosaveDirty;
    private double _autosaveCooldown;

    private enum GameFlowState
    {
        MainMenu,
        World,
        Browser,
        Replay,
        DemoComplete,
    }

    public override void _Ready()
    {
        _content = ContentDatabase.LoadDefault();
        _assets = new BlockAssetRegistry(_content);
        _worlds = WorldCatalog.LoadDefault(_content);
        _minerCatalog = MinerCatalog.LoadDefault(_content);
        _skillCatalog = SkillTreeCatalog.LoadDefault(_content);
        _patterns = MiningPatternRegistry.CreateDefault();
        _progression = new WorldProgressionService(_worlds);
        _saveService = new SaveService();
        _save = _saveService.LoadOrCreate();
        _saveService.MigrateLegacyTutorialCurrency(_save, _worlds.SteamDemoWorldIds);
        _specialResources = new SpecialResourceInventory(_save);

        BuildPersistentPresentation();
        BuildMainMenu();
        ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        if (!_autosaveDirty || _saveService is null || !_sessionPersists || _flowState != GameFlowState.World)
            return;

        _autosaveCooldown -= Math.Max(0.0, delta);
        if (_autosaveCooldown > 0.0) return;

        CaptureCurrentSession();
        _saveService.Save(_save);
        _autosaveDirty = false;
        _autosaveCooldown = 1.0;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        if (key.Keycode == Key.F1 && _flowState == GameFlowState.World)
        {
            _tutorialOverlay?.RecallLastMessage();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.F9 && OS.IsDebugBuild() && _flowState == GameFlowState.World)
        {
            ShowDebugDiagnostics();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.F11 && OS.IsDebugBuild() && _flowState == GameFlowState.World)
        {
            DumpDebugDiagnostics();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode != Key.Escape) return;
        switch (_flowState)
        {
            case GameFlowState.World:
                TogglePauseMenu();
                break;
            case GameFlowState.Browser:
                HideWorldBrowser();
                break;
            case GameFlowState.Replay:
                ExitReplay();
                break;
            case GameFlowState.DemoComplete:
                ShowWorldBrowserFromDemoComplete();
                break;
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildPersistentPresentation()
    {
        _camera = new OrbitCameraController { Name = "OrbitCamera" };
        AddChild(_camera);

        _clouds = new CloudField { Name = "CloudField" };
        _clouds.Initialize(_camera);
        AddChild(_clouds);

        _completionView = new WorldCompleteView { Name = "WorldCompleteView" };
        _completionView.NextRequested += OnCompletionNextRequested;
        _completionView.BrowseRequested += OnCompletionBrowseRequested;
        AddChild(_completionView);
    }

    private void BuildMainMenu()
    {
        _mainMenu = new MainMenuView { Name = "MainMenu" };
        _mainMenu.ContinueRequested += StartOrContinueGame;
        _mainMenu.NewGameRequested += StartNewGame;
        _mainMenu.WorldBrowserRequested += ShowWorldBrowserFromMainMenu;
        _mainMenu.SettingsRequested += ShowSettingsFromMainMenu;
        _mainMenu.ClearSaveRequested += ClearSaveData;
        AddChild(_mainMenu);
    }

    private void StartOrContinueGame()
    {
        string worldId = ResolveContinueWorldId();
        BuildWorldSession(_worlds.Get(worldId), applyOfflineProgress: true, persistSession: true);
    }

    private void StartNewGame()
    {
        BuildWorldSession(_worlds.Get(_worlds.SteamDemoWorldIds[0]), applyOfflineProgress: false, persistSession: true);
    }

    private void BuildWorldSession(WorldProfile profile, bool applyOfflineProgress, bool persistSession)
    {
        TearDownWorldSession();
        _flowState = GameFlowState.World;
        _sessionPersists = persistSession;
        _completionShown = false;
        _pendingNextWorld = false;
        _debugPreview = false;
        _activeWorldId = profile.Id;
        _manualBlocksThisWorld = 0;
        _automationBlocksThisWorld = 0;
        _worldResourcesEarned = 0;
        _autosaveDirty = false;
        _autosaveCooldown = 1.0;

        WorldSaveData? savedWorld = persistSession ? _save.Worlds.GetValueOrDefault(profile.Id) : null;
        WorldGenerationResult generated = WorldGenerator.Generate(profile, _content);
        _world = new VirtualWorld(profile, generated, _content, savedWorld?.MinedChunks);
        if (savedWorld is not null)
        {
            _world.State.RestoreExhaustedRegions(savedWorld.ExhaustedRegions);
            if (savedWorld.InitialMineableBlockCount <= 0) savedWorld.InitialMineableBlockCount = _world.InitialMineableBlocks;
        }

        _sessionRoot = new Node3D { Name = $"WorldSession_{profile.Id}" };
        AddChild(_sessionRoot);

        _worldView = new WorldView { Name = "WorldView" };
        _worldView.Initialize(_world, _assets, _camera);
        _sessionRoot.AddChild(_worldView);

        _skills = new SkillTreeService(_skillCatalog, _save, persistSession);
        _specialResources.BindSkills(_skills);
        if (persistSession) _skills.Changed += MarkAutosaveDirty;

        _mining = new MiningService(_world, _content, _skills, _specialResources);
        long startingCurrency = persistSession
            ? (profile.UsePersistentMainCurrency
                ? _save.PersistentMainCurrency
                : savedWorld?.Currency ?? 0L)
            : 0L;
        _mining.RestoreCurrency(startingCurrency);
        // BlockMined is attached after ResourceCollectionField so deferred rewards exist before
        // completion/stat observers see the removal.
        _mining.BulkMined += OnBulkMined;
        _mining.CurrencyChanged += _ => MarkAutosaveDirty();

        _camera.ConfigureForWorld(_world);
        _clouds.ConfigureForWorld(_world.Profile);

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

        _placement = new MinerPlacementController { Name = "MinerPlacement" };
        _placement.Initialize(_world, _camera, _worldView, _mining, _skills, _miners, _manualMining);
        _placement.Changed += MarkAutosaveDirty;
        _sessionRoot.AddChild(_placement);

        var hud = new HudView { Name = "Hud" };
        hud.Initialize(_world, _mining, _worldView, _skills, _miners, _manualMining, _placement);
        _sessionRoot.AddChild(hud);

        // Incremental-game feedback remains presentation-only. ResourceCollectionField has already
        // decided whether ordinary manual/live-automation rewards are banked or deferred pickups.
        var incrementalFeedback = new IncrementalFeedbackView { Name = "IncrementalFeedbackView" };
        incrementalFeedback.Initialize(_world, _worldView, _mining, _specialResources, _assets);
        _sessionRoot.AddChild(incrementalFeedback);

        _skillTree = new SkillTreeView { Name = "SkillTree" };
        _skillTree.Initialize(_skills, _mining, _specialResources, _skillCatalog, _manualMining, _placement);
        _skillTree.Closed += OnSkillTreeClosed;
        _skillTree.SkillPurchased += MarkAutosaveDirty;
        _sessionRoot.AddChild(_skillTree);

        _worldBrowser = new WorldBrowserView { Name = "WorldBrowser" };
        _worldBrowser.Initialize(_worlds, _save);
        _worldBrowser.WorldSelected += OnWorldSelected;
        _worldBrowser.ReplaySelected += OnReplaySelected;
        _worldBrowser.Closed += HideWorldBrowser;
        _sessionRoot.AddChild(_worldBrowser);

        _pauseMenu = new PauseMenuView { Name = "PauseMenu" };
        _pauseMenu.ResumeRequested += ResumeFromPause;
        _pauseMenu.SaveAndMenuRequested += SaveAndReturnToMainMenu;
        _sessionRoot.AddChild(_pauseMenu);

        _tutorialOverlay = new TutorialOverlay { Name = "TutorialOverlay" };
        _tutorialOverlay.Initialize(profile.Id, _save, persistSession);
        _sessionRoot.AddChild(_tutorialOverlay);

        _phaseQTelemetry = new PhaseQTelemetry(profile.Id, _save, persistSession);
        _phaseQTelemetry.Attach(_mining, _miners, _skills, _placement);

        _worldEvents = new WorldEventController { Name = "WorldEvents" };
        _worldEvents.Initialize(_world, _mining, _worldView, _skills, _specialResources, _tutorialOverlay, _save, persistSession);
        _worldEvents.PersistentStateChanged += MarkAutosaveDirty;
        _sessionRoot.AddChild(_worldEvents);

        _replayRecorder = persistSession ? new ReplayRecorder(profile, _world) : null;
        if (_replayRecorder is not null) _replayRecorder.Attach(_mining, _skills, _miners, _worldEvents);

        if (applyOfflineProgress && persistSession && savedWorld is not null)
        {
            ApplyOfflineProgress(savedWorld);
        }

        if (persistSession)
        {
            _save.LastWorldId = profile.Id;
            if (!_save.Worlds.TryGetValue(profile.Id, out WorldSaveData? worldSave))
            {
                worldSave = new WorldSaveData();
                _save.Worlds[profile.Id] = worldSave;
            }
            if (worldSave.InitialMineableBlockCount <= 0) worldSave.InitialMineableBlockCount = _world.InitialMineableBlocks;
            _saveService.Save(_save);
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
        _flowState = GameFlowState.Replay;
        _sessionPersists = false;
        _activeWorldId = profile.Id;
        _completionShown = false;

        WorldGenerationResult generated = WorldGenerator.Generate(profile, _content);
        _world = new VirtualWorld(profile, generated, _content);
        _sessionRoot = new Node3D { Name = $"ReplaySession_{profile.Id}" };
        AddChild(_sessionRoot);

        _worldView = new WorldView { Name = "WorldView" };
        _worldView.Initialize(_world, _assets, _camera);
        _sessionRoot.AddChild(_worldView);

        _skills = new SkillTreeService(_skillCatalog, _save, persistPurchases: false);
        _specialResources.BindSkills(_skills);
        _mining = new MiningService(_world, _content, _skills, _specialResources);
        _mining.RestoreCurrency(0L);
        _camera.ConfigureForWorld(_world);
        _clouds.ConfigureForWorld(_world.Profile);

        _manualMining = new ManualMiningController { Name = "ManualMining" };
        _manualMining.Initialize(_world, _camera, _worldView, _mining, _skills);
        _manualMining.InputEnabled = false;
        _sessionRoot.AddChild(_manualMining);

        _miners = new MinerSimulationService { Name = "MinerSimulation" };
        _miners.Initialize(_world, _mining, _worldView, _minerCatalog, _patterns, _skills);
        _sessionRoot.AddChild(_miners);

        var replayHud = new ReplayHudView { Name = "ReplayHud" };
        replayHud.ExitRequested += ExitReplay;
        _sessionRoot.AddChild(replayHud);

        _replayPlayback = new ReplayPlaybackController();
        _replayPlayback.Initialize(replay, _mining, _skills, _miners);
        _replayPlayback.Completed += ExitReplay;
        _sessionRoot.AddChild(_replayPlayback);
    }

    private void TearDownWorldSession()
    {
        if (_phaseQTelemetry is not null) _phaseQTelemetry.Flush();
        _phaseQTelemetry = null;
        if (_skillTree is not null) _skillTree.Closed -= OnSkillTreeClosed;
        if (_worldBrowser is not null)
        {
            _worldBrowser.WorldSelected -= OnWorldSelected;
            _worldBrowser.ReplaySelected -= OnReplaySelected;
            _worldBrowser.Closed -= HideWorldBrowser;
        }
        if (_placement is not null) _placement.Changed -= MarkAutosaveDirty;
        if (_skills is not null) _skills.Changed -= MarkAutosaveDirty;
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
        _worldBrowser = null;
        _pauseMenu = null;
        _tutorialOverlay = null;
        _replayRecorder = null;
        _replayPlayback = null;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (result.Source == MiningSource.Automated) _automationBlocksThisWorld++;
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
        if (result.Source == MiningSource.Automated) _automationBlocksThisWorld += result.Removed;
        else if (result.Source == MiningSource.Manual) _manualBlocksThisWorld += result.Removed;
        MarkAutosaveDirty();
    }

    private void MarkAutosaveDirty()
    {
        if (!_sessionPersists) return;
        _autosaveDirty = true;
        _autosaveCooldown = Math.Min(_autosaveCooldown, 0.25);
    }

    private void ShowCompletion(bool debugPreview)
    {
        if (_world is null || _mining is null || _completionView is null) return;
        _completionShown = true;
        _debugPreview = debugPreview;
        CaptureCurrentSession();
        if (_sessionPersists)
        {
            _progression.MarkWorldCompleted(_save, _world.Profile.Id);
            _saveService.Save(_save);
        }

        bool demoFinale = _world.Profile.Id == _worlds.SteamDemoWorldIds[^1];
        _completionView.ShowCompletion(_world.Profile, _mining.Currency, demoFinale, debugPreview);
        SetWorldInputEnabled(false);
    }

    private void HideCompletion()
    {
        _completionView.HideCompletion();
        _completionShown = false;
        SetWorldInputEnabled(true);
    }

    private void OnCompletionNextRequested()
    {
        if (_world is null) return;
        if (_debugPreview)
        {
            HideCompletion();
            return;
        }

        if (_world.Profile.Id == _worlds.SteamDemoWorldIds[^1])
        {
            ShowDemoComplete();
            return;
        }

        string? next = _progression.GetNextWorldId(_world.Profile.Id);
        if (string.IsNullOrWhiteSpace(next))
        {
            ShowWorldBrowserFromWorld();
            return;
        }

        BuildWorldSession(_worlds.Get(next), applyOfflineProgress: true, persistSession: true);
    }

    private void OnCompletionBrowseRequested()
    {
        ShowWorldBrowserFromWorld();
    }

    private void TogglePauseMenu()
    {
        if (_pauseMenu is null) return;
        if (_pauseMenu.Visible)
        {
            ResumeFromPause();
            return;
        }

        _pauseMenu.ShowPause();
        SetWorldInputEnabled(false);
    }

    private void ResumeFromPause()
    {
        _pauseMenu?.HidePause();
        SetWorldInputEnabled(true);
    }

    private void SaveAndReturnToMainMenu()
    {
        CaptureCurrentSession();
        _saveService.Save(_save);
        _autosaveDirty = false;
        ShowMainMenu();
    }

    private void CaptureCurrentSession()
    {
        if (!_sessionPersists || _world is null || _mining is null || _skills is null || _miners is null) return;

        _save.PersistentMainCurrency = _mining.Currency;
        WorldSaveData? previous = _save.Worlds.GetValueOrDefault(_world.Profile.Id);
        _save.Worlds[_world.Profile.Id] = new WorldSaveData
        {
            Currency = _world.Profile.UsePersistentMainCurrency ? previous?.Currency ?? 0L : _mining.Currency,
            InitialMineableBlockCount = previous?.InitialMineableBlockCount > 0
                ? previous.InitialMineableBlockCount
                : _world.InitialMineableBlocks,
            Completed = previous?.Completed ?? false,
            HoverMiningEnabled = _manualMining?.HoverMiningEnabled ?? previous?.HoverMiningEnabled ?? false,
            LastPlayedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            MinedChunks = _world.State.CreateSnapshot(),
            ExhaustedRegions = _world.State.CreateExhaustedRegionSnapshot(),
            Miners = _miners.CreateSnapshot(),
            PendingPickups = _resourceCollection?.CreateSnapshot() ?? previous?.PendingPickups ?? new(),
        };
    }

    private string ResolveContinueWorldId()
    {
        if (_save.DemoCompleted) return _worlds.SteamDemoWorldIds[^1];
        if (!string.IsNullOrWhiteSpace(_save.LastWorldId) && _worlds.Contains(_save.LastWorldId)) return _save.LastWorldId;
        return _worlds.SteamDemoWorldIds[0];
    }

    private void ClearSaveData()
    {
        _saveService.Delete();
        _save = _saveService.LoadOrCreate();
        _specialResources = new SpecialResourceInventory(_save);
        _mainMenu?.Refresh(_save, _worlds);
    }

    private void ShowMainMenu()
    {
        TearDownWorldSession();
        _flowState = GameFlowState.MainMenu;
        _completionView.HideCompletion();
        _demoCompleteView?.HideScreen();
        _mainMenu?.Refresh(_save, _worlds);
        _mainMenu?.ShowMenu();
        _clouds.Visible = true;
    }

    private void ShowSettingsFromMainMenu()
    {
        SettingsView.ShowStandalone(this, _save, _saveService);
    }

    private void ShowWorldBrowserFromMainMenu()
    {
        string previewWorld = ResolveContinueWorldId();
        BuildWorldSession(_worlds.Get(previewWorld), applyOfflineProgress: false, persistSession: false);
        ShowWorldBrowserInternal(returnState: GameFlowState.MainMenu);
    }

    private void ShowWorldBrowserFromWorld()
    {
        ShowWorldBrowserInternal(returnState: GameFlowState.World);
    }

    private void ShowWorldBrowserInternal(GameFlowState returnState)
    {
        if (_worldBrowser is null) return;
        _flowState = GameFlowState.Browser;
        _worldBrowser.SetReturnState(returnState.ToString());
        _worldBrowser.Refresh(_save, _worlds);
        _worldBrowser.ShowBrowser();
        SetWorldInputEnabled(false);
    }

    private void HideWorldBrowser()
    {
        if (_worldBrowser is null) return;
        GameFlowState returnState = Enum.TryParse(_worldBrowser.ReturnState, out GameFlowState parsed)
            ? parsed
            : GameFlowState.World;
        _worldBrowser.HideBrowser();

        if (returnState == GameFlowState.MainMenu)
        {
            ShowMainMenu();
            return;
        }

        _flowState = GameFlowState.World;
        SetWorldInputEnabled(true);
    }

    private void OnWorldSelected(string worldId, bool replay)
    {
        if (replay)
        {
            ReplayData? data = ReplayStore.LoadLatest(worldId);
            if (data is null) return;
            _replayReturnWorldId = _activeWorldId;
            BuildReplaySession(_worlds.Get(worldId), data);
            return;
        }

        CaptureCurrentSession();
        _saveService.Save(_save);
        BuildWorldSession(_worlds.Get(worldId), applyOfflineProgress: true, persistSession: true);
    }

    private void OnReplaySelected(string worldId)
    {
        ReplayData? data = ReplayStore.LoadLatest(worldId);
        if (data is null) return;
        _replayReturnWorldId = _activeWorldId;
        BuildReplaySession(_worlds.Get(worldId), data);
    }

    private void ExitReplay()
    {
        if (_flowState != GameFlowState.Replay) return;
        string returnWorld = !string.IsNullOrWhiteSpace(_replayReturnWorldId) && _worlds.Contains(_replayReturnWorldId)
            ? _replayReturnWorldId
            : ResolveContinueWorldId();
        _replayReturnWorldId = string.Empty;
        BuildWorldSession(_worlds.Get(returnWorld), applyOfflineProgress: true, persistSession: true);
    }

    private void ShowDemoComplete()
    {
        CaptureCurrentSession();
        _save.DemoCompleted = true;
        _saveService.Save(_save);
        _flowState = GameFlowState.DemoComplete;
        TearDownWorldSession();

        _demoCompleteView ??= new DemoCompleteView { Name = "DemoCompleteView" };
        if (_demoCompleteView.GetParent() is null)
        {
            _demoCompleteView.BrowseRequested += ShowWorldBrowserFromDemoComplete;
            _demoCompleteView.MainMenuRequested += ShowMainMenu;
            AddChild(_demoCompleteView);
        }
        _demoCompleteView.ShowScreen();
    }

    private void ShowWorldBrowserFromDemoComplete()
    {
        _demoCompleteView?.HideScreen();
        BuildWorldSession(_worlds.Get(_worlds.SteamDemoWorldIds[^1]), applyOfflineProgress: false, persistSession: false);
        ShowWorldBrowserInternal(GameFlowState.DemoComplete);
    }

    private void SetWorldInputEnabled(bool enabled)
    {
        if (_manualMining is not null) _manualMining.InputEnabled = enabled;
        if (_placement is not null) _placement.InputEnabled = enabled;
        if (_skillTree is not null) _skillTree.InputEnabled = enabled;
    }

    private void ShowDebugDiagnostics()
    {
        if (_world is null || _worldView is null || _miners is null) return;
        DebugDiagnostics.ShowOverlay(this, _world, _worldView, _miners);
    }

    private void DumpDebugDiagnostics()
    {
        if (_world is null || _worldView is null || _miners is null) return;
        DebugDiagnostics.Dump(_world, _worldView, _miners);
    }

    private void ApplyOfflineProgress(WorldSaveData savedWorld)
    {
        if (_miners is null || _mining is null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long elapsed = Math.Max(0L, now - savedWorld.LastPlayedUnixSeconds);
        if (elapsed <= 0) return;
        _miners.ApplyOfflineProgress(TimeSpan.FromSeconds(elapsed));
    }

    private void OnSkillTreeClosed()
    {
        SetWorldInputEnabled(true);
    }
}
