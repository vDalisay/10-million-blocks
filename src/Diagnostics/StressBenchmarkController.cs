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

    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private OrbitCameraController _camera = null!;
    private bool _running;
    private double _elapsed;
    private double _bulkTimer;
    private long _probeCount;
    private long _bulkBlocks;
    private double _generatorMilliseconds;
    private double _maxProbeBatchMilliseconds;
    private double _minimumFps = double.MaxValue;
    private long _regionCursor;
    private uint _randomState = 0x9e3779b9u;

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
        if (!_running) return;

        _elapsed += delta;
        _bulkTimer += delta;
        _camera.AddOrbitDegrees(14.0f * (float)delta, MathF.Sin((float)_elapsed * 0.7f) * 0.10f);

        ulong started = Time.GetTicksUsec();
        ProbeGenerator();
        double probeMs = (Time.GetTicksUsec() - started) / 1000.0;
        _generatorMilliseconds += probeMs;
        _maxProbeBatchMilliseconds = Math.Max(_maxProbeBatchMilliseconds, probeMs);
        _minimumFps = Math.Min(_minimumFps, Engine.GetFramesPerSecond());

        if (_bulkTimer >= 2.0)
        {
            _bulkTimer = 0.0;
            RegionCoord region = RegionFromCursor(_regionCursor++);
            BulkMiningResult result = _mining.TryExhaustRegion(region, MiningSource.Debug);
            if (result.Success)
            {
                _bulkBlocks = checked(_bulkBlocks + result.BlocksMined);
                _view.MarkRegionDirty(region);
            }
        }

        if (_elapsed >= BenchmarkDurationSeconds)
        {
            Finish("complete");
        }
    }

    private void StartBenchmark()
    {
        _running = true;
        _elapsed = 0.0;
        _bulkTimer = 0.0;
        _probeCount = 0L;
        _bulkBlocks = 0L;
        _generatorMilliseconds = 0.0;
        _maxProbeBatchMilliseconds = 0.0;
        _minimumFps = double.MaxValue;
        _regionCursor = 0L;
        _randomState = unchecked((uint)_world.Profile.Seed) ^ 0x9e3779b9u;
        GD.Print("Stress benchmark started: 20s camera orbit + generator probes + aggregate region mining. [F7] cancels.");
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
        string report =
            $"Stress benchmark {reason}\n" +
            $"world={_world.Profile.Id}\n" +
            $"duration_s={_elapsed:0.00}\n" +
            $"generator_probes={_probeCount}\n" +
            $"generator_avg_us={averageProbeUs:0.000}\n" +
            $"probe_batch_max_ms={_maxProbeBatchMilliseconds:0.000}\n" +
            $"minimum_observed_fps={minFps:0.0}\n" +
            $"chunk_build_avg_ms={_view.AverageChunkBuildMilliseconds:0.000}\n" +
            $"chunk_build_last_ms={_view.LastChunkBuildMilliseconds:0.000}\n" +
            $"stream_loads={_view.StreamedChunkLoads}\n" +
            $"stream_unloads={_view.StreamedChunkUnloads}\n" +
            $"aggregate_blocks_mined={_bulkBlocks}\n" +
            $"sparse_voxel_overrides={_world.State.SparseVoxelOverrideCount}\n" +
            $"exhausted_regions={_world.State.ExhaustedRegionCount}\n" +
            $"managed_memory_mb={GC.GetTotalMemory(false) / (1024.0 * 1024.0):0.0}";

        GD.Print(report);
        using Godot.FileAccess file = Godot.FileAccess.Open("user://stress_benchmark_latest.txt", Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(report);
    }
}
