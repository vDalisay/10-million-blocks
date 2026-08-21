using System;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Diagnostics;

public partial class StressBenchmarkController : Node
{
    private const double BenchmarkDurationSeconds = 20.0;
    private const int GeneratorProbesPerFrame = 128;
    private const double BulkIntervalSeconds = 2.0;

    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private OrbitCameraController _camera = null!;
    private VirtualWorld? _aggregateBenchmarkWorld;
    private bool _running;
    private double _elapsed;
    private double _lastBulkAt;
    private long _probeCount;
    private long _bulkBlocks;
    private double _generatorMilliseconds;
    private double _maxProbeBatchMilliseconds;
    private double _minimumFps = double.MaxValue;
    private long _regionCursor;
    private uint _randomState = 0x9e3779b9u;
    private ulong _startedAtUsec;
    private ulong _lastFrameUsec;
    private long _liveMinedAtStart;
    private long _chunkBuildsAtStart;
    private double _chunkBuildMillisecondsAtStart;
    private long _lastObservedChunkBuildCount;
    private double _maxObservedChunkBuildMilliseconds;

    public bool IsRunning => _running;

    public void Initialize(VirtualWorld world, WorldView view, MiningService mining, OrbitCameraController camera)
    {
        _world = world;
        _view = view;
        _mining = mining;
        _camera = camera;
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
        double wallDelta = (nowUsec - _lastFrameUsec) / 1_000_000.0;
        _lastFrameUsec = nowUsec;

        float orbitDelta = (float)Math.Min(wallDelta, 0.25);
        _camera.AddOrbitDegrees(14.0f * orbitDelta, MathF.Sin((float)_elapsed * 0.7f) * 0.10f);

        ulong started = Time.GetTicksUsec();
        ProbeGenerator();
        double probeMs = (Time.GetTicksUsec() - started) / 1000.0;
        _generatorMilliseconds += probeMs;
        _maxProbeBatchMilliseconds = Math.Max(_maxProbeBatchMilliseconds, probeMs);
        _minimumFps = Math.Min(_minimumFps, Engine.GetFramesPerSecond());

        long buildCount = _view.TotalChunkBuilds;
        if (buildCount != _lastObservedChunkBuildCount)
        {
            _lastObservedChunkBuildCount = buildCount;
            _maxObservedChunkBuildMilliseconds = Math.Max(
                _maxObservedChunkBuildMilliseconds,
                _view.LastChunkBuildMilliseconds);
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
        _probeCount = 0L;
        _bulkBlocks = 0L;
        _generatorMilliseconds = 0.0;
        _maxProbeBatchMilliseconds = 0.0;
        _minimumFps = double.MaxValue;
        _regionCursor = 0L;
        _randomState = unchecked((uint)_world.Profile.Seed) ^ 0x9e3779b9u;
        _liveMinedAtStart = _world.State.MinedVoxelCount;
        _chunkBuildsAtStart = _view.TotalChunkBuilds;
        _chunkBuildMillisecondsAtStart = _view.TotalChunkBuildMilliseconds;
        _lastObservedChunkBuildCount = _chunkBuildsAtStart;
        _maxObservedChunkBuildMilliseconds = 0.0;

        _aggregateBenchmarkWorld = new VirtualWorld(_world.Profile);
        _aggregateBenchmarkWorld.InitializeMineableBlockCount();

        GD.Print(
            "Stress benchmark started: 20s wall-clock camera orbit + generator probes + detached aggregate-state mining. " +
            "F7 no longer removes blocks from the visible world; normal player mining can still be tested simultaneously. [F7] cancels.");
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
        double averageProbeUs = _probeCount == 0
            ? 0.0
            : (_generatorMilliseconds * 1000.0) / _probeCount;
        double minFps = double.IsFinite(_minimumFps) ? _minimumFps : 0.0;
        long chunkBuildDelta = Math.Max(0L, _view.TotalChunkBuilds - _chunkBuildsAtStart);
        double chunkBuildMsDelta = Math.Max(0.0, _view.TotalChunkBuildMilliseconds - _chunkBuildMillisecondsAtStart);
        double benchmarkChunkBuildAverage = chunkBuildDelta == 0 ? 0.0 : chunkBuildMsDelta / chunkBuildDelta;
        long liveMinedDelta = _world.State.MinedVoxelCount - _liveMinedAtStart;
        int aggregateRegions = _aggregateBenchmarkWorld?.State.ExhaustedRegionCount ?? 0;
        long aggregateSparse = _aggregateBenchmarkWorld?.State.SparseVoxelOverrideCount ?? 0L;

        string report =
            $"Stress benchmark {reason}\n" +
            $"world={_world.Profile.Id}\n" +
            $"duration_s={_elapsed:0.00}\n" +
            $"generator_probes={_probeCount}\n" +
            $"generator_avg_us={averageProbeUs:0.000}\n" +
            $"probe_batch_max_ms={_maxProbeBatchMilliseconds:0.000}\n" +
            $"minimum_observed_fps={minFps:0.0}\n" +
            $"chunk_builds_during_benchmark={chunkBuildDelta}\n" +
            $"chunk_build_avg_ms_during_benchmark={benchmarkChunkBuildAverage:0.000}\n" +
            $"chunk_build_max_observed_ms={_maxObservedChunkBuildMilliseconds:0.000}\n" +
            $"chunk_build_avg_ms_lifetime={_view.AverageChunkBuildMilliseconds:0.000}\n" +
            $"chunk_build_last_ms={_view.LastChunkBuildMilliseconds:0.000}\n" +
            $"stream_loads={_view.StreamedChunkLoads}\n" +
            $"stream_unloads={_view.StreamedChunkUnloads}\n" +
            $"aggregate_blocks_mined_detached={_bulkBlocks}\n" +
            $"aggregate_sparse_voxel_overrides_detached={aggregateSparse}\n" +
            $"aggregate_exhausted_regions_detached={aggregateRegions}\n" +
            $"live_blocks_mined_during_benchmark={liveMinedDelta}\n" +
            $"managed_memory_mb={GC.GetTotalMemory(false) / (1024.0 * 1024.0):0.0}";

        GD.Print(report);
        using Godot.FileAccess file = Godot.FileAccess.Open("user://stress_benchmark_latest.txt", Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(report);
        _aggregateBenchmarkWorld = null;
    }
}
