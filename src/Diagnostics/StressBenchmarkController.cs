using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Diagnostics;

public partial class StressBenchmarkController : Node
{
    private const double BenchmarkDurationSeconds = 20.0;
    private const int GeneratorProbesPerFrame = 128;
    private const double BulkIntervalSeconds = 2.0;
    private const double DiagnosticSampleIntervalSeconds = 0.10;
    private const double TimelineSampleIntervalSeconds = 1.0;

    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private OrbitCameraController _camera = null!;
    private MinerSimulationService? _miners;
    private IncrementalFeedbackView? _feedback;
    private VirtualWorld? _aggregateBenchmarkWorld;
    private Rid _viewportRid;
    private bool _running;
    private double _elapsed;
    private double _lastBulkAt;
    private double _nextDiagnosticAt;
    private double _nextTimelineAt;
    private long _probeCount;
    private long _bulkBlocks;
    private double _generatorMilliseconds;
    private double _maxProbeBatchMilliseconds;
    private double _minimumFps = double.MaxValue;
    private long _regionCursor;
    private uint _randomState = 0x9e3779b9u;
    private ulong _startedAtUsec;
    private ulong _lastFrameUsec;

    private readonly List<double> _frameTimesMs = new(4096);
    private readonly List<string> _timeline = new(24);
    private double _frameTimeTotalMs;
    private double _maximumFrameTimeMs;
    private long _framesOver16_67;
    private long _framesOver25;
    private long _framesOver33_33;
    private long _framesOver50;
    private long _framesOver100;

    private long _liveMinedAtStart;
    private long _miningTotalAtStart;
    private long _currencyAtStart;
    private long _chunkBuildsAtStart;
    private double _chunkBuildMillisecondsAtStart;
    private long _lastObservedChunkBuildCount;
    private double _maxObservedChunkBuildMilliseconds;
    private long _sparseBuildsAtStart;
    private double _sparseBuildMillisecondsAtStart;
    private long _lastObservedSparseBuildCount;
    private double _maxObservedSparseBuildMilliseconds;
    private long _streamLoadsAtStart;
    private long _streamUnloadsAtStart;
    private long _sampleCacheHitsAtStart;
    private long _sampleCacheMissesAtStart;
    private long _automationQueuedAtStart;
    private long _automationSuppressedAtStart;
    private long _popDroppedAtStart;
    private long _debrisDroppedAtStart;
    private long _feedbackSpawnedAtStart;
    private long _feedbackAggregatedAtStart;
    private long _feedbackDroppedAtStart;

    private double _managedMemoryStartMb;
    private double _managedMemoryMaximumMb;
    private long _workingSetStartBytes;
    private long _workingSetMaximumBytes;
    private readonly int[] _gcAtStart = new int[3];

    private long _diagnosticSamples;
    private double _renderCpuMsTotal;
    private double _renderCpuMsMaximum;
    private long _renderCpuSamples;
    private double _renderGpuMsTotal;
    private double _renderGpuMsMaximum;
    private long _renderGpuSamples;
    private double _renderSetupCpuMsTotal;
    private double _renderSetupCpuMsMaximum;
    private double _drawCallsTotal;
    private ulong _drawCallsMaximum;
    private double _renderObjectsTotal;
    private ulong _renderObjectsMaximum;
    private double _primitivesTotal;
    private ulong _primitivesMaximum;
    private ulong _videoMemoryStartBytes;
    private ulong _videoMemoryMaximumBytes;
    private double _presentedChunksTotal;
    private double _backfaceCulledTotal;
    private double _frustumCulledTotal;
    private double _hiddenTreeBatchesTotal;
    private double _shadowDisabledBatchesTotal;
    private int _maximumDirtyChunks;
    private int _maximumPendingLoads;
    private int _maximumSparsePending;
    private long _maximumSparseFrontier;
    private int _maximumDeferredAutomationChunks;
    private int _maximumPresentedMiners;
    private int _maximumAttentionMiners;
    private double _maximumAutomationBlocksPerSecond;
    private double _lastRenderCpuMs;
    private double _lastRenderGpuMs;
    private ulong _lastDrawCalls;

    public bool IsRunning => _running;

    public void Initialize(
        VirtualWorld world,
        WorldView view,
        MiningService mining,
        OrbitCameraController camera,
        MinerSimulationService? miners = null)
    {
        _world = world;
        _view = view;
        _mining = mining;
        _camera = camera;
        _miners = miners;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F7)
        {
            return;
        }

        if (!_world.Profile.UsesStreamingRenderer)
        {
            GD.Print("Stress benchmark requires a streaming profile. Use F8 to enter stress_1000 first.");
            return;
        }

