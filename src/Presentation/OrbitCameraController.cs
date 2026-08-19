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

    public string ActivePresetName { get; private set; } = MediumPreset.Name;
    public Camera3D Camera => _camera;
    public float CurrentDistance => _distance;
    public float PresetScale => _presetScale;
    public bool IsManipulating => _orbitHeld || _panHeld;

    /// <summary>
    /// 0 means the camera is orbiting the world centre. 1 means the orbit pivot has moved onto the
    /// currently viewed surface, allowing a giant world to be inspected from only a few blocks away
    /// without ever driving the camera through the cube.
    /// </summary>
    public float SurfaceFocusBlend => _surfaceFocusBlend;
    public bool SurfaceFocusEnabled => _surfaceFocusEnabled;
    public float WorldRadius => _worldRadius;

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

        Rotation = new Vector3(_pitch, _yaw, 0.0f);

        // A centre-orbit camera cannot zoom close to a 1000-wide cube: once its distance drops below
        // the world radius the camera is literally inside the terrain. For large worlds we therefore
        // transition the orbit pivot from the centre toward the visible surface as the user zooms in.
        // The physical camera remains outside the cube while its distance to the new surface pivot can
        // become small enough to see individual supplied block meshes.
        _surfaceFocusBlend = CalculateSurfaceFocusBlend(_distance);
        Vector3 radial = Transform.Basis.Z.Normalized();
        Vector3 surfaceOffset = radial * _worldRadius * _surfaceFocusBlend;
        Position = _pan + surfaceOffset;
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
    /// Keeps the same framing language for tiny authored cubes while switching giant diagnostic/world
    /// profiles to a centre-orbit -> surface-inspection zoom. The threshold intentionally mirrors the
    /// rendering architecture: ordinary authored cubes never need this mode.
    /// </summary>
    public void ConfigureWorldExtent(float worldRadius)
    {
        _worldRadius = MathF.Max(1.0f, worldRadius);
        _presetScale = MathF.Max(1.0f, _worldRadius / ReferenceWorldRadius);
        _surfaceFocusEnabled = _worldRadius >= LargeWorldFocusThreshold;

        if (_surfaceFocusEnabled)
        {
            // This is distance from the surface-focused pivot, not distance from the world centre.
            // At full focus the camera's centre distance is worldRadius + this stand-off.
            MinDistance = MathF.Max(8.0f, _worldRadius * 0.012f);
        }
        else
        {
            MinDistance = 16.0f * _presetScale;
        }

        MaxDistance = 65.0f * _presetScale;
        if (_camera is not null)
        {
            _camera.Far = MathF.Max(300.0f, _worldRadius * 6.0f);
            _camera.Near = _surfaceFocusEnabled
                ? MathF.Max(0.03f, _worldRadius * 0.00004f)
                : MathF.Max(0.05f, _presetScale * 0.015f);
        }
    }

    public void ApplyPreset(CameraPreset preset, bool immediate = false)
    {
        ActivePresetName = preset.Name;
        _targetYaw = Mathf.DegToRad(preset.YawDegrees);
        _targetPitch = Mathf.DegToRad(preset.PitchDegrees);

        if (_surfaceFocusEnabled && preset.Name == NearPreset.Name)
        {
            // Near on a huge world means inspecting its surface, not placing a centre-orbit camera
            // approximately at the surface radius where it clips straight through the terrain.
            _targetDistance = MathF.Max(MinDistance * 2.0f, _worldRadius * 0.035f);
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
            _targetDistance = Mathf.Clamp(_targetDistance * EffectiveZoomStep(), MinDistance, MaxDistance);
            ActivePresetName = "Custom";
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
        {
            _targetDistance = Mathf.Clamp(_targetDistance / EffectiveZoomStep(), MinDistance, MaxDistance);
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
            float inspectionScale = PanSensitivity * MathF.Max(0.35f, _targetDistance / ReferenceWorldRadius);
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

    private float EffectiveZoomStep()
    {
        if (!_surfaceFocusEnabled)
        {
            return ZoomStep;
        }

        // Large multiplicative steps are extremely coarse while traversing a thousand-world-unit
        // radius. Slow the wheel through the centre->surface transition, then permit slightly faster
        // close inspection once one wheel notch corresponds to only a few world units.
        if (_targetDistance > _worldRadius * 1.20f) return 0.92f;
        if (_targetDistance > _worldRadius * 0.30f) return 0.945f;
        return 0.90f;
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
}
