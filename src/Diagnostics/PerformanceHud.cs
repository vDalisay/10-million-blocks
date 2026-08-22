using System;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Diagnostics;

public partial class PerformanceHud : CanvasLayer
{
    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private OrbitCameraController _camera = null!;
    private PanelContainer _panel = null!;
    private Label _label = null!;
    private double _refreshTimer;

    public new bool IsVisible => _panel is not null && _panel.Visible;

    public void Initialize(VirtualWorld world, WorldView view, OrbitCameraController camera)
    {
        _world = world;
        _view = view;
        _camera = camera;
    }

    public override void _Ready()
    {
        Layer = 40;
        _panel = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -580.0f,
            OffsetTop = 16.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 580.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _panel.AddChild(margin);

        _label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        margin.AddChild(_label);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refreshTimer += delta;
        if (_refreshTimer >= 0.25)
        {
            _refreshTimer = 0.0;
            if (IsVisible) Refresh();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F9)
        {
            return;
        }

        _panel.Visible = !_panel.Visible;
        if (_panel.Visible) Refresh();
        GetViewport().SetInputAsHandled();
    }

    private void Refresh()
    {
        if (_label is null || _world is null || _view is null) return;

        double memoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        long cacheTotal = _world.GeneratedSampleCacheHits + _world.GeneratedSampleCacheMisses;
        double cacheHitPercent = cacheTotal <= 0
            ? 0.0
            : _world.GeneratedSampleCacheHits * 100.0 / cacheTotal;
        string renderer = _view.FullSurfaceRenderer
            ? "real-block full surface"
            : _view.StreamingEnabled ? "macro + streamed detail" : "eager real blocks";
        string context = _view.FullSurfaceRenderer
            ? "macro: disabled (real blocks only)"
            : $"macro: {(_view.MacroVisible ? "visible" : "hidden")} opacity {_view.MacroOpacity:0.00}";

        IncrementalFeedbackView? feedback = GetParent()?.GetNodeOrNull<IncrementalFeedbackView>("IncrementalFeedbackView");
        string feedbackMetrics = feedback is null
            ? "incremental feedback: unavailable"
            : $"incremental feedback active/pool: {feedback.ActiveFeedbackCount}/{feedback.PooledFeedbackCount}  spawned/aggregated/dropped: {feedback.SpawnedFeedbackCount:N0}/{feedback.AggregatedFeedbackCount:N0}/{feedback.DroppedFeedbackCount:N0}";
        MinerSimulationService? miners = GetParent()?.GetNodeOrNull<MinerSimulationService>("MinerSimulation");
        string automationBudget = miners is null
            ? "automation scheduler: unavailable"
            : $"automation scheduler: {miners.Miners.Count:N0} units  max work units/frame: {miners.MaxMiningOperationsPerFrame:N0}";

        _label.Text =
            "PERFORMANCE [F9]\n" +
            $"world: {_world.Profile.Id}  logical: {_world.Profile.LogicalWidth:N0} x {_world.Profile.LogicalHeight:N0} x {_world.Profile.LogicalDepth:N0}\n" +
            $"fps: {Engine.GetFramesPerSecond():0}  managed: {memoryMb:0.0} MB  GC: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"renderer: {renderer}  camera: {_camera.CurrentDistance:0.0}  clearance: {_camera.SurfaceClearance:0.00}  drag: {(_camera.IsManipulating ? "active" : "idle")}\n" +
            $"surface focus: {_camera.SurfaceFocusBlend:0.00}  detail radius: {_view.CurrentStreamingDetailRadius}  {context}\n" +
            $"chunks resident: {_view.VisibleChunkCount}  presented/culled: {_view.PresentedChunkCount}/{_view.CulledChunkCount}  backface/frustum: {_view.BackfaceCulledChunkCount}/{_view.FrustumCulledChunkCount}\n" +
            $"cavity roots total/presented/backface/frustum: {_view.SparseExposureOverlayRootCount:N0}/{_view.PresentedSparseOverlayCount:N0}/{_view.BackfaceCulledSparseOverlayCount:N0}/{_view.FrustumCulledSparseOverlayCount:N0}\n" +
            $"LOD tree batches hidden: {_view.LodHiddenTreeBatchCount:N0}  shadow batches disabled: {_view.LodShadowDisabledBatchCount:N0}  queue: {_view.PendingChunkLoads}  dirty: {_view.PendingChunkRebuilds}\n" +
            $"sparse exposure pending/frontier/builds: {_view.PendingSparseExposureOverlays:N0}/{_view.SparseExposureFrontierCandidateCount:N0}/{_view.SparseExposureOverlayBuilds:N0}  ms last/avg: {_view.LastSparseExposureOverlayBuildMilliseconds:0.00}/{_view.AverageSparseExposureOverlayBuildMilliseconds:0.00}\n" +
            automationBudget + "\n" +
            $"automation presentation queued/suppressed: {_view.AutomationPresentationUpdatesQueued:N0}/{_view.AutomationPresentationUpdatesSuppressed:N0}  deferred/pending chunks: {_view.DeferredAutomationChunkCount:N0}/{_view.PendingVisibleAutomationChunkCount:N0}  flushes: {_view.AutomationPresentationChunkFlushes:N0}\n" +
            $"mining FX pop active/pool/dropped: {_view.ActiveMinePopCount}/{_view.PooledMinePopCount}/{_view.DroppedMinePopCount:N0}  debris active/pool/dropped: {_view.ActiveDebrisBurstCount}/{_view.PooledDebrisBurstCount}/{_view.DroppedDebrisBurstCount:N0}\n" +
            feedbackMetrics + "\n" +
            $"generated sample cache hit/miss: {_world.GeneratedSampleCacheHits:N0}/{_world.GeneratedSampleCacheMisses:N0}  hit rate: {cacheHitPercent:0.0}%\n" +
            $"chunk build ms last/avg: {_view.LastChunkBuildMilliseconds:0.00} / {_view.AverageChunkBuildMilliseconds:0.00}\n" +
            $"chunk builds: {_view.TotalChunkBuilds:N0}  samples: {_view.TotalVoxelCandidatesScanned:N0}\n" +
            $"stream load/unload: {_view.StreamedChunkLoads:N0}/{_view.StreamedChunkUnloads:N0}  macro cells: {_view.MacroInstanceCount:N0} ({_view.MacroBuildMilliseconds:0.0} ms)\n" +
            $"state sparse voxels: {_world.State.SparseVoxelOverrideCount:N0}  modified chunks: {_world.State.ModifiedChunkCount:N0}  exhausted regions: {_world.State.ExhaustedRegionCount:N0}\n" +
            $"mined/remaining: {_world.State.MinedVoxelCount:N0} / {_world.RemainingMineableBlocks:N0}";
    }
}
