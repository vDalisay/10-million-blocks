using System;
using Godot;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Screen-space hover-mining cadence plus a short hardware-cursor size pop when resources are collected.
/// The collection feedback uses Input.SetCustomMouseCursor so the cursor itself changes size rather than
/// drawing a separate ring near it. The normal system cursor is restored immediately after the beat.
/// </summary>
public partial class HoverMiningCursorIndicator : Control
{
    private const float Tau = MathF.PI * 2.0f;
    private const float CursorPopDuration = 0.18f;

    private float _radius = 18.0f;
    private float _progress;
    private float _pulse;
    private float _cursorPopRemaining;
    private int _cursorStage;
    private bool _active;
    private ImageTexture? _cursorNormal;
    private ImageTexture? _cursorMedium;
    private ImageTexture? _cursorLarge;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Size = Vector2.One;
        Visible = false;
        _cursorNormal = BuildCursorTexture(24);
        _cursorMedium = BuildCursorTexture(28);
        _cursorLarge = BuildCursorTexture(32);
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (_cursorStage != 0)
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            _cursorStage = 0;
        }
    }

    public override void _Process(double delta)
    {
        float dt = Math.Max(0.0f, (float)delta);
        if (_pulse > 0.0f)
        {
            _pulse = MathF.Max(0.0f, _pulse - dt * 3.6f);
            QueueRedraw();
        }

        if (_cursorPopRemaining > 0.0f)
        {
            _cursorPopRemaining = MathF.Max(0.0f, _cursorPopRemaining - dt);
            UpdateHardwareCursorStage();
        }
        else if (_cursorStage != 0)
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            _cursorStage = 0;
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
        Position = screenPosition;
        if (!active)
        {
            _progress = 0.0f;
            _pulse = 0.0f;
            QueueRedraw();
            return;
        }

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
        _cursorPopRemaining = CursorPopDuration;
        SetHardwareCursorStage(3);
    }

    private void UpdateHardwareCursorStage()
    {
        int stage = _cursorPopRemaining switch
        {
            > 0.115f => 3,
            > 0.055f => 2,
            > 0.0f => 1,
            _ => 0,
        };
        SetHardwareCursorStage(stage);
    }

    private void SetHardwareCursorStage(int stage)
    {
        if (_cursorStage == stage) return;
        _cursorStage = stage;
        Resource? texture = stage switch
        {
            3 => _cursorLarge,
            2 => _cursorMedium,
            1 => _cursorNormal,
            _ => null,
        };
        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, Vector2.Zero);
    }

    private static ImageTexture BuildCursorTexture(int size)
    {
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        Vector2[] polygon =
        [
            new(1.0f, 1.0f),
            new(1.0f, size * 0.73f),
            new(size * 0.23f, size * 0.57f),
            new(size * 0.40f, size * 0.91f),
            new(size * 0.53f, size * 0.84f),
            new(size * 0.36f, size * 0.53f),
            new(size * 0.67f, size * 0.49f),
        ];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 point = new(x + 0.5f, y + 0.5f);
            if (!PointInPolygon(point, polygon)) continue;

            bool edge = false;
            for (int oy = -1; oy <= 1 && !edge; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                if (!PointInPolygon(point + new Vector2(ox, oy), polygon))
                {
                    edge = true;
                    break;
                }
            }
            image.SetPixel(x, y, edge ? new Color(0.02f, 0.03f, 0.035f, 1.0f) : Colors.White);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / Math.Max(0.0001f, b.Y - a.Y) + a.X;
            if (crosses) inside = !inside;
        }
        return inside;
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
