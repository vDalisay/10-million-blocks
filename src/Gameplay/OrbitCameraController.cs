using System;
using Godot;

namespace TenMillionBlocks.Gameplay;

public sealed partial class OrbitCameraController : Node3D
{
    private const float MinDistance = 3.2f;
    private const float MaxDistance = 95.0f;

    private Camera3D? _camera;
    private bool _orbiting;
    private float _yaw = 38.0f;
    private float _pitch = -24.0f;
    private float _distance = 12.0f;
    private float _targetDistance = 12.0f;
    private float _idleSeconds;

    public Camera3D Camera => _camera!;

    public override void _Ready()
    {
        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Fov = 46.0f,
            Position = new Vector3(0, 0, _distance),
        };

        AddChild(_camera);
        RotationDegrees = new Vector3(_pitch, _yaw, 0.0f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mouseButton
                when mouseButton.ButtonIndex == MouseButton.Right:
                _orbiting = mouseButton.Pressed;
                _idleSeconds = 0.0f;
                Input.MouseMode = _orbiting ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when _orbiting:
                _yaw -= mouseMotion.Relative.X * 0.22f;
                _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * 0.18f, -78.0f, 78.0f);
                _idleSeconds = 0.0f;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelUp:
                _targetDistance = Mathf.Clamp(_targetDistance * 0.88f, MinDistance, MaxDistance);
                _idleSeconds = 0.0f;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton wheel when wheel.Pressed && wheel.ButtonIndex == MouseButton.WheelDown:
                _targetDistance = Mathf.Clamp(_targetDistance * 1.14f, MinDistance, MaxDistance);
                _idleSeconds = 0.0f;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _idleSeconds += dt;

        if (!_orbiting && _idleSeconds > 3.5f)
        {
            _yaw += dt * 2.2f;
        }

        _distance = Mathf.Lerp(_distance, _targetDistance, 1.0f - MathF.Exp(-8.0f * dt));
        RotationDegrees = new Vector3(_pitch, _yaw, 0.0f);

        if (_camera is not null)
        {
            _camera.Position = new Vector3(0, 0, _distance);
        }
    }

    public void Frame(Aabb bounds)
    {
        Position = bounds.GetCenter();
        float largestAxis = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
        _targetDistance = Mathf.Clamp(largestAxis * 1.85f + 3.0f, MinDistance, MaxDistance);
        _distance = _targetDistance;
        _idleSeconds = 0.0f;
    }
}