        if (_running)
        {
            Finish("cancelled");
        }
        else
        {
            StartBenchmark();
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_running) return;

        ulong nowUsec = Time.GetTicksUsec();
        _elapsed = (nowUsec - _startedAtUsec) / 1_000_000.0;
        double wallDelta = Math.Max(0.0, (nowUsec - _lastFrameUsec) / 1_000_000.0);
        _lastFrameUsec = nowUsec;
        CaptureFrameTiming(wallDelta);

        float orbitDelta = (float)Math.Min(wallDelta, 0.25);
        _camera.AddOrbitDegrees(14.0f * orbitDelta, MathF.Sin((float)_elapsed * 0.7f) * 0.10f);

        ulong started = Time.GetTicksUsec();
        ProbeGenerator();
        double probeMs = (Time.GetTicksUsec() - started) / 1000.0;
        _generatorMilliseconds += probeMs;
        _maxProbeBatchMilliseconds = Math.Max(_maxProbeBatchMilliseconds, probeMs);
        _minimumFps = Math.Min(_minimumFps, Engine.GetFramesPerSecond());

        ObserveBuildMetrics();

        if (_elapsed >= _nextDiagnosticAt)
        {
            _nextDiagnosticAt += DiagnosticSampleIntervalSeconds;
            CaptureDiagnosticSample();
        }

        if (_elapsed >= _nextTimelineAt)
        {
            _nextTimelineAt += TimelineSampleIntervalSeconds;
            CaptureTimelineSnapshot();
        }

        if (_elapsed - _lastBulkAt >= BulkIntervalSeconds)
        {
            _lastBulkAt = _elapsed;
            RegionCoord region = RegionFromCursor(_regionCursor++);

            // Aggregate-state pressure must never mutate the rendered/player-owned stress world. The
            // previous benchmark exhausted live regions and then rebuilt their chunks, which both made
            // the cube disappear progressively and turned F7 into an artificial renderer worst case.
            if (_aggregateBenchmarkWorld is not null
                && _aggregateBenchmarkWorld.TryExhaustRegion(region, out long blocksMined))
            {
                _bulkBlocks = checked(_bulkBlocks + blocksMined);
            }
        }

