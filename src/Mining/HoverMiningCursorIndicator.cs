using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space cursor feedback. The hover-mining ring follows mining cadence, while collection emits a
/// short independent pop at the pointer even when Hover Mining itself is disabled.
/// </summary>
public partial class HoverMiningCursorIndicator : Control
{
    private const float Tau = MathF.PI * 2.0f;

    private float _radius = 18.0f;
    private float _progress;
    private float _pulse;
    private float _collectionPulse;
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
        float dt = Math.Max(0.0f, (float)delta);
        bool redraw = false;

        if (_pulse > 0.0f)
        {
            _pulse = MathF.Max(0.0f, _pulse - dt * 3.6f);
            redraw = true;
        }
        if (_collectionPulse > 0.0f)
        {
            _collectionPulse = MathF.Max(0.0f, _collectionPulse - dt * 5.2f);
            redraw = true;
        }

        Visible = _active || _collectionPulse > 0.0f;
        if (redraw) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_active)
        {
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

        if (_collectionPulse > 0.0f)
        {
            float age = 1.0f - _collectionPulse;
            float bump = MathF.Sin(age * MathF.PI);
            float radius = 7.5f * (1.0f + bump * 0.42f) + age * 2.0f;
            float alpha = 0.72f * _collectionPulse;
            var color = new Color(0.76f, 1.0f, 0.93f, alpha);
            DrawArc(Vector2.Zero, radius, 0.0f, Tau, 28, color, 2.2f, true);

            float tickInner = radius + 2.0f;
            float tickOuter = tickInner + 3.5f * (0.65f + bump * 0.35f);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * MathF.PI * 0.5f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                DrawLine(direction * tickInner, direction * tickOuter, color, 1.6f, true);
            }
        }
    }

    public void SetState(
        bool active,
        Vector2 screenPosition,
        ManualMiningFootprintKind footprint,
        float progress)
    {
        _active = active;
        Position = screenPosition;
        if (!active)
        {
            _progress = 0.0f;
            _pulse = 0.0f;
            Visible = _collectionPulse > 0.0f;
            QueueRedraw();
            return;
        }

        Visible = true;
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

    public void PulseCollection(Vector2 screenPosition)
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        Position = screenPosition;
        _collectionPulse = 1.0f;
        Visible = true;
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
