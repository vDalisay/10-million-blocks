using System;
using System.IO;
using Godot;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Presentation;

/// <summary>
/// Debug-only fixed-camera/look harness used for like-for-like art comparison. It deliberately changes
/// presentation only: world generation, mining state and progression are untouched. The capture name
/// records world/version, camera preset and look preset so screenshots can be compared without relying
/// on memory or hand-written notes.
/// </summary>
public partial class ReferenceVisualHarness : Node
{
    private OrbitCameraController _camera = null!;
    private Label _status = null!;
    private Godot.Environment? _environment;
    private DirectionalLight3D? _keyLight;
    private DirectionalLight3D? _fillLight;
    private string _visualPreset = "Final";

    public void Initialize(OrbitCameraController camera)
    {
        _camera = camera;
    }

    public override void _Ready()
    {
        if (!OS.IsDebugBuild())
        {
            QueueFree();
            return;
        }

        ResolvePresentationNodes();
        ApplyVisualPreset("Final");

        var canvas = new CanvasLayer { Name = "ReferenceVisualHarnessCanvas", Layer = 20 };
        AddChild(canvas);

        var panel = new PanelContainer
        {
            OffsetLeft = 16.0f,
            OffsetTop = 16.0f,
            OffsetRight = 724.0f,
            OffsetBottom = 108.0f,
            TooltipText = "Reference A/B harness. Camera [1-3], look [4-7], capture [F6]. RMB orbit, MMB pan, wheel zoom.",
        };
        canvas.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        margin.AddChild(column);

        var cameraRow = new HBoxContainer();
        cameraRow.AddThemeConstantOverride("separation", 4);
        column.AddChild(cameraRow);

        _status = new Label
        {
            Text = "Camera: Medium · Look: Final",
            CustomMinimumSize = new Vector2(232.0f, 0.0f),
        };
        cameraRow.AddChild(_status);

        AddCameraPresetButton(cameraRow, "Far [1]", OrbitCameraController.FarPreset);
        AddCameraPresetButton(cameraRow, "Med [2]", OrbitCameraController.MediumPreset);
        AddCameraPresetButton(cameraRow, "Near [3]", OrbitCameraController.NearPreset);

        var recenter = new Button { Text = "Center [F]" };
        recenter.Pressed += () => _camera.Recenter();
        cameraRow.AddChild(recenter);

        var lookRow = new HBoxContainer();
        lookRow.AddThemeConstantOverride("separation", 4);
        column.AddChild(lookRow);
        lookRow.AddChild(new Label
        {
            Text = "A/B:",
            CustomMinimumSize = new Vector2(44.0f, 0.0f),
        });

        AddLookPresetButton(lookRow, "Raw [4]", "Raw");
        AddLookPresetButton(lookRow, "AO [5]", "AO");
        AddLookPresetButton(lookRow, "Grade [6]", "Grade");
        AddLookPresetButton(lookRow, "Final [7]", "Final");

        var capture = new Button
        {
            Text = "Capture [F6]",
            TooltipText = "Save a PNG under user://reference_captures with world/version/camera/look metadata.",
        };
        capture.Pressed += CaptureScreenshot;
        lookRow.AddChild(capture);
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_status is not null && _camera is not null)
        {
            _status.Text = $"Camera: {_camera.ActivePresetName} · Look: {_visualPreset}";
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
            case Key.Key4:
                ApplyVisualPreset("Raw");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key5:
                ApplyVisualPreset("AO");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key6:
                ApplyVisualPreset("Grade");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key7:
                ApplyVisualPreset("Final");
                GetViewport().SetInputAsHandled();
                break;
            case Key.F6:
                CaptureScreenshot();
                GetViewport().SetInputAsHandled();
                break;
            case Key.F:
                _camera.Recenter();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void ResolvePresentationNodes()
    {
        Node? root = GetParent();
        _environment = root?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        _keyLight = root?.GetNodeOrNull<DirectionalLight3D>("KeyLight");
        _fillLight = root?.GetNodeOrNull<DirectionalLight3D>("FillLight");
    }

    private void ApplyVisualPreset(string preset)
    {
        if (_environment is null) ResolvePresentationNodes();
        if (_environment is null) return;

        _visualPreset = preset;

        // Keep the actual authored key/fill arrangement fixed between presets. A/B comparisons isolate
        // the post/ambient contribution instead of accidentally comparing two different light rigs.
        if (_keyLight is not null) _keyLight.LightEnergy = 1.05f;
        if (_fillLight is not null) _fillLight.LightEnergy = 0.45f;
        _environment.AmbientLightEnergy = 0.42f;
        _environment.TonemapWhite = 2.0f;

        switch (preset)
        {
            case "Raw":
                _environment.SsaoEnabled = false;
                _environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
                _environment.GlowEnabled = false;
                break;
            case "AO":
                _environment.SsaoEnabled = true;
                _environment.SsaoRadius = 1.6f;
                _environment.SsaoIntensity = 2.6f;
                _environment.SsaoPower = 1.4f;
                _environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
                _environment.GlowEnabled = false;
                break;
            case "Grade":
                _environment.SsaoEnabled = false;
                _environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                _environment.GlowEnabled = false;
                break;
            default:
                _visualPreset = "Final";
                _environment.SsaoEnabled = true;
                _environment.SsaoRadius = 1.6f;
                _environment.SsaoIntensity = 2.6f;
                _environment.SsaoPower = 1.4f;
                _environment.TonemapMode = Godot.Environment.ToneMapper.Filmic;
                _environment.GlowEnabled = true;
                _environment.GlowIntensity = 0.18f;
                break;
        }
    }

    private void CaptureScreenshot()
    {
        Image image = GetViewport().GetTexture().GetImage();
        if (image is null || image.IsEmpty())
        {
            GD.PushWarning("Reference capture skipped because the viewport image is empty.");
            return;
        }

        WorldView? worldView = FindDescendant<WorldView>(GetParent());
        string worldId = worldView?.WorldForAuthoring.Profile.Id ?? "unknown_world";
        int worldVersion = worldView?.WorldForAuthoring.Profile.WorldVersion ?? 0;
        string camera = SafeFilePart(_camera.ActivePresetName);
        string look = SafeFilePart(_visualPreset);
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        string fileName = $"{SafeFilePart(worldId)}_v{worldVersion}_{camera}_{look}_{timestamp}.png";

        const string relativeDirectory = "user://reference_captures";
        string absoluteDirectory = ProjectSettings.GlobalizePath(relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);
        string absolutePath = Path.Combine(absoluteDirectory, fileName);
        Error result = image.SavePng(absolutePath);
        if (result != Error.Ok)
        {
            GD.PushError($"Reference capture failed ({result}): {absolutePath}");
            return;
        }

        GD.Print($"Reference capture saved: {absolutePath}");
        if (_status is not null) _status.Text = $"Saved {fileName}";
    }

    private void AddCameraPresetButton(Control parent, string text, OrbitCameraController.CameraPreset preset)
    {
        var button = new Button { Text = text };
        button.Pressed += () => _camera.ApplyPreset(preset);
        parent.AddChild(button);
    }

    private void AddLookPresetButton(Control parent, string text, string preset)
    {
        var button = new Button { Text = text };
        button.Pressed += () => ApplyVisualPreset(preset);
        parent.AddChild(button);
    }

    private static T? FindDescendant<T>(Node? node) where T : Node
    {
        if (node is null) return null;
        foreach (Node child in node.GetChildren())
        {
            if (child is T match) return match;
            T? nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static string SafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace(' ', '_').ToLowerInvariant();
    }
}
