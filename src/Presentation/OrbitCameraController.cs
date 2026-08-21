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
    private const float LargeWorldFocusThreshold = ReferenceWorldRadius * 4.0f;
    private const double IdleOrbitDelaySeconds = 30.0;
    private const float IdleOrbitDegreesPerSecond = 1.6f;

    [Export] public float OrbitSensitivity { get; set; } = 0.22f;
    [Export] public float PanSensitivity { get; set; } = 0.025f;
    [Export] public float ZoomStep { get; set; } = 0.92f;
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
    private float _worldRadius = ReferenceWorldRadius;
    private bool _surfaceFocusEnabled;
    private float _surfaceFocusBlend;
    private float _surfaceClearance;
    private bool _forceFarOnNextPreset;
    private double _mouseIdleSeconds;

    public string ActivePresetName { get; private set; } = MediumPreset.Name;
    public Camera3D Camera => _camera;
    public float CurrentDistance => _distance;
    public float PresetScale => _presetScale;
    public bool IsManipulating => _orbitHeld || _panHeld;

    /// <summary>
    /// 0 means the camera is orbiting the world centre. 1 means the orbit pivot has moved onto the
    /// currently viewed surface, allowing a giant world to be inspected from only a few blocks away.
    /// </summary>
    public float SurfaceFocusBlend => _surfaceFocusBlend;
    public bool SurfaceFocusEnabled => _surfaceFocusEnabled;
    public float WorldRadius => _worldRadius;
    public float SurfaceClearance => _surfaceClearance;

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

    public override void _Input(InputEvent @event)
    {
        // Count mouse activity even when a UI Control later consumes the event. Keyboard activity does
        // not cancel the ambient showcase orbit because this mode is specifically about mouse/click idle.
        if (@event is InputEventMouseMotion motion)
        {
            if (motion.Relative.LengthSquared() > 0.0001f) ResetIdleOrbit();
            return;
        }

        if (@event is InputEventMouseButton)
        {
            ResetIdleOrbit();
        }
    }

    public override void _Process(double delta)
    {
        double safeDelta = Math.Max(0.0, delta);
        if (!_orbitHeld && !_panHeld)
        {
            _mouseIdleSeconds += safeDelta;
            if (_mouseIdleSeconds >= IdleOrbitDelaySeconds)
            {
                // Match the direction produced by dragging the mouse to the right: slowly orbit right.
                _targetYaw -= Mathf.DegToRad(IdleOrbitDegreesPerSecond) * (float)safeDelta;
            }
        }

        float blend = 1.0f - MathF.Exp(-Smoothing * (float)safeDelta);
        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, blend);
        _pitch = Mathf.Lerp(_pitch, _targetPitch, blend);
        _distance = Mathf.Lerp(_distance, _targetDistance, blend);
        _pan = _pan.Lerp(_targetPan, blend);

        Rotation = new Vector3(_pitch, _yaw, 0.0f);

        // A centre-orbit camera cannot safely use the cube half-extent as though it were a sphere:
        // along a diagonal the actual cube surface is much farther from the centre. Compute the exact
        // support distance for the current view, then enforce an expanded cube as a hard final-position
        // barrier. The final-position check matters after panning because the pivot itself may already
        // be outside one face while the requested camera point has rotated back through the cube.
        _surfaceFocusBlend = CalculateSurfaceFocusBlend(_distance);
        Vector3 radial = Transform.Basis.Z.Normalized();
        float supportRadius = SurfaceRadiusAlong(radial);
        Vector3 pivot = _pan + radial * supportRadius * _surfaceFocusBlend;
        Position = pivot;

        float localDistance = _distance;
        if (_surfaceFocusEnabled)
        {
            float safeExtent = _worldRadius + MinimumSurfaceClearance();
            Vector3 requestedPosition = pivot + radial * localDistance;
            if (IsInsideCube(requestedPosition, safeExtent))
            {
                localDistance += DistanceToExitCubeFromInside(requestedPosition, radial, safeExtent);
            }
        }

        _camera.Position = new Vector3(0.0f, 0.0f, localDistance);
        _surfaceClearance = EstimateCubeClearance(_camera.GlobalPosition);
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
    /// Keeps the same framing language for tiny authored cubes while switching giant diagnostic/world
    /// profiles to a centre-orbit -> surface-inspection zoom. The threshold intentionally mirrors the
    /// rendering architecture: ordinary authored cubes never need this mode.
    /// </summary>
    public void ConfigureWorldExtent(float worldRadius)
    {
        _worldRadius = MathF.Max(1.0f, worldRadius);
        _presetScale = MathF.Max(1.0f, _worldRadius / ReferenceWorldRadius);
        _surfaceFocusEnabled = _worldRadius >= LargeWorldFocusThreshold;
        _forceFarOnNextPreset = true;
        ResetIdleOrbit();

        if (_surfaceFocusEnabled)
        {
            // Full surface focus interprets this as stand-off from the inspected face. Keep it low
            // enough for individual 1-unit blocks to be readable, but never allow zero/negative range.
            MinDistance = MathF.Max(1.5f, MathF.Min(4.0f, _worldRadius * 0.004f));
        }
        else
        {
            MinDistance = 16.0f * _presetScale;
        }

        MaxDistance = 65.0f * _presetScale;
        if (_camera is not null)
        {
            _camera.Far = MathF.Max(300.0f, _worldRadius * 8.0f);
            _camera.Near = _surfaceFocusEnabled
                ? 0.03f
                : MathF.Max(0.05f, _presetScale * 0.015f);
        }
    }

    public void ApplyPreset(CameraPreset preset, bool immediate = false)
    {
        // GameRoot applies an authored default immediately after configuring each world/replay. Override
        // that first request so every fresh world view consistently begins with the complete Far framing.
        if (_forceFarOnNextPreset)
        {
            preset = FarPreset;
            _forceFarOnNextPreset = false;
        }

        ResetIdleOrbit();
        ActivePresetName = preset.Name;
        _targetYaw = Mathf.DegToRad(preset.YawDegrees);
        _targetPitch = Mathf.DegToRad(preset.PitchDegrees);

        if (_surfaceFocusEnabled && preset.Name == NearPreset.Name)
        {
            // Near on a huge world is an actual close inspection stand-off. The cube barrier in
            // _Process guarantees this can never put the camera inside the world, including diagonals.
            _targetDistance = MathF.Max(MinDistance * 2.0f, 5.0f);
        }
        else
        {
            _targetDistance = Mathf.Clamp(preset.Distance * _presetScale, MinDistance, MaxDistance);
        }

        _targetPan = Vector3.Zero;

        if (!immediate)
        {
            return;
        }

        _yaw = _targetYaw;
        _pitch = _targetPitch;
        _distance = _targetDistance;
        _pan = _targetPan;
        _surfaceFocusBlend = CalculateSurfaceFocusBlend(_distance);
    }

    public void Recenter()
    {
        _targetPan = Vector3.Zero;
        ResetIdleOrbit();
    }

    public void AddOrbitDegrees(float yawDegrees, float pitchDegrees = 0.0f)
    {
        _targetYaw += Mathf.DegToRad(yawDegrees);
        _targetPitch = Mathf.Clamp(
            _targetPitch + Mathf.DegToRad(pitchDegrees),
            Mathf.DegToRad(-78.0f),
            Mathf.DegToRad(78.0f));
        ActivePresetName = "Benchmark";
        ResetIdleOrbit();
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
        {
            ZoomByWheel(zoomIn: true);
            ActivePresetName = "Custom";
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
        {
            ZoomByWheel(zoomIn: false);
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

            float centreOrbitScale = PanSensitivity
                * MathF.Max(0.5f, _targetDistance / (MediumPreset.Distance * _presetScale))
                * _presetScale;
            float inspectionScale = PanSensitivity * MathF.Max(0.20f, _targetDistance / ReferenceWorldRadius);
            float scale = Mathf.Lerp(centreOrbitScale, inspectionScale, _surfaceFocusBlend);

            _targetPan += (-right * motion.Relative.X + up * motion.Relative.Y) * scale;
            float panLimit = _surfaceFocusEnabled
                ? MathF.Max(8.0f * _presetScale, _worldRadius * 0.4f)
                : 8.0f * _presetScale;
            if (_targetPan.LengthSquared() > panLimit * panLimit)
            {
                _targetPan = _targetPan.Normalized() * panLimit;
            }
            ActivePresetName = "Custom";
        }

        GetViewport().SetInputAsHandled();
    }

    private void ZoomByWheel(bool zoomIn)
    {
        if (!_surfaceFocusEnabled)
        {
            float next = zoomIn ? _targetDistance * ZoomStep : _targetDistance / ZoomStep;
            _targetDistance = Mathf.Clamp(next, MinDistance, MaxDistance);
            return;
        }

        // Large worlds use additive, distance-adaptive wheel motion. Multiplying a 500-1000 unit
        // distance by 0.94 makes one notch jump tens of blocks; near the surface each notch now shrinks
        // naturally to fractions of a block instead. This is deliberately monotonic and symmetric.
        float delta = LargeWorldZoomDelta(_targetDistance);
        _targetDistance = Mathf.Clamp(
            _targetDistance + (zoomIn ? -delta : delta),
            MinDistance,
            MaxDistance);
    }

    private float LargeWorldZoomDelta(float distance)
    {
        float transitionStart = _worldRadius * 1.20f;
        float transitionEnd = _worldRadius * 0.32f;

        if (distance > transitionStart)
        {
            return MathF.Max(_worldRadius * 0.012f, distance * 0.022f);
        }

        if (distance > transitionEnd)
        {
            return MathF.Max(_worldRadius * 0.004f, distance * 0.012f);
        }

        float closeMaximum = MathF.Max(0.75f, _worldRadius * 0.008f);
        return Mathf.Clamp(distance * 0.08f, 0.20f, closeMaximum);
    }

    private float CalculateSurfaceFocusBlend(float distance)
    {
        if (!_surfaceFocusEnabled)
        {
            return 0.0f;
        }

        float transitionStart = _worldRadius * 1.20f;
        float transitionEnd = _worldRadius * 0.32f;
        if (distance >= transitionStart) return 0.0f;
        if (distance <= transitionEnd) return 1.0f;

        float t = (transitionStart - distance) / MathF.Max(0.001f, transitionStart - transitionEnd);
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private float SurfaceRadiusAlong(Vector3 radial)
    {
        float maxAbs = MathF.Max(MathF.Abs(radial.X), MathF.Max(MathF.Abs(radial.Y), MathF.Abs(radial.Z)));
        return _worldRadius / MathF.Max(0.0001f, maxAbs);
    }

    private float MinimumSurfaceClearance()
        => MathF.Max(0.45f, MathF.Min(1.5f, MinDistance * 0.30f));

    private static bool IsInsideCube(Vector3 point, float halfExtent)
        => MathF.Abs(point.X) < halfExtent
            && MathF.Abs(point.Y) < halfExtent
            && MathF.Abs(point.Z) < halfExtent;

    private static float DistanceToExitCubeFromInside(Vector3 origin, Vector3 direction, float halfExtent)
    {
        float best = float.PositiveInfinity;
        ConsiderAxis(origin.X, direction.X, halfExtent, ref best);
        ConsiderAxis(origin.Y, direction.Y, halfExtent, ref best);
        ConsiderAxis(origin.Z, direction.Z, halfExtent, ref best);
        return float.IsPositiveInfinity(best) ? 0.0f : MathF.Max(0.0f, best);
    }

    private static void ConsiderAxis(float origin, float direction, float halfExtent, ref float best)
    {
        if (MathF.Abs(direction) < 0.00001f) return;
        float boundary = direction > 0.0f ? halfExtent : -halfExtent;
        float distance = (boundary - origin) / direction;
        if (distance >= 0.0f && distance < best)
        {
            best = distance;
        }
    }

    private float EstimateCubeClearance(Vector3 worldPosition)
    {
        float outside = MathF.Max(MathF.Abs(worldPosition.X), MathF.Max(MathF.Abs(worldPosition.Y), MathF.Abs(worldPosition.Z)));
        return MathF.Max(0.0f, outside - _worldRadius);
    }

    private void ResetIdleOrbit()
    {
        _mouseIdleSeconds = 0.0;
    }
}
