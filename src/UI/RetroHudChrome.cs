using System;
using Godot;

namespace TenMillionBlocks.UI;

/// <summary>
/// Shared presentation primitives for the gameplay HUD. The goal is deliberately not "modern dark cards":
/// panels are mostly borderless black glass and this control draws sparse instrument/bracket geometry,
/// tiny registration marks and scan-lines over them. That makes the HUD read like one manufactured
/// console rather than a collection of generic Godot rectangles.
/// </summary>
internal partial class RetroHudChrome : Control
{
    public Color Accent { get; set; } = new("#63d8cb");
    public bool Dense { get; set; }
    public bool Scanlines { get; set; } = true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        if (size.X < 12.0f || size.Y < 12.0f) return;

        Color bright = new(Accent, 0.76f);
        Color dim = new(Accent, 0.28f);
        Color ghost = new(Accent, 0.09f);
        float corner = Dense ? 7.0f : 10.0f;
        float notch = Dense ? 4.0f : 6.0f;

        // Four broken corners. They imply a frame without drawing another generic rectangle.
        DrawLine(new Vector2(0, corner), new Vector2(0, notch), bright, 1.0f, false);
        DrawLine(new Vector2(0, notch), new Vector2(notch, 0), bright, 1.0f, false);
        DrawLine(new Vector2(notch, 0), new Vector2(corner + 7.0f, 0), bright, 1.0f, false);

        DrawLine(new Vector2(size.X - corner - 7.0f, 0), new Vector2(size.X - notch, 0), dim, 1.0f, false);
        DrawLine(new Vector2(size.X - notch, 0), new Vector2(size.X, notch), dim, 1.0f, false);
        DrawLine(new Vector2(size.X, notch), new Vector2(size.X, corner), dim, 1.0f, false);

        DrawLine(new Vector2(0, size.Y - corner), new Vector2(0, size.Y - notch), dim, 1.0f, false);
        DrawLine(new Vector2(0, size.Y - notch), new Vector2(notch, size.Y), dim, 1.0f, false);
        DrawLine(new Vector2(notch, size.Y), new Vector2(corner + 4.0f, size.Y), dim, 1.0f, false);

        DrawLine(new Vector2(size.X - corner - 4.0f, size.Y), new Vector2(size.X - notch, size.Y), bright, 1.0f, false);
        DrawLine(new Vector2(size.X - notch, size.Y), new Vector2(size.X, size.Y - notch), bright, 1.0f, false);
        DrawLine(new Vector2(size.X, size.Y - notch), new Vector2(size.X, size.Y - corner), bright, 1.0f, false);

        // A short bus line and registration ticks make each module feel like hardware, not a card.
        float busStart = corner + 12.0f;
        float busEnd = MathF.Min(size.X - corner - 18.0f, busStart + (Dense ? 28.0f : 50.0f));
        if (busEnd > busStart)
        {
            DrawLine(new Vector2(busStart, 0), new Vector2(busEnd, 0), dim, 1.0f, false);
        }

        DrawRect(new Rect2(size.X - 12.0f, 5.0f, 2.0f, 2.0f), bright, true);
        DrawRect(new Rect2(size.X - 8.0f, 5.0f, 2.0f, 2.0f), dim, true);
        if (!Dense)
        {
            DrawRect(new Rect2(size.X - 4.0f, 5.0f, 2.0f, 2.0f), ghost, true);
        }

        if (Scanlines)
        {
            float spacing = Dense ? 5.0f : 6.0f;
            for (float y = spacing; y < size.Y - 2.0f; y += spacing)
            {
                DrawLine(new Vector2(3.0f, y), new Vector2(size.X - 3.0f, y), ghost, 1.0f, false);
            }
        }

        // Small lower-right calibration ruler.
        float rulerY = size.Y - 5.0f;
        float rulerX = MathF.Max(6.0f, size.X - (Dense ? 28.0f : 42.0f));
        for (int i = 0; i < (Dense ? 3 : 5); i++)
        {
            float x = rulerX + i * 5.0f;
            DrawLine(new Vector2(x, rulerY), new Vector2(x, rulerY - (i % 2 == 0 ? 3.0f : 2.0f)), dim, 1.0f, false);
        }
    }

    public static RetroHudChrome Attach(Control parent, Color accent, bool dense = false, bool scanlines = true)
    {
        var chrome = new RetroHudChrome
        {
            Accent = accent,
            Dense = dense,
            Scanlines = scanlines,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 20,
        };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        parent.AddChild(chrome);
        parent.Resized += chrome.QueueRedraw;
        return chrome;
    }

    public static StyleBoxFlat Glass(Color accent, float opacity = 0.86f, bool interactive = false)
        => new()
        {
            BgColor = new Color(0.012f, 0.018f, 0.025f, opacity),
            BorderColor = new Color(accent, interactive ? 0.55f : 0.24f),
            BorderWidthLeft = interactive ? 2 : 1,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = interactive ? 1 : 0,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            ContentMarginLeft = interactive ? 8 : 6,
            ContentMarginRight = 6,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
        };

    public static void SkinButton(Button button, Color accent)
    {
        button.AddThemeStyleboxOverride("normal", Glass(accent, 0.72f, interactive: true));
        button.AddThemeStyleboxOverride("hover", Glass(accent.Lightened(0.10f), 0.88f, interactive: true));
        button.AddThemeStyleboxOverride("pressed", Glass(accent, 0.96f, interactive: true));
        button.AddThemeStyleboxOverride("disabled", Glass(accent.Darkened(0.35f), 0.46f, interactive: true));
        button.AddThemeColorOverride("font_color", new Color("#cdd8d7"));
        button.AddThemeColorOverride("font_hover_color", new Color("#ffffff"));
        button.AddThemeColorOverride("font_pressed_color", new Color("#ffffff"));
        button.AddThemeColorOverride("font_disabled_color", new Color("#566268"));
    }
}

/// <summary>Segmented progress indicator: a row of instrument LEDs rather than a web-style smooth bar.</summary>
internal partial class RetroSegmentBar : Control
{
    private double _value;

    public Color Accent { get; set; } = new("#63d8cb");
    public int SegmentCount { get; set; } = 40;
    public double MinValue { get; set; }
    public double MaxValue { get; set; } = 100.0;

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (SegmentCount <= 0 || Size.X <= 1.0f || Size.Y <= 1.0f) return;
        double span = Math.Max(0.0001, MaxValue - MinValue);
        float ratio = Mathf.Clamp((float)((Value - MinValue) / span), 0.0f, 1.0f);
        float gap = 2.0f;
        float segmentWidth = MathF.Max(1.0f, (Size.X - gap * (SegmentCount - 1)) / SegmentCount);
        int lit = (int)MathF.Round(ratio * SegmentCount);
        Color off = new(Accent, 0.12f);
        Color on = new(Accent, 0.88f);

        for (int i = 0; i < SegmentCount; i++)
        {
            float x = i * (segmentWidth + gap);
            DrawRect(new Rect2(x, 0, segmentWidth, Size.Y), i < lit ? on : off, true);
        }
    }
}
