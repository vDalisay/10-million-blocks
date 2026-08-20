using System;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class OrbitCameraController
{
    /// <summary>
    /// Reframes the camera around a world-space point without teleporting through the cube. Used by
    /// automation attention UI so repeated clicks can cycle between stopped machines.
    /// </summary>
    public void FocusWorldPoint(Vector3 worldPoint)
    {
        Vector3 radial = worldPoint.LengthSquared() > 0.001f
            ? worldPoint.Normalized()
            : Transform.Basis.Z.Normalized();

        _targetYaw = MathF.Atan2(radial.X, radial.Z);
        _targetPitch = Mathf.Clamp(
            -MathF.Asin(Mathf.Clamp(radial.Y, -1.0f, 1.0f)),
            Mathf.DegToRad(-78.0f),
            Mathf.DegToRad(78.0f));

        if (_surfaceFocusEnabled)
        {
            float support = SurfaceRadiusAlong(radial);
            Vector3 surfacePoint = radial * support;
            _targetPan = worldPoint - surfacePoint;
            float panLimit = MathF.Max(8.0f * _presetScale, _worldRadius * 0.4f);
            if (_targetPan.LengthSquared() > panLimit * panLimit)
            {
                _targetPan = _targetPan.Normalized() * panLimit;
            }
            _targetDistance = Mathf.Clamp(MathF.Max(MinDistance * 2.0f, 6.0f), MinDistance, MaxDistance);
        }
        else
        {
            _targetPan = worldPoint;
            float panLimit = 8.0f * _presetScale;
            if (_targetPan.LengthSquared() > panLimit * panLimit)
            {
                _targetPan = _targetPan.Normalized() * panLimit;
            }
            _targetDistance = Mathf.Clamp(NearPreset.Distance * _presetScale, MinDistance, MaxDistance);
        }

        ActivePresetName = "Automation";
    }
}
