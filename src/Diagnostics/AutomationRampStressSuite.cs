using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Diagnostics;

/// <summary>
/// Fully automated late-game stress scenario for the 100^3 debug world. F11 starts/cancels the suite.
/// It ramps a deterministic shovel/drill fleet every ten seconds, drives fully-upgraded manual mining
/// through the real center-screen click path, orbits the camera, and records a 70-second performance
/// report. The final fleet is held for ten seconds (60-70s) so the report contains a stable worst-case
/// window instead of measuring only the instant that the last machines spawn.
/// </summary>
public partial class AutomationRampStressSuite : Node
{
    private const double SuiteDurationSeconds = 70.0;
    private const double FullStressStartSeconds = 60.0;
    private const double AutoMineIntervalSeconds = 0.20;
    private const double DiagnosticSampleIntervalSeconds = 0.10;
    private const double TimelineSampleIntervalSeconds = 1.0;

    private static readonly double[] StageTimes = [10, 20, 30, 40, 50, 60];
    private static readonly int[] StageShovels = [20, 10, 10, 10, 10, 10];
    private static readonly int[] StageDrills = [0, 10, 10, 10, 10, 10];

    private WorldView? _view;
    private MinerSimulationService? _miners;
    private ManualMiningController? _manual;
    private OrbitCameraController? _camera;
    private IncrementalFeedbackView? _feedback;
    private StressBenchmarkController? _standardBenchmark;
    private Rid _viewportRid;

    private bool _running;
    private ulong _startedUsec;
    private ulong _lastFrameUsec;
    private double _elapsed;
    private double _autoMineAccumulator;
    private double _nextDiagnosticAt;
    private double _nextTimelineAt;
    private int _nextStage;
    private uint _spawnRandomState;

    private readonly List<double> _frameTimesMs = new(8192);
    private readonly List<double> _fullStressFrameTimesMs = new(2048);
    private readonly List<string> _timeline = new(80);
    private readonly List<string> _stageEvents = new(8);
    private double _frameTimeTotalMs;
    private double _maximumFrameMs;
    private long _framesOver16;
    private long _framesOver25;
    private long _framesOver33;
    private long _framesOver50;
    private long _framesOver100;
    private long _autoMineClicks;
    private long _autoMineActions;
    private int _requestedShovels;
    private int _placedShovels;
    private int _requestedDrills;
    private int _placedDrills;

    private long _minedAtStart;
    private long _miningTotalAtStart;
    private long _chunkBuildsAtStart;
    private double _chunkBuildMsAtStart;
    private long _sparseBuildsAtStart;
    private double _sparseBuildMsAtStart;
    private long _streamLoadsAtStart;
    private long _streamUnloadsAtStart;
    private long _automationQueuedAtStart;
    private long _automationSuppressedAtStart;
    private long _sampleHitsAtStart;
    private long _sampleMissesAtStart;
    private long _popDropsAtStart;
    private long _debrisDropsAtStart;
    private long _feedbackSpawnedAtStart;
    private long _feedbackAggregatedAtStart;
    private long _feedbackDroppedAtStart;
    private readonly int[] _gcAtStart = new int[3];
    private double _managedMemoryStartMb;
    private double _managedMemoryMaxMb;
    private long _workingSetStartBytes;
    private long _workingSetMaxBytes;
    private ulong _videoMemoryStartBytes;
    private ulong _videoMemoryMaxBytes;

    private long _diagnosticSamples;
    private double _renderCpuTotalMs;
    private double _renderCpuMaxMs;
    private long _renderCpuSamples;
    private double _renderGpuTotalMs;
    private double _renderGpuMaxMs;
    private long _renderGpuSamples;
    private double _renderSetupTotalMs;
    private double _renderSetupMaxMs;
    private double _drawCallsTotal;
    private ulong _drawCallsMax;
    private double _renderObjectsTotal;
    private ulong _renderObjectsMax;
    private double _primitivesTotal;
    private ulong _primitivesMax;
    private double _presentedChunksTotal;
    private double _backfaceCulledTotal;
    private double _frustumCulledTotal;
    private double _lodTreeHiddenTotal;
    private double _lodShadowsDisabledTotal;
    private int _maxDirtyChunks;
    private int _maxPendingLoads;
    private int _maxSparsePending;
    private long _maxSparseFrontier;
    private int _maxDeferredAutomationChunks;
    private int _maxPresentedMiners;
    private int _maxAttentionMiners;
    private double _maxAutomationBlocksPerSecond;
    private double _lastRenderCpuMs;
    private double _lastRenderGpuMs;
    private ulong _lastDrawCalls;

