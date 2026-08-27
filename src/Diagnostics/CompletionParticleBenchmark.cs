using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Diagnostics;

/// <summary>
/// Renderer-side exact-count completion benchmark. Debug F6 cycles through the reviewed cases without
/// requiring a real world clear. No reward/progression mutation is attached to the ceremony instance.
/// </summary>
public partial class CompletionParticleBenchmark : Node
{
    private static readonly long[] Cases = [25L, 6_824L, 61_225L, 123_412L, 1_000_000L];

    private BlockAssetRegistry _assets = null!;
    private OrbitCameraController _camera = null!;
    private Func<VirtualWorld?> _worldProvider = null!;
    private WorldCompletionCeremony? _ceremony;
    private int _caseIndex = -1;
    private long _activeCount;
    private double _elapsed;
    private double _peakImplosionMs;
    private double _peakScatterMs;
    private double _peakSuctionMs;
    private WorldCompletionVisualStage _stage = WorldCompletionVisualStage.Implosion;

    public void Initialize(BlockAssetRegistry assets, OrbitCameraController camera, Func<VirtualWorld?> worldProvider)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _worldProvider = worldProvider ?? throw new ArgumentNullException(nameof(worldProvider));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!OS.IsDebugBuild() || @event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F6)
            return;

        StartNextCase();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_ceremony is null) return;
        _elapsed += Math.Max(0.0, delta);
        double ms = Math.Max(0.0, delta) * 1000.0;
        switch (_stage)
        {
            case WorldCompletionVisualStage.Implosion: _peakImplosionMs = Math.Max(_peakImplosionMs, ms); break;
            case WorldCompletionVisualStage.BonusScatter: _peakScatterMs = Math.Max(_peakScatterMs, ms); break;
            case WorldCompletionVisualStage.BlackHoleSuction: _peakSuctionMs = Math.Max(_peakSuctionMs, ms); break;
        }
    }

    private void StartNextCase()
    {
        VirtualWorld? world = _worldProvider();
        if (world is null)
        {
            GD.PushWarning("Completion particle benchmark requires an active world.");
            return;
        }

        ClearActive();
        _caseIndex = (_caseIndex + 1) % Cases.Length;
        _activeCount = Cases[_caseIndex];
        Aabb bounds = world.GetWorldBounds();
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float spacing = Math.Max(0.01f, world.Profile.BlockSpacing);
        float worldRadius = Math.Max(spacing * 2.0f, bounds.Size.Length() * 0.5f);
        float scatterRadius = Math.Max(spacing * 4.0f, Math.Min(worldRadius * 0.58f, spacing * 20.0f));

        ulong beforeUsec = Time.GetTicksUsec();
        _ceremony = new WorldCompletionCeremony { Name = $"CompletionParticleBenchmark_{_activeCount}" };
        _ceremony.Initialize(world.Profile, _assets, _camera.Camera, center, _activeCount, scatterRadius, 0L);
        _ceremony.StageChanged += OnStageChanged;
        _ceremony.Completed += OnCompleted;
        AddChild(_ceremony);
        ulong afterUsec = Time.GetTicksUsec();

        _elapsed = 0.0;
        _peakImplosionMs = 0.0;
        _peakScatterMs = 0.0;
        _peakSuctionMs = 0.0;
        _stage = WorldCompletionVisualStage.Implosion;
        double setupMs = (afterUsec - beforeUsec) / 1000.0;
        GD.Print($"COMPLETION PARTICLE BENCH start count={_activeCount:N0} setup={setupMs:0.00}ms adapter='{RenderingServer.GetVideoAdapterName()}'. F6 starts the next preset.");
    }

    private void OnStageChanged(WorldCompletionVisualStage stage) => _stage = stage;

    private void OnCompleted()
    {
        GD.Print(
            $"COMPLETION PARTICLE BENCH done count={_activeCount:N0} elapsed={_elapsed:0.00}s " +
            $"peak_implosion={_peakImplosionMs:0.00}ms peak_scatter={_peakScatterMs:0.00}ms peak_suction={_peakSuctionMs:0.00}ms.");
        ClearActive();
    }

    private void ClearActive()
    {
        if (_ceremony is null) return;
        _ceremony.StageChanged -= OnStageChanged;
        _ceremony.Completed -= OnCompleted;
        _ceremony.QueueFree();
        _ceremony = null;
    }
}
