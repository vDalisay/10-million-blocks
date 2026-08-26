using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space cursor feedback. The hover-mining ring follows mining cadence, while collection emits a
/// short independent cursor pop at the pointer even when Hover Mining itself is disabled.
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
            DrawCollectionCursorPop();
        }
    }

    private void DrawCollectionCursorPop()
    {
        float age = 1.0f - _collectionPulse;
        float bump = MathF.Sin(age * MathF.PI);
        float scale = 1.0f + bump * 0.22f;
        float alpha = 0.76f * _collectionPulse;

        // This translucent cursor silhouette is anchored at the real OS cursor hotspot. During the
        // ~0.2 second collection beat it expands and settles, making the pointer itself read as popping
        // without permanently replacing the player's platform cursor.
        Vector2[] cursor =
        [
            new Vector2(0.0f, 0.0f),
            new Vector2(1.2f, 14.8f),
            new Vector2(4.8f, 10.8f),
            new Vector2(8.2f, 17.3f),
            new Vector2(10.5f, 16.0f),
            new Vector2(7.0f, 9.6f),
            new Vector2(12.4f, 9.0f),
        ];
        for (int i = 0; i < cursor.Length; i++) cursor[i] *= scale;

        DrawColoredPolygon(cursor, new Color(0.91f, 1.0f, 0.98f, alpha * 0.58f));
        Color outline = new(0.01f, 0.025f, 0.03f, alpha * 0.72f);
        for (int i = 0; i < cursor.Length; i++)
        {
            DrawLine(cursor[i], cursor[(i + 1) % cursor.Length], outline, 1.4f, true);
        }

        float ringRadius = 8.0f * (1.0f + bump * 0.30f) + age * 2.0f;
        var ringColor = new Color(0.76f, 1.0f, 0.93f, alpha * 0.58f);
        DrawArc(Vector2.Zero, ringRadius, 0.0f, Tau, 28, ringColor, 1.7f, true);
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