    private long _fullStressMinedAtStart;
    private long _fullStressAutomationQueuedAtStart;
    private long _fullStressAutomationSuppressedAtStart;
    private long _fullStressChunkBuildsAtStart;
    private double _fullStressChunkBuildMsAtStart;
    private long _fullStressSparseBuildsAtStart;
    private double _fullStressSparseBuildMsAtStart;
    private double _fullStressRenderCpuTotalMs;
    private long _fullStressRenderCpuSamples;
    private double _fullStressRenderGpuTotalMs;
    private long _fullStressRenderGpuSamples;
    private double _fullStressDrawCallsTotal;
    private long _fullStressDiagnosticSamples;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!OS.IsDebugBuild()
            || @event is not InputEventKey key
            || !key.Pressed
            || key.Echo
            || key.Keycode != Key.F11)
        {
            return;
        }

        if (_running)
        {
            Finish("cancelled");
        }
        else
        {
            TryStart();
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_running || _view is null || _miners is null || _manual is null || _camera is null)
        {
            return;
        }

        ulong nowUsec = Time.GetTicksUsec();
        _elapsed = (nowUsec - _startedUsec) / 1_000_000.0;
        double wallDelta = Math.Max(0.0, (nowUsec - _lastFrameUsec) / 1_000_000.0);
        _lastFrameUsec = nowUsec;
        CaptureFrameTiming(wallDelta);

        // Keep changing the visible working set while center-screen mining follows the camera. This
        // stresses culling, deferred automation presentation and sparse-frontier promotion together.
        float orbitDelta = (float)Math.Min(wallDelta, 0.25);
        _camera.AddOrbitDegrees(8.0f * orbitDelta, MathF.Sin((float)_elapsed * 0.55f) * 0.055f);

        _autoMineAccumulator += wallDelta;
        int manualClicksThisFrame = 0;
        while (_autoMineAccumulator >= AutoMineIntervalSeconds && manualClicksThisFrame < 2)
        {
            _autoMineAccumulator -= AutoMineIntervalSeconds;
            _autoMineClicks++;
            _autoMineActions += _manual.DiagnosticMineScreenCenter();
            manualClicksThisFrame++;
        }

        while (_nextStage < StageTimes.Length && _elapsed >= StageTimes[_nextStage])
        {
            ApplyStage(_nextStage++);
        }

        if (_elapsed >= FullStressStartSeconds && _fullStressMinedAtStart < 0)
        {
            CaptureFullStressBaseline();
        }

        if (_elapsed >= _nextDiagnosticAt)
        {
            _nextDiagnosticAt += DiagnosticSampleIntervalSeconds;
            CaptureDiagnosticSample();
        }

        if (_elapsed >= _nextTimelineAt)
        {
            _nextTimelineAt += TimelineSampleIntervalSeconds;
            CaptureTimeline();
        }

        if (_elapsed >= SuiteDurationSeconds)
        {
            Finish("complete");
        }
    }

    public override void _ExitTree()
    {
        if (_running) Finish("aborted");
    }

    private void TryStart()
    {
        Node? session = GetTree().Root.FindChild("WorldSession_stress_1000", recursive: true, owned: false);
        _view = session?.GetNodeOrNull<WorldView>("WorldView");
        _miners = session?.GetNodeOrNull<MinerSimulationService>("MinerSimulation");
        _manual = session?.GetNodeOrNull<ManualMiningController>("ManualMining");
        _feedback = session?.GetNodeOrNull<IncrementalFeedbackView>("IncrementalFeedbackView");
        _standardBenchmark = session?.GetNodeOrNull<StressBenchmarkController>("StressBenchmark");
        _camera = GetTree().Root.FindChild("OrbitCamera", recursive: true, owned: false) as OrbitCameraController;

        if (_view is null || _miners is null || _manual is null || _camera is null || _view.DiagnosticWorldId != "stress_1000")
        {
            GD.Print("Automation ramp stress suite requires stress_1000. Press F8 first, wait for the world to load, then press F11.");
            ClearReferences();
            return;
        }
        if (_standardBenchmark?.IsRunning == true)
        {
            GD.Print("F7 benchmark is already running. Cancel/finish F7 before starting the independent F11 automation ramp suite.");
            ClearReferences();
            return;
        }

        // Produce a reproducible baseline regardless of what the player can currently afford. stress_1000
        // is non-persistent, so neither clearing miners nor maxing skills can alter progression saves.
        _miners.ClearMiners();
        _miners.ResetDiagnosticSpawnReservations();
        _miners.ApplyDiagnosticMaximumSkills();
        _manual.SetHoverMiningEnabled(false);
        _manual.InputEnabled = true;
        _manual.PlacementMode = false;

        _running = true;
        _startedUsec = Time.GetTicksUsec();
        _lastFrameUsec = _startedUsec;
        _elapsed = 0.0;
        _autoMineAccumulator = 0.0;
        _nextDiagnosticAt = 0.0;
        _nextTimelineAt = 0.0;
        _nextStage = 0;
        _spawnRandomState = 0x6d2b79f5u;
        _fullStressMinedAtStart = -1L;

        ResetRunMetrics();
        CaptureRunBaselines();

        _viewportRid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);

        GD.Print(
            "F11 automation ramp stress started (70s): max skills forced for this non-persistent stress session; " +
            "center-screen 10x10/manual max-footprint mining at 5 clicks/s; camera orbit; " +
            "t=10 +20 shovels, then t=20/30/40/50/60 +10 shovels +10 drills. Final 120-unit fleet runs for 10s. " +
            "F11 cancels and still writes user://automation_stress_benchmark_latest.txt.");
    }

    private void ApplyStage(int stageIndex)
    {
        if (_miners is null) return;

        int requestedShovels = StageShovels[stageIndex];
        int requestedDrills = StageDrills[stageIndex];
        int placedShovels = _miners.SpawnDiagnosticSurfaceMiners("shovel_miner", requestedShovels, ref _spawnRandomState);
        int placedDrills = _miners.SpawnDiagnosticSurfaceMiners("line_miner", requestedDrills, ref _spawnRandomState);
        _requestedShovels += requestedShovels;
        _placedShovels += placedShovels;
        _requestedDrills += requestedDrills;
        _placedDrills += placedDrills;

        string stage = $"t={StageTimes[stageIndex]:0}s requested_shovels={requestedShovels} placed_shovels={placedShovels} requested_drills={requestedDrills} placed_drills={placedDrills} total_units={_miners.Miners.Count}";
        _stageEvents.Add(stage);
        GD.Print("Automation stress stage: " + stage);
    }

    private void CaptureFullStressBaseline()
    {
        if (_view is null) return;
        _fullStressMinedAtStart = _view.DiagnosticMinedVoxelCount;
        _fullStressAutomationQueuedAtStart = _view.AutomationPresentationUpdatesQueued;
        _fullStressAutomationSuppressedAtStart = _view.AutomationPresentationUpdatesSuppressed;
        _fullStressChunkBuildsAtStart = _view.TotalChunkBuilds;
        _fullStressChunkBuildMsAtStart = _view.TotalChunkBuildMilliseconds;
        _fullStressSparseBuildsAtStart = _view.SparseExposureOverlayBuilds;
        _fullStressSparseBuildMsAtStart = _view.TotalSparseExposureOverlayBuildMilliseconds;
        _fullStressRenderCpuTotalMs = 0.0;
        _fullStressRenderCpuSamples = 0L;
        _fullStressRenderGpuTotalMs = 0.0;
        _fullStressRenderGpuSamples = 0L;
        _fullStressDrawCallsTotal = 0.0;
        _fullStressDiagnosticSamples = 0L;
    }

    private void ResetRunMetrics()
    {
        _frameTimesMs.Clear();
        _fullStressFrameTimesMs.Clear();
        _timeline.Clear();
        _stageEvents.Clear();
        _frameTimeTotalMs = 0.0;
        _maximumFrameMs = 0.0;
        _framesOver16 = 0;
        _framesOver25 = 0;
        _framesOver33 = 0;
        _framesOver50 = 0;
        _framesOver100 = 0;
        _autoMineClicks = 0;
        _autoMineActions = 0;
        _requestedShovels = 0;
        _placedShovels = 0;
        _requestedDrills = 0;
        _placedDrills = 0;
        _diagnosticSamples = 0;
        _renderCpuTotalMs = 0.0;
        _renderCpuMaxMs = 0.0;
        _renderCpuSamples = 0;
        _renderGpuTotalMs = 0.0;
        _renderGpuMaxMs = 0.0;
        _renderGpuSamples = 0;
        _renderSetupTotalMs = 0.0;
        _renderSetupMaxMs = 0.0;
        _drawCallsTotal = 0.0;
        _drawCallsMax = 0;
        _renderObjectsTotal = 0.0;
        _renderObjectsMax = 0;
        _primitivesTotal = 0.0;
        _primitivesMax = 0;
        _presentedChunksTotal = 0.0;
        _backfaceCulledTotal = 0.0;
        _frustumCulledTotal = 0.0;
        _lodTreeHiddenTotal = 0.0;
        _lodShadowsDisabledTotal = 0.0;
        _maxDirtyChunks = 0;
        _maxPendingLoads = 0;
        _maxSparsePending = 0;
        _maxSparseFrontier = 0;
        _maxDeferredAutomationChunks = 0;
        _maxPresentedMiners = 0;
        _maxAttentionMiners = 0;
        _maxAutomationBlocksPerSecond = 0.0;
        _lastRenderCpuMs = 0.0;
        _lastRenderGpuMs = 0.0;
        _lastDrawCalls = 0;
    }

    private void CaptureRunBaselines()
    {
        if (_view is null || _miners is null) return;
        _minedAtStart = _view.DiagnosticMinedVoxelCount;
        _miningTotalAtStart = _miners.DiagnosticTotalMined;
        _chunkBuildsAtStart = _view.TotalChunkBuilds;
        _chunkBuildMsAtStart = _view.TotalChunkBuildMilliseconds;
        _sparseBuildsAtStart = _view.SparseExposureOverlayBuilds;
        _sparseBuildMsAtStart = _view.TotalSparseExposureOverlayBuildMilliseconds;
        _streamLoadsAtStart = _view.StreamedChunkLoads;
        _streamUnloadsAtStart = _view.StreamedChunkUnloads;
        _automationQueuedAtStart = _view.AutomationPresentationUpdatesQueued;
        _automationSuppressedAtStart = _view.AutomationPresentationUpdatesSuppressed;
        _sampleHitsAtStart = _view.DiagnosticGeneratedSampleCacheHits;
        _sampleMissesAtStart = _view.DiagnosticGeneratedSampleCacheMisses;
        _popDropsAtStart = _view.DroppedMinePopCount;
        _debrisDropsAtStart = _view.DroppedDebrisBurstCount;
        _feedbackSpawnedAtStart = _feedback?.SpawnedFeedbackCount ?? 0L;
        _feedbackAggregatedAtStart = _feedback?.AggregatedFeedbackCount ?? 0L;
        _feedbackDroppedAtStart = _feedback?.DroppedFeedbackCount ?? 0L;
        _managedMemoryStartMb = ManagedMemoryMb();
        _managedMemoryMaxMb = _managedMemoryStartMb;
        _workingSetStartBytes = System.Environment.WorkingSet;
        _workingSetMaxBytes = _workingSetStartBytes;
        for (int generation = 0; generation < _gcAtStart.Length; generation++)
        {
            _gcAtStart[generation] = GC.CollectionCount(generation);
        }
        _videoMemoryStartBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        _videoMemoryMaxBytes = _videoMemoryStartBytes;
    }

    private void CaptureFrameTiming(double wallDelta)
    {
        if (wallDelta <= 0.0 || wallDelta > 5.0) return;
        double frameMs = wallDelta * 1000.0;
        _frameTimesMs.Add(frameMs);
        _frameTimeTotalMs += frameMs;
        _maximumFrameMs = Math.Max(_maximumFrameMs, frameMs);
        if (frameMs > 16.67) _framesOver16++;
        if (frameMs > 25.0) _framesOver25++;
        if (frameMs > 33.33) _framesOver33++;
        if (frameMs > 50.0) _framesOver50++;
        if (frameMs > 100.0) _framesOver100++;
        if (_elapsed >= FullStressStartSeconds)
        {
            _fullStressFrameTimesMs.Add(frameMs);
        }
    }

    private void CaptureDiagnosticSample()
    {
        if (_view is null || _miners is null) return;
        _diagnosticSamples++;
        _managedMemoryMaxMb = Math.Max(_managedMemoryMaxMb, ManagedMemoryMb());
        _workingSetMaxBytes = Math.Max(_workingSetMaxBytes, System.Environment.WorkingSet);

        double renderCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewportRid);
        double renderGpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewportRid);
        double setupMs = Math.Max(0.0, RenderingServer.GetFrameSetupTimeCpu());
        _lastRenderCpuMs = renderCpuMs;
        _lastRenderGpuMs = renderGpuMs;
        if (renderCpuMs > 0.0)
        {
            _renderCpuTotalMs += renderCpuMs;
            _renderCpuMaxMs = Math.Max(_renderCpuMaxMs, renderCpuMs);
            _renderCpuSamples++;
        }
        if (renderGpuMs > 0.0)
        {
            _renderGpuTotalMs += renderGpuMs;
            _renderGpuMaxMs = Math.Max(_renderGpuMaxMs, renderGpuMs);
            _renderGpuSamples++;
        }
        _renderSetupTotalMs += setupMs;
        _renderSetupMaxMs = Math.Max(_renderSetupMaxMs, setupMs);

        ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        ulong primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        ulong videoMemory = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        _lastDrawCalls = draws;
        _drawCallsTotal += draws;
        _drawCallsMax = Math.Max(_drawCallsMax, draws);
        _renderObjectsTotal += objects;
        _renderObjectsMax = Math.Max(_renderObjectsMax, objects);
        _primitivesTotal += primitives;
        _primitivesMax = Math.Max(_primitivesMax, primitives);
        _videoMemoryMaxBytes = Math.Max(_videoMemoryMaxBytes, videoMemory);

        _presentedChunksTotal += _view.PresentedChunkCount;
        _backfaceCulledTotal += _view.BackfaceCulledChunkCount;
        _frustumCulledTotal += _view.FrustumCulledChunkCount;
        _lodTreeHiddenTotal += _view.LodHiddenTreeBatchCount;
        _lodShadowsDisabledTotal += _view.LodShadowDisabledBatchCount;
        _maxDirtyChunks = Math.Max(_maxDirtyChunks, _view.PendingChunkRebuilds);
        _maxPendingLoads = Math.Max(_maxPendingLoads, _view.PendingChunkLoads);
        _maxSparsePending = Math.Max(_maxSparsePending, _view.PendingSparseExposureOverlays);
        _maxSparseFrontier = Math.Max(_maxSparseFrontier, _view.SparseExposureFrontierCandidateCount);
        _maxDeferredAutomationChunks = Math.Max(_maxDeferredAutomationChunks, _view.DeferredAutomationChunkCount);
        _maxPresentedMiners = Math.Max(_maxPresentedMiners, _miners.PresentedMinerCount);
        _maxAttentionMiners = Math.Max(_maxAttentionMiners, _miners.AttentionMinerCount);
        _maxAutomationBlocksPerSecond = Math.Max(_maxAutomationBlocksPerSecond, _miners.BlocksPerSecond);

        if (_elapsed >= FullStressStartSeconds)
        {
            _fullStressDiagnosticSamples++;
            if (renderCpuMs > 0.0)
            {
                _fullStressRenderCpuTotalMs += renderCpuMs;
                _fullStressRenderCpuSamples++;
            }
            if (renderGpuMs > 0.0)
            {
                _fullStressRenderGpuTotalMs += renderGpuMs;
                _fullStressRenderGpuSamples++;
            }
            _fullStressDrawCallsTotal += draws;
        }
    }

    private void CaptureTimeline()
    {
        if (_view is null || _miners is null) return;
        double lastFrame = _frameTimesMs.Count == 0 ? 0.0 : _frameTimesMs[^1];
        _timeline.Add(
            $"{_elapsed:0.0}," +
            $"{Engine.GetFramesPerSecond():0.0}," +
            $"{lastFrame:0.00}," +
            $"{_lastRenderCpuMs:0.00}," +
            $"{_lastRenderGpuMs:0.00}," +
            $"{_lastDrawCalls}," +
            $"{_miners.Miners.Count}," +
            $"{_miners.PresentedMinerCount}," +
            $"{_miners.BlocksPerSecond:0.00}," +
            $"{_view.DiagnosticMinedVoxelCount}," +
            $"{_view.PresentedChunkCount}," +
            $"{_view.BackfaceCulledChunkCount}," +
            $"{_view.FrustumCulledChunkCount}," +
            $"{_view.PendingChunkRebuilds}," +
            $"{_view.PendingSparseExposureOverlays}," +
            $"{_view.SparseExposureFrontierCandidateCount}," +
            $"{_view.DeferredAutomationChunkCount}," +
            $"{ManagedMemoryMb():0.0}");
    }

    private void Finish(string reason)
    {
        if (!_running) return;
        _running = false;

        if (_view is not null && _miners is not null)
        {
            CaptureDiagnosticSample();
            CaptureTimeline();
        }
        if (_viewportRid.IsValid)
        {
            RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, false);
        }

        string report = BuildReport(reason);
        GD.Print(report);
        using Godot.FileAccess file = Godot.FileAccess.Open(
            "user://automation_stress_benchmark_latest.txt",
            Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(report);
        GD.Print("Automation stress report written to user://automation_stress_benchmark_latest.txt");

        ClearReferences();
    }

    private string BuildReport(string reason)
    {
        if (_view is null || _miners is null)
        {
            return $"Automation ramp stress {reason}\nreport_version=1\nerror=session references unavailable";
        }

        long frameCount = _frameTimesMs.Count;
        double avgFrameMs = frameCount == 0 ? 0.0 : _frameTimeTotalMs / frameCount;
        double avgFps = avgFrameMs <= 0.0 ? 0.0 : 1000.0 / avgFrameMs;
        double p50 = Percentile(_frameTimesMs, 0.50);
        double p95 = Percentile(_frameTimesMs, 0.95);
        double p99 = Percentile(_frameTimesMs, 0.99);
        double fullP50 = Percentile(_fullStressFrameTimesMs, 0.50);
        double fullP95 = Percentile(_fullStressFrameTimesMs, 0.95);
        double fullP99 = Percentile(_fullStressFrameTimesMs, 0.99);
        double fullAvgMs = Average(_fullStressFrameTimesMs);

        long chunkBuilds = Math.Max(0L, _view.TotalChunkBuilds - _chunkBuildsAtStart);
        double chunkMs = Math.Max(0.0, _view.TotalChunkBuildMilliseconds - _chunkBuildMsAtStart);
        long sparseBuilds = Math.Max(0L, _view.SparseExposureOverlayBuilds - _sparseBuildsAtStart);
        double sparseMs = Math.Max(0.0, _view.TotalSparseExposureOverlayBuildMilliseconds - _sparseBuildMsAtStart);
        long hitDelta = Math.Max(0L, _view.DiagnosticGeneratedSampleCacheHits - _sampleHitsAtStart);
        long missDelta = Math.Max(0L, _view.DiagnosticGeneratedSampleCacheMisses - _sampleMissesAtStart);
        long sampleTotal = hitDelta + missDelta;
        double sampleHitRate = sampleTotal == 0 ? 0.0 : hitDelta * 100.0 / sampleTotal;
        long samples = Math.Max(1L, _diagnosticSamples);
        double managedEnd = ManagedMemoryMb();
        long workingSetEnd = System.Environment.WorkingSet;
        ulong videoMemoryEnd = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);

        long fullStressChunkBuilds = _fullStressMinedAtStart < 0 ? 0 : Math.Max(0L, _view.TotalChunkBuilds - _fullStressChunkBuildsAtStart);
        double fullStressChunkMs = _fullStressMinedAtStart < 0 ? 0.0 : Math.Max(0.0, _view.TotalChunkBuildMilliseconds - _fullStressChunkBuildMsAtStart);
        long fullStressSparseBuilds = _fullStressMinedAtStart < 0 ? 0 : Math.Max(0L, _view.SparseExposureOverlayBuilds - _fullStressSparseBuildsAtStart);
        double fullStressSparseMs = _fullStressMinedAtStart < 0 ? 0.0 : Math.Max(0.0, _view.TotalSparseExposureOverlayBuildMilliseconds - _fullStressSparseBuildMsAtStart);
        long fullStressMined = _fullStressMinedAtStart < 0 ? 0 : Math.Max(0L, _view.DiagnosticMinedVoxelCount - _fullStressMinedAtStart);

        var report = new StringBuilder(20_000);
        report.AppendLine($"Automation ramp stress {reason}");
        report.AppendLine("report_version=1");
        report.AppendLine($"timestamp_local={DateTimeOffset.Now:O}");
        report.AppendLine($"world={_view.DiagnosticWorldId}");
        report.AppendLine($"duration_s={_elapsed:0.00}");
        report.AppendLine("schedule=t10 +20 shovel; t20/t30/t40/t50/t60 +10 shovel +10 drill; hold final fleet to t70");
        report.AppendLine("skills=diagnostic_max_all_authored_skills");
        report.AppendLine($"auto_manual_interval_s={AutoMineIntervalSeconds:0.00}");
        report.AppendLine($"auto_manual_clicks={_autoMineClicks}");
        report.AppendLine($"auto_manual_actions={_autoMineActions}");
        report.AppendLine($"requested_shovels={_requestedShovels}");
        report.AppendLine($"placed_shovels={_placedShovels}");
        report.AppendLine($"requested_drills={_requestedDrills}");
        report.AppendLine($"placed_drills={_placedDrills}");
        report.AppendLine($"final_automation_units={_miners.Miners.Count}");
        report.AppendLine($"automation_work_unit_cap_per_frame={_miners.MaxMiningOperationsPerFrame}");
        report.AppendLine($"automation_nominal_blocks_per_s_final={_miners.BlocksPerSecond:0.00}");
        report.AppendLine();

        report.AppendLine("[frame_timing_all_70s]");
        report.AppendLine($"frame_count={frameCount}");
        report.AppendLine($"average_fps_wall={avgFps:0.0}");
        report.AppendLine($"average_frame_ms={avgFrameMs:0.000}");
        report.AppendLine($"frame_p50_ms={p50:0.000}");
        report.AppendLine($"frame_p95_ms={p95:0.000}");
        report.AppendLine($"frame_p99_ms={p99:0.000}");
        report.AppendLine($"frame_max_ms={_maximumFrameMs:0.000}");
        report.AppendLine($"frames_over_16_67ms={_framesOver16} ({Percent(_framesOver16, frameCount):0.00}%)");
        report.AppendLine($"frames_over_25ms={_framesOver25} ({Percent(_framesOver25, frameCount):0.00}%)");
        report.AppendLine($"frames_over_33_33ms={_framesOver33} ({Percent(_framesOver33, frameCount):0.00}%)");
        report.AppendLine($"frames_over_50ms={_framesOver50} ({Percent(_framesOver50, frameCount):0.00}%)");
        report.AppendLine($"frames_over_100ms={_framesOver100} ({Percent(_framesOver100, frameCount):0.00}%)");
        report.AppendLine();

        report.AppendLine("[full_stress_window_60_to_70s]");
        report.AppendLine($"frame_count={_fullStressFrameTimesMs.Count}");
        report.AppendLine($"average_frame_ms={fullAvgMs:0.000}");
        report.AppendLine($"average_fps_wall={(fullAvgMs <= 0 ? 0.0 : 1000.0 / fullAvgMs):0.0}");
        report.AppendLine($"frame_p50_ms={fullP50:0.000}");
        report.AppendLine($"frame_p95_ms={fullP95:0.000}");
        report.AppendLine($"frame_p99_ms={fullP99:0.000}");
        report.AppendLine($"blocks_mined={fullStressMined}");
        report.AppendLine($"chunk_builds={fullStressChunkBuilds}");
        report.AppendLine($"chunk_build_total_ms={fullStressChunkMs:0.000}");
        report.AppendLine($"sparse_builds={fullStressSparseBuilds}");
        report.AppendLine($"sparse_build_total_ms={fullStressSparseMs:0.000}");
        report.AppendLine($"render_cpu_avg_ms={Average(_fullStressRenderCpuTotalMs, _fullStressRenderCpuSamples):0.000}");
        report.AppendLine($"render_gpu_avg_ms={Average(_fullStressRenderGpuTotalMs, _fullStressRenderGpuSamples):0.000}");
        report.AppendLine($"draw_calls_avg={Average(_fullStressDrawCallsTotal, _fullStressDiagnosticSamples):0.0}");
        report.AppendLine($"automation_presentation_queued={(_fullStressMinedAtStart < 0 ? 0 : _view.AutomationPresentationUpdatesQueued - _fullStressAutomationQueuedAtStart)}");
        report.AppendLine($"automation_presentation_suppressed={(_fullStressMinedAtStart < 0 ? 0 : _view.AutomationPresentationUpdatesSuppressed - _fullStressAutomationSuppressedAtStart)}");
        report.AppendLine();

        report.AppendLine("[rendering]");
        report.AppendLine($"render_cpu_avg_ms={Average(_renderCpuTotalMs, _renderCpuSamples):0.000}");
        report.AppendLine($"render_cpu_max_ms={_renderCpuMaxMs:0.000}");
        report.AppendLine($"render_gpu_avg_ms={Average(_renderGpuTotalMs, _renderGpuSamples):0.000}");
        report.AppendLine($"render_gpu_max_ms={_renderGpuMaxMs:0.000}");
        report.AppendLine($"render_setup_cpu_avg_ms={Average(_renderSetupTotalMs, samples):0.000}");
        report.AppendLine($"render_setup_cpu_max_ms={_renderSetupMaxMs:0.000}");
        report.AppendLine($"draw_calls_avg={Average(_drawCallsTotal, samples):0.0}");
        report.AppendLine($"draw_calls_max={_drawCallsMax}");
        report.AppendLine($"objects_avg={Average(_renderObjectsTotal, samples):0.0}");
        report.AppendLine($"objects_max={_renderObjectsMax}");
        report.AppendLine($"primitives_avg={Average(_primitivesTotal, samples):0.0}");
        report.AppendLine($"primitives_max={_primitivesMax}");
        report.AppendLine($"presented_chunks_avg={Average(_presentedChunksTotal, samples):0.0}");
        report.AppendLine($"backface_culled_chunks_avg={Average(_backfaceCulledTotal, samples):0.0}");
        report.AppendLine($"frustum_culled_chunks_avg={Average(_frustumCulledTotal, samples):0.0}");
        report.AppendLine($"lod_tree_batches_hidden_avg={Average(_lodTreeHiddenTotal, samples):0.0}");
        report.AppendLine($"lod_shadow_batches_disabled_avg={Average(_lodShadowsDisabledTotal, samples):0.0}");
        report.AppendLine();

        report.AppendLine("[world_and_mining]");
        report.AppendLine($"blocks_mined_during_suite={Math.Max(0L, _view.DiagnosticMinedVoxelCount - _minedAtStart)}");
        report.AppendLine($"mining_service_operations_delta={Math.Max(0L, _miners.DiagnosticTotalMined - _miningTotalAtStart)}");
        report.AppendLine($"remaining_blocks_end={_view.DiagnosticRemainingMineableBlocks}");
        report.AppendLine($"modified_chunks_end={_view.DiagnosticModifiedChunkCount}");
        report.AppendLine($"sparse_voxel_overrides_end={_view.DiagnosticSparseVoxelOverrideCount}");
        report.AppendLine($"chunk_builds={chunkBuilds}");
        report.AppendLine($"chunk_build_total_ms={chunkMs:0.000}");
        report.AppendLine($"chunk_build_avg_ms={(chunkBuilds == 0 ? 0.0 : chunkMs / chunkBuilds):0.000}");
        report.AppendLine($"sparse_overlay_builds={sparseBuilds}");
        report.AppendLine($"sparse_overlay_total_ms={sparseMs:0.000}");
        report.AppendLine($"sparse_overlay_avg_ms={(sparseBuilds == 0 ? 0.0 : sparseMs / sparseBuilds):0.000}");
        report.AppendLine($"dirty_chunks_max={_maxDirtyChunks}");
        report.AppendLine($"pending_chunk_loads_max={_maxPendingLoads}");
        report.AppendLine($"sparse_pending_max={_maxSparsePending}");
        report.AppendLine($"sparse_frontier_max={_maxSparseFrontier}");
        report.AppendLine($"stream_loads_delta={Math.Max(0L, _view.StreamedChunkLoads - _streamLoadsAtStart)}");
        report.AppendLine($"stream_unloads_delta={Math.Max(0L, _view.StreamedChunkUnloads - _streamUnloadsAtStart)}");
        report.AppendLine($"sample_cache_hits_delta={hitDelta}");
        report.AppendLine($"sample_cache_misses_delta={missDelta}");
        report.AppendLine($"sample_cache_hit_rate_pct={sampleHitRate:0.00}");
        report.AppendLine();

        report.AppendLine("[automation_and_feedback]");
        report.AppendLine($"automation_blocks_per_s_max={_maxAutomationBlocksPerSecond:0.00}");
        report.AppendLine($"presented_miners_max={_maxPresentedMiners}");
        report.AppendLine($"attention_miners_max={_maxAttentionMiners}");
        report.AppendLine($"deferred_automation_chunks_max={_maxDeferredAutomationChunks}");
        report.AppendLine($"automation_presentation_queued_delta={Math.Max(0L, _view.AutomationPresentationUpdatesQueued - _automationQueuedAtStart)}");
        report.AppendLine($"automation_presentation_suppressed_delta={Math.Max(0L, _view.AutomationPresentationUpdatesSuppressed - _automationSuppressedAtStart)}");
        report.AppendLine($"mine_pop_dropped_delta={Math.Max(0L, _view.DroppedMinePopCount - _popDropsAtStart)}");
        report.AppendLine($"debris_dropped_delta={Math.Max(0L, _view.DroppedDebrisBurstCount - _debrisDropsAtStart)}");
        report.AppendLine($"feedback_spawned_delta={Math.Max(0L, (_feedback?.SpawnedFeedbackCount ?? 0L) - _feedbackSpawnedAtStart)}");
        report.AppendLine($"feedback_aggregated_delta={Math.Max(0L, (_feedback?.AggregatedFeedbackCount ?? 0L) - _feedbackAggregatedAtStart)}");
        report.AppendLine($"feedback_dropped_delta={Math.Max(0L, (_feedback?.DroppedFeedbackCount ?? 0L) - _feedbackDroppedAtStart)}");
        report.AppendLine();

        report.AppendLine("[memory]");
        report.AppendLine($"managed_memory_start_mb={_managedMemoryStartMb:0.0}");
        report.AppendLine($"managed_memory_max_mb={_managedMemoryMaxMb:0.0}");
        report.AppendLine($"managed_memory_end_mb={managedEnd:0.0}");
        report.AppendLine($"working_set_start_mb={BytesToMb(_workingSetStartBytes):0.0}");
        report.AppendLine($"working_set_max_mb={BytesToMb(_workingSetMaxBytes):0.0}");
        report.AppendLine($"working_set_end_mb={BytesToMb(workingSetEnd):0.0}");
        report.AppendLine($"video_memory_start_mb={BytesToMb(_videoMemoryStartBytes):0.0}");
        report.AppendLine($"video_memory_max_mb={BytesToMb(_videoMemoryMaxBytes):0.0}");
        report.AppendLine($"video_memory_end_mb={BytesToMb(videoMemoryEnd):0.0}");
        report.AppendLine($"gc_gen0_delta={GC.CollectionCount(0) - _gcAtStart[0]}");
        report.AppendLine($"gc_gen1_delta={GC.CollectionCount(1) - _gcAtStart[1]}");
        report.AppendLine($"gc_gen2_delta={GC.CollectionCount(2) - _gcAtStart[2]}");
        report.AppendLine();

        report.AppendLine("[system]");
        report.AppendLine($"os={OS.GetName()}");
        report.AppendLine($"processor={Sanitize(OS.GetProcessorName())}");
        report.AppendLine($"processor_threads={OS.GetProcessorCount()}");
        report.AppendLine($"gpu={Sanitize(RenderingServer.GetVideoAdapterName())}");
        report.AppendLine($"rendering_method={RenderingServer.GetCurrentRenderingMethod()}");
        report.AppendLine($"rendering_driver={RenderingServer.GetCurrentRenderingDriverName()}");
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        report.AppendLine($"viewport={viewport.X:0}x{viewport.Y:0}");
        report.AppendLine();

        report.AppendLine("[stage_events]");
        foreach (string stage in _stageEvents) report.AppendLine(stage);
        report.AppendLine();

        report.AppendLine("[timeline_csv]");
        report.AppendLine("time_s,fps,last_frame_ms,render_cpu_ms,render_gpu_ms,draw_calls,automation_units,presented_miners,automation_blocks_per_s,mined_voxels,presented_chunks,backface_culled,frustum_culled,dirty_chunks,sparse_pending,sparse_frontier,deferred_automation,managed_mb");
        foreach (string line in _timeline) report.AppendLine(line);
        return report.ToString();
    }

    private void ClearReferences()
    {
        _view = null;
        _miners = null;
        _manual = null;
        _camera = null;
        _feedback = null;
        _standardBenchmark = null;
    }

    private static double ManagedMemoryMb()
        => GC.GetTotalMemory(false) / (1024.0 * 1024.0);

    private static double BytesToMb(long bytes)
        => bytes / (1024.0 * 1024.0);

    private static double BytesToMb(ulong bytes)
        => bytes / (1024.0 * 1024.0);

    private static double Average(double total, long count)
        => count <= 0 ? 0.0 : total / count;

    private static double Average(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        double total = 0.0;
        foreach (double value in values) total += value;
        return total / values.Count;
    }

    private static double Percent(long value, long total)
        => total <= 0 ? 0.0 : value * 100.0 / total;

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0.0;
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        double position = Math.Clamp(percentile, 0.0, 1.0) * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = Math.Min(sorted.Length - 1, lower + 1);
        double fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static string Sanitize(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
}
