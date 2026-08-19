using Godot;

namespace TenMillionBlocks.Presentation;

public partial class ReferenceVisualHarness : Node
{
    private OrbitCameraController _camera = null!;
    private Label _status = null!;

    public void Initialize(OrbitCameraController camera)
    {
        _camera = camera;
    }

    public override void _Ready()
    {
        var canvas = new CanvasLayer { Name = "ReferenceVisualHarnessCanvas", Layer = 20 };
        AddChild(canvas);

        var panel = new PanelContainer
        {
            OffsetLeft = 16.0f,
            OffsetTop = 16.0f,
            OffsetRight = 442.0f,
            OffsetBottom = 64.0f,
            TooltipText = "LMB mine / UI   RMB drag orbit   MMB drag pan   Wheel zoom   [H] HUD details",
        };
        canvas.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        panel.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        margin.AddChild(row);

        _status = new Label
        {
            Text = "Camera: Medium",
            CustomMinimumSize = new Vector2(108.0f, 0.0f),
        };
        row.AddChild(_status);

        AddPresetButton(row, "Far [1]", OrbitCameraController.FarPreset);
        AddPresetButton(row, "Med [2]", OrbitCameraController.MediumPreset);
        AddPresetButton(row, "Near [3]", OrbitCameraController.NearPreset);

        var recenter = new Button { Text = "Center [F]" };
        recenter.Pressed += () => _camera.Recenter();
        row.AddChild(recenter);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_status is not null && _camera is not null)
        {
            _status.Text = $"Camera: {_camera.ActivePresetName}";
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || _camera is null)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Key1:
                _camera.ApplyPreset(OrbitCameraController.FarPreset);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key2:
                _camera.ApplyPreset(OrbitCameraController.MediumPreset);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key3:
                _camera.ApplyPreset(OrbitCameraController.NearPreset);
                GetViewport().SetInputAsHandled();
                break;
            case Key.F:
                _camera.Recenter();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void AddPresetButton(Control parent, string text, OrbitCameraController.CameraPreset preset)
    {
        var button = new Button { Text = text };
        button.Pressed += () => _camera.ApplyPreset(preset);
        parent.AddChild(button);
    }
}
