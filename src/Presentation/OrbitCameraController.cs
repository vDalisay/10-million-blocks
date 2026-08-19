using System;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class OrbitCameraController : Node3D
{
    public readonly record struct CameraPreset(string Name, float YawDegrees, float PitchDegrees, float Distance);

    public static readonly CameraPreset FarPreset = new("Far", -38.0f, -24.0f, 50.0f);
    public static readonly CameraPreset MediumPreset = new("Medium", -38.0f, -27.0f, 37.0f);
    public static readonly CameraPreset NearPreset = new("Near", -42.0f, -30.0f, 24.0f);

    private const float ReferenceWorldRadius = 24.0f;

    [Export] public float OrbitSensitivity { get; set; } = 0.22f;
    [Export] public float PanSensitivity { get; set; } = 0.025f;
    [Export] public float ZoomStep { get; set; } = 0.88f;
    [Export] public float MinDistance { get; set; } = 16.0f;
    [Export] public float MaxDistance { get; set; } = 65.0f;
    [Export] public float Smoothing { get; set; } = 10.0f;

    private Camera3D _camera = null!;
    private bool _orbitHeld;
    private bool _panHeld;
    private Vector2 _pressPosition;
    private bool _dragThresholdPassed;

    private float _yaw;
    private float _pitch;
    private float _distance;
    private Vector3 _pan;

    private float _targetYaw;
    private float _targetPitch;
    private float _targetDistance;
    private Vector3 _targetPan;
    private float _presetScale = 1.0f;

    public string ActivePresetName { get; private set; } = MediumPreset.Name;
    public Camera3D Camera => _camera;
    public float CurrentDistance => _distance;
    public float PresetScale => _presetScale;

    public override void _Ready()
    {
        _camera = new Camera3D
        {
            Name = "ReferenceCamera",
            Current = true,
            Fov = 55.0f,
            Near = 0.05f,
            Far = 300.0f,
        };
        AddChild(_camera);

        ApplyPreset(MediumPreset, immediate: true);
    }

    public override void _Process(double delta)
    {
        float blend = 1.0f - MathF.Exp(-Smoothing * (float)delta);
        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, blend);
        _pitch = Mathf.Lerp(_pitch, _targetPitch, blend);
        _distance = Mathf.Lerp(_distance, _targetDistance, blend);
        _pan = _pan.Lerp(_targetPan, blend);

        Position = _pan;
        Rotation = new Vector3(_pitch, _yaw, 0.0f);
        _camera.Position = new Vector3(0.0f, 0.0f, _distance);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            HandleMouseButton(button);
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            HandleMouseMotion(motion);
        }
    }

    /// <summary>
    /// Keeps the same framing language for tiny authored cubes and million-scale logical profiles.
    /// Distances, pan range and far clip scale with the rendered world radius rather than being hard coded.
    /// </summary>
    public void ConfigureWorldExtent(float worldRadius)
    {
        _presetScale = MathF.Max(1.0f, worldRadius / ReferenceWorldRadius);
        MinDistance = 16.0f * _presetScale;
        MaxDistance = 65.0f * _presetScale;
        if (_camera is not null)
        {
            _camera.Far = MathF.Max(300.0f, worldRadius * 6.0f);
            _camera.Near = MathF.Max(0.05f, _presetScale * 0.015f);
        }
    }

    public void ApplyPreset(CameraPreset preset, bool immediate = false)
    {
        ActivePresetName = preset.Name;
        _targetYaw = Mathf.DegToRad(preset.YawDegrees);
        _targetPitch = Mathf.DegToRad(preset.PitchDegrees);
        _targetDistance = Mathf.Clamp(preset.Distance * _presetScale, MinDistance, MaxDistance);
        _targetPan = Vector3.Zero;

        if (!immediate)
        {
            return;
        }

        _yaw = _targetYaw;
        _pitch = _targetPitch;
        _distance = _targetDistance;
        _pan = _targetPan;
    }

    public void Recenter()
    {
        _targetPan = Vector3.Zero;
    }

    public void AddOrbitDegrees(float yawDegrees, float pitchDegrees = 0.0f)
    {
        _targetYaw += Mathf.DegToRad(yawDegrees);
        _targetPitch = Mathf.Clamp(
            _targetPitch + Mathf.DegToRad(pitchDegrees),
            Mathf.DegToRad(-78.0f),
            Mathf.DegToRad(78.0f));
        ActivePresetName = "Benchmark";
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
        {
            _targetDistance = Mathf.Clamp(_targetDistance * ZoomStep, MinDistance, MaxDistance);
            ActivePresetName = "Custom";
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
        {
            _targetDistance = Mathf.Clamp(_targetDistance / ZoomStep, MinDistance, MaxDistance);
            ActivePresetName = "Custom";
            GetViewport().SetInputAsHandled();
            return;
        }

        // LMB is intentionally not handled here. It belongs exclusively to mining and UI.
        if (button.ButtonIndex == MouseButton.Right)
        {
            _orbitHeld = button.Pressed;
            if (button.Pressed)
            {
                _pressPosition = button.Position;
                _dragThresholdPassed = false;
            }
            else
            {
                _dragThresholdPassed = false;
            }
            return;
        }

        if (button.ButtonIndex == MouseButton.Middle)
        {
            _panHeld = button.Pressed;
            if (button.Pressed)
            {
                _pressPosition = button.Position;
                _dragThresholdPassed = false;
            }
            else
            {
                _dragThresholdPassed = false;
            }
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (!_orbitHeld && !_panHeld)
        {
            return;
        }

        if (!_dragThresholdPassed && motion.Position.DistanceTo(_pressPosition) >= 4.0f)
        {
            _dragThresholdPassed = true;
        }

        if (!_dragThresholdPassed)
        {
            return;
        }

        if (_orbitHeld)
        {
            _targetYaw -= Mathf.DegToRad(motion.Relative.X * OrbitSensitivity);
            _targetPitch -= Mathf.DegToRad(motion.Relative.Y * OrbitSensitivity);
            _targetPitch = Mathf.Clamp(_targetPitch, Mathf.DegToRad(-78.0f), Mathf.DegToRad(78.0f));
            ActivePresetName = "Custom";
        }
        else if (_panHeld)
        {
            Vector3 right = _camera.GlobalTransform.Basis.X;
            Vector3 up = _camera.GlobalTransform.Basis.Y;
            float scale = PanSensitivity * MathF.Max(0.5f, _targetDistance / (MediumPreset.Distance * _presetScale));
            scale *= _presetScale;
            _targetPan += (-right * motion.Relative.X + up * motion.Relative.Y) * scale;
            float panLimit = 8.0f * _presetScale;
            if (_targetPan.LengthSquared() > panLimit * panLimit)
            {
                _targetPan = _targetPan.Normalized() * panLimit;
            }
            ActivePresetName = "Custom";
        }

        GetViewport().SetInputAsHandled();
    }
}
