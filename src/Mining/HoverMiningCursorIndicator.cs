using System;
using Godot;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space hover-mining cadence indicator. The ring follows the mouse, grows with the currently
/// selected mining footprint and emits a short outward pulse whenever an automatic mining beat fires.
/// </summary>
public partial class HoverMiningCursorIndicator : Control
{
    private const float Tau = MathF.PI * 2.0f;

    private float _radius = 18.0f;
    private float _progress;
    private float _pulse;
    private bool _active;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Size = Vector2.One;
        Visible = false;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!_active || !Visible) return;

        float dt = (float)delta;
        if (_pulse > 0.0f)
        {
            _pulse = MathF.Max(0.0f, _pulse - dt * 3.6f);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!_active) return;

        var baseColor = new Color(1.0f, 0.94f, 0.42f, 0.52f);
        var progressColor = new Color(1.0f, 0.97f, 0.62f, 0.92f);
        DrawArc(Vector2.Zero, _radius, 0.0f, Tau, 64, baseColor, 2.0f, true);

        if (_progress > 0.001f)
        {
            float start = -MathF.PI * 0.5f;
            DrawArc(
                Vector2.Zero,
                _radius,
                start,
                start + Tau * Mathf.Clamp(_progress, 0.0f, 1.0f),
                64,
                progressColor,
                3.0f,
                true);
        }

        if (_pulse > 0.0f)
        {
            float age = 1.0f - _pulse;
            float pulseRadius = _radius * (1.0f + age * 0.48f);
            var pulseColor = new Color(1.0f, 0.96f, 0.54f, 0.78f * _pulse);
            DrawArc(Vector2.Zero, pulseRadius, 0.0f, Tau, 64, pulseColor, 3.0f, true);
        }
    }

    public void SetState(
        bool active,
        Vector2 screenPosition,
        ManualMiningFootprintKind footprint,
        float progress)
    {
        _active = active;
        Visible = active;
        if (!active)
        {
            _progress = 0.0f;
            _pulse = 0.0f;
            return;
        }

        Position = screenPosition;
        _radius = RadiusFor(footprint);
        _progress = Mathf.Clamp(progress, 0.0f, 1.0f);
        QueueRedraw();
    }

    public void Pulse()
    {
        if (!_active) return;
        _pulse = 1.0f;
        QueueRedraw();
    }

    private static float RadiusFor(ManualMiningFootprintKind footprint)
        => footprint switch
        {
            ManualMiningFootprintKind.Plus3 => 28.0f,
            ManualMiningFootprintKind.Square3 => 34.0f,
            ManualMiningFootprintKind.Square10 => 72.0f,
            _ => 18.0f,
        };
}
