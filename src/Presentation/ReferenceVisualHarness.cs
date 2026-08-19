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
        var canvas = new CanvasLayer { Name = "ReferenceVisualHarnessCanvas" };
        AddChild(canvas);

        var panel = new PanelContainer
        {
            OffsetLeft = 16.0f,
            OffsetTop = 16.0f,
            OffsetRight = 306.0f,
            OffsetBottom = 174.0f,
        };
        canvas.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);
        margin.AddChild(column);

        column.AddChild(new Label { Text = "REFERENCE VISUAL HARNESS" });

        _status = new Label { Text = "Camera: Medium" };
        column.AddChild(_status);

        var presets = new HBoxContainer();
        presets.AddThemeConstantOverride("separation", 4);
        column.AddChild(presets);

        AddPresetButton(presets, "Far [1]", OrbitCameraController.FarPreset);
        AddPresetButton(presets, "Medium [2]", OrbitCameraController.MediumPreset);
        AddPresetButton(presets, "Near [3]", OrbitCameraController.NearPreset);

        var recenter = new Button { Text = "Recenter [F]" };
        recenter.Pressed += () => _camera.Recenter();
        column.AddChild(recenter);

        column.AddChild(new Label
        {
            Text = "LMB: mine / UI   RMB drag: orbit   MMB drag: pan   Wheel: zoom",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
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
