using System;
using Godot;
using TenMillionBlocks.Presentation;
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

    public bool IsVisible => _panel is not null && _panel.Visible;

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
            OffsetLeft = -470.0f,
            OffsetTop = 16.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 326.0f,
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
        string renderer = _view.FullSurfaceRenderer
            ? "real-block full surface"
            : _view.StreamingEnabled ? "macro + streamed detail" : "eager real blocks";
        string context = _view.FullSurfaceRenderer
            ? "macro: disabled (real blocks only)"
            : $"macro: {(_view.MacroVisible ? "visible" : "hidden")} opacity {_view.MacroOpacity:0.00}";

        _label.Text =
            "PERFORMANCE [F9]\n" +
            $"world: {_world.Profile.Id}  logical: {_world.Profile.LogicalWidth:N0} x {_world.Profile.LogicalHeight:N0} x {_world.Profile.LogicalDepth:N0}\n" +
            $"fps: {Engine.GetFramesPerSecond():0}  managed: {memoryMb:0.0} MB  GC: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"renderer: {renderer}  camera: {_camera.CurrentDistance:0.0}  clearance: {_camera.SurfaceClearance:0.00}  drag: {(_camera.IsManipulating ? "active" : "idle")}\n" +
            $"surface focus: {_camera.SurfaceFocusBlend:0.00}  detail radius: {_view.CurrentStreamingDetailRadius}  {context}\n" +
            $"chunks loaded: {_view.VisibleChunkCount}  load queue: {_view.PendingChunkLoads}  dirty: {_view.PendingChunkRebuilds}\n" +
            $"chunk build ms last/avg: {_view.LastChunkBuildMilliseconds:0.00} / {_view.AverageChunkBuildMilliseconds:0.00}\n" +
            $"chunk builds: {_view.TotalChunkBuilds:N0}  samples: {_view.TotalVoxelCandidatesScanned:N0}\n" +
            $"stream load/unload: {_view.StreamedChunkLoads:N0}/{_view.StreamedChunkUnloads:N0}  macro cells: {_view.MacroInstanceCount:N0} ({_view.MacroBuildMilliseconds:0.0} ms)\n" +
            $"state sparse voxels: {_world.State.SparseVoxelOverrideCount:N0}  modified chunks: {_world.State.ModifiedChunkCount:N0}  exhausted regions: {_world.State.ExhaustedRegionCount:N0}\n" +
            $"mined/remaining: {_world.State.MinedVoxelCount:N0} / {_world.RemainingMineableBlocks:N0}";
    }
}