        if (_elapsed >= BenchmarkDurationSeconds)
        {
            Finish("complete");
        }
    }

    public override void _ExitTree()
    {
        if (_running)
        {
            ulong nowUsec = Time.GetTicksUsec();
            _elapsed = _startedAtUsec == 0 ? _elapsed : (nowUsec - _startedAtUsec) / 1_000_000.0;
            Finish("aborted");
        }
    }

    private void StartBenchmark()
    {
        _running = true;
        _startedAtUsec = Time.GetTicksUsec();
        _lastFrameUsec = _startedAtUsec;
        _elapsed = 0.0;
        _lastBulkAt = 0.0;
        _nextDiagnosticAt = 0.0;
        _nextTimelineAt = 0.0;
        _probeCount = 0L;
        _bulkBlocks = 0L;
        _generatorMilliseconds = 0.0;
        _maxProbeBatchMilliseconds = 0.0;
        _minimumFps = double.MaxValue;
        _regionCursor = 0L;
        _randomState = unchecked((uint)_world.Profile.Seed) ^ 0x9e3779b9u;

        _frameTimesMs.Clear();
        _timeline.Clear();
        _frameTimeTotalMs = 0.0;
        _maximumFrameTimeMs = 0.0;
        _framesOver16_67 = 0L;
        _framesOver25 = 0L;
        _framesOver33_33 = 0L;
        _framesOver50 = 0L;
        _framesOver100 = 0L;

        _liveMinedAtStart = _world.State.MinedVoxelCount;
        _miningTotalAtStart = _mining.TotalMined;
        _currencyAtStart = _mining.Currency;
        _chunkBuildsAtStart = _view.TotalChunkBuilds;
        _chunkBuildMillisecondsAtStart = _view.TotalChunkBuildMilliseconds;
        _lastObservedChunkBuildCount = _chunkBuildsAtStart;
        _maxObservedChunkBuildMilliseconds = 0.0;
        _sparseBuildsAtStart = _view.SparseExposureOverlayBuilds;
        _sparseBuildMillisecondsAtStart = _view.TotalSparseExposureOverlayBuildMilliseconds;
        _lastObservedSparseBuildCount = _sparseBuildsAtStart;
        _maxObservedSparseBuildMilliseconds = 0.0;
        _streamLoadsAtStart = _view.StreamedChunkLoads;
        _streamUnloadsAtStart = _view.StreamedChunkUnloads;
        _sampleCacheHitsAtStart = _world.GeneratedSampleCacheHits;
        _sampleCacheMissesAtStart = _world.GeneratedSampleCacheMisses;
        _automationQueuedAtStart = _view.AutomationPresentationUpdatesQueued;
        _automationSuppressedAtStart = _view.AutomationPresentationUpdatesSuppressed;
        _popDroppedAtStart = _view.DroppedMinePopCount;
        _debrisDroppedAtStart = _view.DroppedDebrisBurstCount;

        _feedback = GetParent()?.GetNodeOrNull<IncrementalFeedbackView>("IncrementalFeedbackView");
        _feedbackSpawnedAtStart = _feedback?.SpawnedFeedbackCount ?? 0L;
        _feedbackAggregatedAtStart = _feedback?.AggregatedFeedbackCount ?? 0L;
        _feedbackDroppedAtStart = _feedback?.DroppedFeedbackCount ?? 0L;

        _managedMemoryStartMb = ManagedMemoryMb();
        _managedMemoryMaximumMb = _managedMemoryStartMb;
        _workingSetStartBytes = Environment.WorkingSet;
        _workingSetMaximumBytes = _workingSetStartBytes;
        for (int generation = 0; generation < _gcAtStart.Length; generation++)
        {
            _gcAtStart[generation] = GC.CollectionCount(generation);
        }

        _diagnosticSamples = 0L;
        _renderCpuMsTotal = 0.0;
        _renderCpuMsMaximum = 0.0;
        _renderCpuSamples = 0L;
        _renderGpuMsTotal = 0.0;
        _renderGpuMsMaximum = 0.0;
        _renderGpuSamples = 0L;
        _renderSetupCpuMsTotal = 0.0;
        _renderSetupCpuMsMaximum = 0.0;
        _drawCallsTotal = 0.0;
        _drawCallsMaximum = 0UL;
        _renderObjectsTotal = 0.0;
        _renderObjectsMaximum = 0UL;
        _primitivesTotal = 0.0;
        _primitivesMaximum = 0UL;
        _presentedChunksTotal = 0.0;
        _backfaceCulledTotal = 0.0;
        _frustumCulledTotal = 0.0;
        _hiddenTreeBatchesTotal = 0.0;
        _shadowDisabledBatchesTotal = 0.0;
        _maximumDirtyChunks = 0;
        _maximumPendingLoads = 0;
        _maximumSparsePending = 0;
        _maximumSparseFrontier = 0L;
        _maximumDeferredAutomationChunks = 0;
        _maximumPresentedMiners = 0;
        _maximumAttentionMiners = 0;
        _maximumAutomationBlocksPerSecond = 0.0;
        _lastRenderCpuMs = 0.0;
        _lastRenderGpuMs = 0.0;
        _lastDrawCalls = 0UL;

        _viewportRid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);
        _videoMemoryStartBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        _videoMemoryMaximumBytes = _videoMemoryStartBytes;

        _aggregateBenchmarkWorld = new VirtualWorld(_world.Profile);
        _aggregateBenchmarkWorld.InitializeMineableBlockCount();

        GD.Print(
            "Stress benchmark started: 20s wall-clock camera orbit + generator probes + detached aggregate-state mining. " +
            "The report now includes frame percentiles/stutters, CPU/GPU render time, draw calls, VRAM/RAM/GC, culling/LOD, " +
            "queue high-water marks, automation/presentation deltas and a 1-second timeline. F7 cancels and still writes the report.");
    }

    private void CaptureFrameTiming(double wallDelta)
    {
        if (wallDelta <= 0.0 || wallDelta > 5.0) return;
        double frameMs = wallDelta * 1000.0;
        _frameTimesMs.Add(frameMs);
        _frameTimeTotalMs += frameMs;
        _maximumFrameTimeMs = Math.Max(_maximumFrameTimeMs, frameMs);
        if (frameMs > 16.67) _framesOver16_67++;
        if (frameMs > 25.0) _framesOver25++;
        if (frameMs > 33.33) _framesOver33_33++;
        if (frameMs > 50.0) _framesOver50++;
        if (frameMs > 100.0) _framesOver100++;
    }

    private void ObserveBuildMetrics()
    {
        long buildCount = _view.TotalChunkBuilds;
        if (buildCount != _lastObservedChunkBuildCount)
        {
            _lastObservedChunkBuildCount = buildCount;
            _maxObservedChunkBuildMilliseconds = Math.Max(
                _maxObservedChunkBuildMilliseconds,
                _view.LastChunkBuildMilliseconds);
        }

        long sparseBuildCount = _view.SparseExposureOverlayBuilds;
        if (sparseBuildCount != _lastObservedSparseBuildCount)
        {
            _lastObservedSparseBuildCount = sparseBuildCount;
            _maxObservedSparseBuildMilliseconds = Math.Max(
                _maxObservedSparseBuildMilliseconds,
                _view.LastSparseExposureOverlayBuildMilliseconds);
        }
    }

    private void CaptureDiagnosticSample()
    {
        _diagnosticSamples++;
        double managedMb = ManagedMemoryMb();
        _managedMemoryMaximumMb = Math.Max(_managedMemoryMaximumMb, managedMb);
        _workingSetMaximumBytes = Math.Max(_workingSetMaximumBytes, Environment.WorkingSet);

        double renderCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewportRid);
        double renderGpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewportRid);
        double renderSetupCpuMs = RenderingServer.GetFrameSetupTimeCpu();
        _lastRenderCpuMs = renderCpuMs;
        _lastRenderGpuMs = renderGpuMs;

        if (renderCpuMs > 0.0)
        {
            _renderCpuMsTotal += renderCpuMs;
            _renderCpuMsMaximum = Math.Max(_renderCpuMsMaximum, renderCpuMs);
            _renderCpuSamples++;
        }
        if (renderGpuMs > 0.0)
        {
            _renderGpuMsTotal += renderGpuMs;
            _renderGpuMsMaximum = Math.Max(_renderGpuMsMaximum, renderGpuMs);
            _renderGpuSamples++;
        }
        _renderSetupCpuMsTotal += Math.Max(0.0, renderSetupCpuMs);
        _renderSetupCpuMsMaximum = Math.Max(_renderSetupCpuMsMaximum, renderSetupCpuMs);

        ulong drawCalls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong renderObjects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        ulong primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        ulong videoMemory = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        _lastDrawCalls = drawCalls;
        _drawCallsTotal += drawCalls;
        _drawCallsMaximum = Math.Max(_drawCallsMaximum, drawCalls);
        _renderObjectsTotal += renderObjects;
        _renderObjectsMaximum = Math.Max(_renderObjectsMaximum, renderObjects);
        _primitivesTotal += primitives;
        _primitivesMaximum = Math.Max(_primitivesMaximum, primitives);
        _videoMemoryMaximumBytes = Math.Max(_videoMemoryMaximumBytes, videoMemory);

        _presentedChunksTotal += _view.PresentedChunkCount;
        _backfaceCulledTotal += _view.BackfaceCulledChunkCount;
        _frustumCulledTotal += _view.FrustumCulledChunkCount;
        _hiddenTreeBatchesTotal += _view.LodHiddenTreeBatchCount;
        _shadowDisabledBatchesTotal += _view.LodShadowDisabledBatchCount;
        _maximumDirtyChunks = Math.Max(_maximumDirtyChunks, _view.PendingChunkRebuilds);
        _maximumPendingLoads = Math.Max(_maximumPendingLoads, _view.PendingChunkLoads);
        _maximumSparsePending = Math.Max(_maximumSparsePending, _view.PendingSparseExposureOverlays);
        _maximumSparseFrontier = Math.Max(_maximumSparseFrontier, _view.SparseExposureFrontierCandidateCount);
        _maximumDeferredAutomationChunks = Math.Max(_maximumDeferredAutomationChunks, _view.DeferredAutomationChunkCount);

        if (_miners is not null)
        {
            _maximumPresentedMiners = Math.Max(_maximumPresentedMiners, _miners.PresentedMinerCount);
            _maximumAttentionMiners = Math.Max(_maximumAttentionMiners, _miners.AttentionMinerCount);
            _maximumAutomationBlocksPerSecond = Math.Max(_maximumAutomationBlocksPerSecond, _miners.BlocksPerSecond);
        }
    }

    private void CaptureTimelineSnapshot()
    {
        _timeline.Add(
            $"{_elapsed:0.0}," +
            $"{Engine.GetFramesPerSecond():0.0}," +
            $"{(_frameTimesMs.Count == 0 ? 0.0 : _frameTimesMs[^1]):0.00}," +
            $"{_lastRenderCpuMs:0.00}," +
            $"{_lastRenderGpuMs:0.00}," +
            $"{_lastDrawCalls}," +
            $"{_view.PresentedChunkCount}," +
            $"{_view.BackfaceCulledChunkCount}," +
            $"{_view.FrustumCulledChunkCount}," +
            $"{_view.PendingChunkRebuilds}," +
            $"{_view.PendingSparseExposureOverlays}," +
            $"{_view.SparseExposureFrontierCandidateCount}," +
            $"{_view.DeferredAutomationChunkCount}," +
            $"{(_miners?.Miners.Count ?? 0)}," +
            $"{ManagedMemoryMb():0.0}");
    }

    private void ProbeGenerator()
    {
        int max = _world.MaxCoordinate;
        int shellDepth = Math.Max(8, (int)MathF.Ceiling(
            _world.Profile.TerrainAmplitude + _world.Profile.DetailAmplitude + 12.0f));

        for (int index = 0; index < GeneratorProbesPerFrame; index++)
        {
            int face = NextInt(6);
            int a = NextInt(max * 2 + 1) - max;
            int b = NextInt(max * 2 + 1) - max;
            int depth = NextInt(shellDepth + 1);
            int radial = Math.Max(0, max - depth);
            Vector3I voxel = face switch
            {
                0 => new Vector3I(radial, a, b),
                1 => new Vector3I(-radial, a, b),
                2 => new Vector3I(a, radial, b),
                3 => new Vector3I(a, -radial, b),
                4 => new Vector3I(a, b, radial),
                _ => new Vector3I(a, b, -radial),
            };
            _ = _world.Source.SampleVoxel(voxel);
            _probeCount++;
        }
    }

    private RegionCoord RegionFromCursor(long cursor)
    {
        long axis = _world.RegionAxisCount;
        long count = _world.TotalLogicalRegionCount;
        long index = count <= 0 ? 0 : cursor % count;
        int z = (int)(index % axis) + _world.MinRegionCoordinate;
        index /= axis;
        int y = (int)(index % axis) + _world.MinRegionCoordinate;
        index /= axis;
        int x = (int)(index % axis) + _world.MinRegionCoordinate;
        return new RegionCoord(x, y, z);
    }

    private int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 1) return 0;
        _randomState = unchecked(_randomState * 1664525u + 1013904223u);
        return (int)(_randomState % (uint)exclusiveMax);
    }

    private void Finish(string reason)
    {
        if (!_running) return;
        _running = false;
        CaptureDiagnosticSample();
        ObserveBuildMetrics();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, false);

        double averageProbeUs = _probeCount == 0
            ? 0.0
            : (_generatorMilliseconds * 1000.0) / _probeCount;
        double minFps = double.IsFinite(_minimumFps) ? _minimumFps : 0.0;
        long frameCount = _frameTimesMs.Count;
        double averageFrameMs = frameCount == 0 ? 0.0 : _frameTimeTotalMs / frameCount;
        double averageFps = averageFrameMs <= 0.0 ? 0.0 : 1000.0 / averageFrameMs;
        double frameP50 = Percentile(_frameTimesMs, 0.50);
        double frameP95 = Percentile(_frameTimesMs, 0.95);
        double frameP99 = Percentile(_frameTimesMs, 0.99);

        long chunkBuildDelta = Math.Max(0L, _view.TotalChunkBuilds - _chunkBuildsAtStart);
        double chunkBuildMsDelta = Math.Max(0.0, _view.TotalChunkBuildMilliseconds - _chunkBuildMillisecondsAtStart);
        double benchmarkChunkBuildAverage = chunkBuildDelta == 0 ? 0.0 : chunkBuildMsDelta / chunkBuildDelta;
        long sparseBuildDelta = Math.Max(0L, _view.SparseExposureOverlayBuilds - _sparseBuildsAtStart);
        double sparseBuildMsDelta = Math.Max(0.0, _view.TotalSparseExposureOverlayBuildMilliseconds - _sparseBuildMillisecondsAtStart);
        double sparseBuildAverage = sparseBuildDelta == 0 ? 0.0 : sparseBuildMsDelta / sparseBuildDelta;
        long liveMinedDelta = _world.State.MinedVoxelCount - _liveMinedAtStart;
        long miningTotalDelta = _mining.TotalMined - _miningTotalAtStart;
        long currencyDelta = _mining.Currency - _currencyAtStart;
        int aggregateRegions = _aggregateBenchmarkWorld?.State.ExhaustedRegionCount ?? 0;
        long aggregateSparse = _aggregateBenchmarkWorld?.State.SparseVoxelOverrideCount ?? 0L;

        long cacheHitDelta = Math.Max(0L, _world.GeneratedSampleCacheHits - _sampleCacheHitsAtStart);
        long cacheMissDelta = Math.Max(0L, _world.GeneratedSampleCacheMisses - _sampleCacheMissesAtStart);
        long cacheTotalDelta = cacheHitDelta + cacheMissDelta;
        double cacheHitRate = cacheTotalDelta == 0 ? 0.0 : cacheHitDelta * 100.0 / cacheTotalDelta;

        double managedEndMb = ManagedMemoryMb();
        long workingSetEndBytes = Environment.WorkingSet;
        ulong videoMemoryEndBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        long diagnostics = Math.Max(1L, _diagnosticSamples);
        int automationUnits = _miners?.Miners.Count ?? 0;
        int automationPresented = _miners?.PresentedMinerCount ?? 0;
        int automationAttention = _miners?.AttentionMinerCount ?? 0;
        double automationRate = _miners?.BlocksPerSecond ?? 0.0;
        int automationBudget = _miners?.MaxMiningOperationsPerFrame ?? 0;

        var report = new StringBuilder(8192);
        report.AppendLine($"Stress benchmark {reason}");
        report.AppendLine("report_version=2");
        report.AppendLine($"timestamp_local={DateTimeOffset.Now:O}");
        report.AppendLine($"world={_world.Profile.Id}");
        report.AppendLine($"duration_s={_elapsed:0.00}");
        report.AppendLine($"os={OS.GetName()}");
        report.AppendLine($"processor={Sanitize(OS.GetProcessorName())}");
        report.AppendLine($"processor_threads={OS.GetProcessorCount()}");
        report.AppendLine($"gpu={Sanitize(RenderingServer.GetVideoAdapterName())}");
        report.AppendLine($"rendering_method={RenderingServer.GetCurrentRenderingMethod()}");
        report.AppendLine($"rendering_driver={RenderingServer.GetCurrentRenderingDriverName()}");
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        report.AppendLine($"viewport={viewportSize.X:0}x{viewportSize.Y:0}");
        report.AppendLine($"camera_fov={_camera.Camera.Fov:0.0}");
        report.AppendLine($"logical_size={_world.Profile.LogicalWidth}x{_world.Profile.LogicalHeight}x{_world.Profile.LogicalDepth}");
        report.AppendLine($"chunk_size={_world.Profile.ChunkSize}");
        report.AppendLine($"block_spacing={_world.Profile.BlockSpacing:0.###}");
        report.AppendLine($"full_surface_renderer={_view.FullSurfaceRenderer}");
        report.AppendLine();

        report.AppendLine("[frame_timing]");
        report.AppendLine($"frame_samples={frameCount}");
        report.AppendLine($"average_fps_from_wall_time={averageFps:0.0}");
        report.AppendLine($"minimum_observed_fps_engine={minFps:0.0}");
        report.AppendLine($"frame_ms_avg={averageFrameMs:0.000}");
        report.AppendLine($"frame_ms_p50={frameP50:0.000}");
        report.AppendLine($"frame_ms_p95={frameP95:0.000}");
        report.AppendLine($"frame_ms_p99={frameP99:0.000}");
        report.AppendLine($"frame_ms_max={_maximumFrameTimeMs:0.000}");
        report.AppendLine($"fps_equivalent_p95_frame={(frameP95 <= 0.0 ? 0.0 : 1000.0 / frameP95):0.0}");
        report.AppendLine($"fps_equivalent_p99_frame={(frameP99 <= 0.0 ? 0.0 : 1000.0 / frameP99):0.0}");
        report.AppendLine($"frames_over_16_67ms={_framesOver16_67} ({Percent(_framesOver16_67, frameCount):0.00}%)");
        report.AppendLine($"frames_over_25ms={_framesOver25} ({Percent(_framesOver25, frameCount):0.00}%)");
        report.AppendLine($"frames_over_33_33ms={_framesOver33_33} ({Percent(_framesOver33_33, frameCount):0.00}%)");
        report.AppendLine($"frames_over_50ms={_framesOver50} ({Percent(_framesOver50, frameCount):0.00}%)");
        report.AppendLine($"frames_over_100ms={_framesOver100} ({Percent(_framesOver100, frameCount):0.00}%)");
        report.AppendLine();

        report.AppendLine("[rendering]");
        report.AppendLine($"diagnostic_samples={_diagnosticSamples}");
        report.AppendLine($"render_cpu_ms_avg={Average(_renderCpuMsTotal, _renderCpuSamples):0.000}");
        report.AppendLine($"render_cpu_ms_max={_renderCpuMsMaximum:0.000}");
        report.AppendLine($"render_gpu_ms_avg={Average(_renderGpuMsTotal, _renderGpuSamples):0.000}");
        report.AppendLine($"render_gpu_ms_max={_renderGpuMsMaximum:0.000}");
        report.AppendLine($"render_setup_cpu_ms_avg={_renderSetupCpuMsTotal / diagnostics:0.000}");
        report.AppendLine($"render_setup_cpu_ms_max={_renderSetupCpuMsMaximum:0.000}");
        report.AppendLine($"draw_calls_avg={_drawCallsTotal / diagnostics:0.0}");
        report.AppendLine($"draw_calls_max={_drawCallsMaximum}");
        report.AppendLine($"render_objects_avg={_renderObjectsTotal / diagnostics:0.0}");
        report.AppendLine($"render_objects_max={_renderObjectsMaximum}");
        report.AppendLine($"render_primitives_avg={_primitivesTotal / diagnostics:0.0}");
        report.AppendLine($"render_primitives_max={_primitivesMaximum}");
        report.AppendLine($"video_memory_mb_start={BytesToMb(_videoMemoryStartBytes):0.0}");
        report.AppendLine($"video_memory_mb_max={BytesToMb(_videoMemoryMaximumBytes):0.0}");
        report.AppendLine($"video_memory_mb_end={BytesToMb(videoMemoryEndBytes):0.0}");
        report.AppendLine();

        report.AppendLine("[world_renderer]");
        report.AppendLine($"chunks_resident_end={_view.VisibleChunkCount}");
        report.AppendLine($"chunks_presented_avg={_presentedChunksTotal / diagnostics:0.0}");
        report.AppendLine($"chunks_presented_end={_view.PresentedChunkCount}");
        report.AppendLine($"chunks_backface_culled_avg={_backfaceCulledTotal / diagnostics:0.0}");
        report.AppendLine($"chunks_frustum_culled_avg={_frustumCulledTotal / diagnostics:0.0}");
        report.AppendLine($"lod_tree_batches_hidden_avg={_hiddenTreeBatchesTotal / diagnostics:0.0}");
        report.AppendLine($"lod_shadow_batches_disabled_avg={_shadowDisabledBatchesTotal / diagnostics:0.0}");
        report.AppendLine($"dirty_chunks_high_water={_maximumDirtyChunks}");
        report.AppendLine($"pending_chunk_loads_high_water={_maximumPendingLoads}");
        report.AppendLine($"sparse_pending_high_water={_maximumSparsePending}");
        report.AppendLine($"sparse_frontier_high_water={_maximumSparseFrontier}");
        report.AppendLine($"deferred_automation_chunks_high_water={_maximumDeferredAutomationChunks}");
        report.AppendLine($"chunk_builds_during_benchmark={chunkBuildDelta}");
        report.AppendLine($"chunk_build_total_ms_during_benchmark={chunkBuildMsDelta:0.000}");
        report.AppendLine($"chunk_build_avg_ms_during_benchmark={benchmarkChunkBuildAverage:0.000}");
        report.AppendLine($"chunk_build_max_observed_ms={_maxObservedChunkBuildMilliseconds:0.000}");
        report.AppendLine($"sparse_overlay_builds_during_benchmark={sparseBuildDelta}");
        report.AppendLine($"sparse_overlay_total_ms_during_benchmark={sparseBuildMsDelta:0.000}");
        report.AppendLine($"sparse_overlay_avg_ms_during_benchmark={sparseBuildAverage:0.000}");
        report.AppendLine($"sparse_overlay_max_observed_ms={_maxObservedSparseBuildMilliseconds:0.000}");
        report.AppendLine($"stream_loads_during_benchmark={Math.Max(0L, _view.StreamedChunkLoads - _streamLoadsAtStart)}");
        report.AppendLine($"stream_unloads_during_benchmark={Math.Max(0L, _view.StreamedChunkUnloads - _streamUnloadsAtStart)}");
        report.AppendLine();

        report.AppendLine("[automation_and_mining]");
        report.AppendLine($"automation_units_end={automationUnits}");
        report.AppendLine($"automation_presented_end={automationPresented}");
        report.AppendLine($"automation_presented_max={_maximumPresentedMiners}");
        report.AppendLine($"automation_attention_end={automationAttention}");
        report.AppendLine($"automation_attention_max={_maximumAttentionMiners}");
        report.AppendLine($"automation_nominal_blocks_per_second_end={automationRate:0.00}");
        report.AppendLine($"automation_nominal_blocks_per_second_max={_maximumAutomationBlocksPerSecond:0.00}");
        report.AppendLine($"automation_work_units_per_frame_cap={automationBudget}");
        report.AppendLine($"automation_presentation_queued_delta={Math.Max(0L, _view.AutomationPresentationUpdatesQueued - _automationQueuedAtStart)}");
        report.AppendLine($"automation_presentation_suppressed_delta={Math.Max(0L, _view.AutomationPresentationUpdatesSuppressed - _automationSuppressedAtStart)}");
        report.AppendLine($"detail_distance={GraphicsSettingsRuntime.Current?.DetailDistance ?? 1}");
        report.AppendLine($"live_blocks_mined_during_benchmark={liveMinedDelta}");
        report.AppendLine($"mining_service_total_delta={miningTotalDelta}");
        report.AppendLine($"currency_delta={currencyDelta}");
        report.AppendLine($"mining_pop_dropped_delta={Math.Max(0L, _view.DroppedMinePopCount - _popDroppedAtStart)}");
        report.AppendLine($"mining_debris_dropped_delta={Math.Max(0L, _view.DroppedDebrisBurstCount - _debrisDroppedAtStart)}");
        report.AppendLine($"feedback_spawned_delta={Math.Max(0L, (_feedback?.SpawnedFeedbackCount ?? _feedbackSpawnedAtStart) - _feedbackSpawnedAtStart)}");
        report.AppendLine($"feedback_aggregated_delta={Math.Max(0L, (_feedback?.AggregatedFeedbackCount ?? _feedbackAggregatedAtStart) - _feedbackAggregatedAtStart)}");
        report.AppendLine($"feedback_dropped_delta={Math.Max(0L, (_feedback?.DroppedFeedbackCount ?? _feedbackDroppedAtStart) - _feedbackDroppedAtStart)}");
        report.AppendLine();

        report.AppendLine("[generator_and_state]");
        report.AppendLine($"generator_probes={_probeCount}");
        report.AppendLine($"generator_avg_us={averageProbeUs:0.000}");
        report.AppendLine($"probe_batch_max_ms={_maxProbeBatchMilliseconds:0.000}");
        report.AppendLine($"generated_sample_cache_hits_delta={cacheHitDelta}");
        report.AppendLine($"generated_sample_cache_misses_delta={cacheMissDelta}");
        report.AppendLine($"generated_sample_cache_hit_rate={cacheHitRate:0.00}%");
        report.AppendLine($"state_sparse_voxels_end={_world.State.SparseVoxelOverrideCount}");
        report.AppendLine($"state_modified_chunks_end={_world.State.ModifiedChunkCount}");
        report.AppendLine($"state_exhausted_regions_end={_world.State.ExhaustedRegionCount}");
        report.AppendLine($"aggregate_blocks_mined_detached={_bulkBlocks}");
        report.AppendLine($"aggregate_sparse_voxel_overrides_detached={aggregateSparse}");
        report.AppendLine($"aggregate_exhausted_regions_detached={aggregateRegions}");
        report.AppendLine();

        report.AppendLine("[memory_and_gc]");
        report.AppendLine($"managed_memory_mb_start={_managedMemoryStartMb:0.0}");
        report.AppendLine($"managed_memory_mb_max={_managedMemoryMaximumMb:0.0}");
        report.AppendLine($"managed_memory_mb_end={managedEndMb:0.0}");
        report.AppendLine($"managed_memory_mb_delta={managedEndMb - _managedMemoryStartMb:0.0}");
        report.AppendLine($"working_set_mb_start={BytesToMb(_workingSetStartBytes):0.0}");
        report.AppendLine($"working_set_mb_max={BytesToMb(_workingSetMaximumBytes):0.0}");
        report.AppendLine($"working_set_mb_end={BytesToMb(workingSetEndBytes):0.0}");
        report.AppendLine($"gc_gen0_delta={GC.CollectionCount(0) - _gcAtStart[0]}");
        report.AppendLine($"gc_gen1_delta={GC.CollectionCount(1) - _gcAtStart[1]}");
        report.AppendLine($"gc_gen2_delta={GC.CollectionCount(2) - _gcAtStart[2]}");
        report.AppendLine();

        report.AppendLine("[timeline_1s_csv]");
        report.AppendLine("t_s,engine_fps,last_frame_ms,render_cpu_ms,render_gpu_ms,draw_calls,presented_chunks,backface_culled,frustum_culled,dirty_chunks,sparse_pending,sparse_frontier,deferred_automation_chunks,automation_units,managed_mb");
        foreach (string row in _timeline)
        {
            report.AppendLine(row);
        }

        string reportText = report.ToString();
        GD.Print(reportText);
        using Godot.FileAccess file = Godot.FileAccess.Open("user://stress_benchmark_latest.txt", Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(reportText);
        _aggregateBenchmarkWorld = null;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0.0;
        var sorted = new List<double>(values);
        sorted.Sort();
        double position = Math.Clamp(percentile, 0.0, 1.0) * (sorted.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = Math.Min(sorted.Count - 1, lower + 1);
        double fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static double Percent(long count, long total)
        => total <= 0 ? 0.0 : count * 100.0 / total;

    private static double Average(double total, long count)
        => count <= 0 ? 0.0 : total / count;

    private static double ManagedMemoryMb()
        => GC.GetTotalMemory(false) / (1024.0 * 1024.0);

    private static double BytesToMb(long bytes)
        => bytes / (1024.0 * 1024.0);

    private static double BytesToMb(ulong bytes)
        => bytes / (1024.0 * 1024.0);

    private static string Sanitize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
