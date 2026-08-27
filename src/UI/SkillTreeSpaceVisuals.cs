using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

/// <summary>
/// Original visual identity for the player-facing upgrade constellation. The progression grammar stays
/// deliberately incremental-game-like, but the presentation is a dark astronomical map rather than the
/// pale paper schematic used during the first reference pass.
/// </summary>
internal static class SkillTreeSpacePalette
{
    public static readonly Color Backdrop = new("#01030a");
    public static readonly Color BackdropDeep = new("#000106");
    public static readonly Color Panel = new("#0b1424");
    public static readonly Color PanelRaised = new("#111d30");
    public static readonly Color Grid = new("#1c2a46");
    public static readonly Color Text = new("#e7f1ff");
    public static readonly Color TextMuted = new("#8ea4be");
    public static readonly Color TextFaint = new("#60738d");
    public static readonly Color BottomBar = new("#0b1427");
    public static readonly Color Locked = new("#33445f");
    public static readonly Color LockedDark = new("#111c31");
    public static readonly Color Affordable = new("#63e6c5");
    public static readonly Color Purchased = new("#5fc9ff");
    public static readonly Color Warning = new("#ffb46f");

    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.Ordinal)
    {
        ["manual"] = new Color("#68c7b9"),
        ["automation"] = new Color("#6d9fc8"),
        ["drill"] = new Color("#858fc0"),
        ["patterns"] = new Color("#9d86b9"),
        ["resources"] = new Color("#c3a563"),
        ["tools"] = new Color("#bd8274"),
        ["events"] = new Color("#9a82b7"),
        ["forest"] = new Color("#7dad87"),
        ["shovel"] = new Color("#bd8a5d"),
        ["finale"] = new Color("#b7809d"),
    };

    public static Color CategoryColor(string category)
        => CategoryColors.TryGetValue(category, out Color color) ? color : new Color("#62b8cf");

    public static StyleBoxFlat Box(Color background, Color border, int radius, int borderWidth = 1)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
        };
    }
}

internal partial class SkillTreeSpaceBackdrop : Control
{
    private double _time;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true)
            _time += Math.Max(0.0, delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 rect = new(Vector2.Zero, Size);
        DrawRect(rect, SkillTreeSpacePalette.Backdrop);

        DrawNebula(Size * new Vector2(0.18f, 0.32f), MathF.Max(220.0f, Size.Y * 0.42f), new Color(0.20f, 0.28f, 0.64f, 0.022f));
        DrawNebula(Size * new Vector2(0.80f, 0.58f), MathF.Max(250.0f, Size.Y * 0.48f), new Color(0.50f, 0.19f, 0.62f, 0.018f));
        DrawNebula(Size * new Vector2(0.55f, 0.08f), MathF.Max(160.0f, Size.Y * 0.26f), new Color(0.08f, 0.54f, 0.58f, 0.014f));

        int columns = Math.Max(1, Mathf.CeilToInt(Size.X / 58.0f));
        int rows = Math.Max(1, Mathf.CeilToInt(Size.Y / 58.0f));
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            uint hash = Hash(x, y);
            if ((hash & 3u) == 0u) continue;

            float jitterX = ((hash >> 4) & 31u) / 31.0f * 32.0f - 16.0f;
            float jitterY = ((hash >> 9) & 31u) / 31.0f * 32.0f - 16.0f;
            Vector2 p = new(x * 58.0f + 22.0f + jitterX, y * 58.0f + 18.0f + jitterY);
            float phase = ((hash >> 14) & 255u) / 255.0f * Mathf.Tau;
            float twinkle = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true
                ? 0.72f
                : 0.58f + 0.28f * (float)(0.5 + 0.5 * Math.Sin(_time * (0.7 + (hash & 7u) * 0.07) + phase));
            float radius = 0.75f + ((hash >> 23) & 3u) * 0.38f;
            Color star = new(0.72f, 0.84f, 1.0f, twinkle);
            DrawCircle(p, radius, star);

            if (hash % 47u == 0u)
            {
                Color glint = new(0.68f, 0.87f, 1.0f, 0.26f + twinkle * 0.30f);
                DrawLine(p + Vector2.Left * 4.0f, p + Vector2.Right * 4.0f, glint, 1.0f, true);
                DrawLine(p + Vector2.Up * 4.0f, p + Vector2.Down * 4.0f, glint, 1.0f, true);
            }
        }

        Color orbit = new(0.30f, 0.47f, 0.72f, 0.045f);
        Vector2 orbitCenter = Size * new Vector2(0.55f, 0.52f);
        DrawArc(orbitCenter, MathF.Min(Size.X, Size.Y) * 0.31f, 0.15f, 5.65f, 96, orbit, 1.0f, true);
        DrawArc(orbitCenter, MathF.Min(Size.X, Size.Y) * 0.44f, 0.55f, 4.95f, 96, orbit, 1.0f, true);
    }

    private void DrawNebula(Vector2 center, float radius, Color color)
    {
        for (int i = 0; i < 7; i++)
        {
            float t = i / 6.0f;
            float r = radius * (1.0f - t * 0.72f);
            Color layer = color;
            layer.A *= 0.45f + t * 0.35f;
            DrawCircle(center + new Vector2((i - 3) * radius * 0.025f, (3 - i) * radius * 0.012f), r, layer);
        }
    }

    private static uint Hash(int x, int y)
    {
        unchecked
        {
            uint value = (uint)(x * 0x45d9f3b) ^ (uint)(y * 0x119de1f3) ^ 0x9e3779b9u;
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }
    }
}

internal static class SkillNodeStarGeometry
{
    public static Vector2[] Points(Vector2 size, float inset = 2.0f)
    {
        Vector2 c = size * 0.5f;
        float outerX = MathF.Max(2.0f, c.X - inset);
        float outerY = MathF.Max(2.0f, c.Y - inset);
        float shoulderX = outerX * 0.39f;
        float shoulderY = outerY * 0.39f;
        return
        [
            c + new Vector2(0, -outerY),
            c + new Vector2(shoulderX, -shoulderY),
            c + new Vector2(outerX, 0),
            c + new Vector2(shoulderX, shoulderY),
            c + new Vector2(0, outerY),
            c + new Vector2(-shoulderX, shoulderY),
            c + new Vector2(-outerX, 0),
            c + new Vector2(-shoulderX, -shoulderY),
        ];
    }

    public static void DrawOutline(Control canvas, Vector2[] points, Color color, float width)
    {
        for (int i = 0; i < points.Length; i++)
            canvas.DrawLine(points[i], points[(i + 1) % points.Length], color, width, true);
    }
}

internal partial class SkillNodeStarPlate : Control
{
    private Color _fill = new(0.02f, 0.035f, 0.055f, 0.98f);
    private Color _border = new(0.32f, 0.46f, 0.58f, 0.8f);
    private float _borderWidth = 1.0f;
    private bool _hovered;

    public void SetState(Color fill, Color border, float borderWidth)
    {
        _fill = fill;
        _border = border;
        _borderWidth = borderWidth;
        QueueRedraw();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2[] points = SkillNodeStarGeometry.Points(Size, 3.0f);
        Color fill = _hovered ? _fill.Lightened(0.045f) : _fill;
        Color border = _hovered ? _border.Lightened(0.10f) : _border;
        DrawColoredPolygon(points, fill);
        SkillNodeStarGeometry.DrawOutline(this, points, border, _borderWidth + (_hovered ? 0.7f : 0.0f));
    }
}

internal partial class SkillNodeSpaceAura : Control
{
    public Color RingColor { get; set; } = Colors.White;

    public override void _Draw()
    {
        Vector2[] points = SkillNodeStarGeometry.Points(Size, 2.0f);
        Color outer = RingColor;
        outer.A *= 0.16f;
        Color inner = RingColor;
        inner.A *= 0.30f;
        SkillNodeStarGeometry.DrawOutline(this, points, outer, 3.2f);
        SkillNodeStarGeometry.DrawOutline(this, points, inner, 0.9f);
    }
}

public partial class IncrementalSkillNodeButton
{
    private SkillNodeStarPlate? _starPlate;
    private SkillNodeSpaceAura? _spaceAura;
    private Tween? _spaceHoverTween;
    private Tween? _spacePurchaseTween;
    private bool _spaceFeedbackInstalled;

    public void InstallSpaceFeedback(SkillNodeDefinition node)
    {
        if (_spaceFeedbackInstalled) return;
        _spaceFeedbackInstalled = true;
        _categoryColor = SkillTreeSpacePalette.CategoryColor(node.Category);

        // Anchor every visual to the button's exact center, then apply only the glyph's optical correction.
        const float iconSize = 42.0f;
        Vector2 opticalOffset = SkillTreeIconAtlas.OpticalOffsetForSkill(node.Id, iconSize);
        _icon.AnchorLeft = 0.5f;
        _icon.AnchorTop = 0.5f;
        _icon.AnchorRight = 0.5f;
        _icon.AnchorBottom = 0.5f;
        _icon.OffsetLeft = -iconSize * 0.5f + opticalOffset.X;
        _icon.OffsetTop = -iconSize * 0.5f + opticalOffset.Y;
        _icon.OffsetRight = iconSize * 0.5f + opticalOffset.X;
        _icon.OffsetBottom = iconSize * 0.5f + opticalOffset.Y;
        _icon.PivotOffset = new Vector2(iconSize * 0.5f, iconSize * 0.5f);
        _icon.Scale = Vector2.One;
        _icon.Rotation = 0.0f;

        _lockGlyph.Position = new Vector2(20, 20);
        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;
        _lockGlyph.QueueRedraw();

        _starPlate = new SkillNodeStarPlate
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -41,
            OffsetTop = -41,
            OffsetRight = 41,
            OffsetBottom = 41,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_starPlate);
        MoveChild(_starPlate, 0);

        _spaceAura = new SkillNodeSpaceAura
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -45,
            OffsetTop = -45,
            OffsetRight = 45,
            OffsetBottom = 45,
            PivotOffset = new Vector2(45, 45),
            MouseFilter = MouseFilterEnum.Ignore,
            RingColor = _categoryColor,
            Modulate = new Color(1, 1, 1, 0.08f),
        };
        AddChild(_spaceAura);
        MoveChild(_spaceAura, 0);

        MouseEntered += () => SetSpaceHover(true);
        MouseExited += () => SetSpaceHover(false);
    }

    public void ApplySpaceState(int rank, int maxRank, bool maxed, bool requirementsMet, bool affordable)
    {
        Color fill;
        Color border;
        Color icon;
        int borderWidth = 1;

        if (maxed)
        {
            fill = new Color(0.016f, 0.030f, 0.050f, 0.99f);
            border = _categoryColor.Lightened(0.06f);
            icon = new Color(0.86f, 0.91f, 0.95f);
        }
        else if (!requirementsMet)
        {
            fill = new Color(0.010f, 0.017f, 0.030f, 0.98f);
            border = SkillTreeSpacePalette.Locked.Darkened(0.14f);
            icon = SkillTreeSpacePalette.TextFaint;
        }
        else
        {
            fill = affordable
                ? new Color(0.018f, 0.034f, 0.055f, 0.99f)
                : new Color(0.013f, 0.024f, 0.040f, 0.98f);
            border = affordable ? _categoryColor.Darkened(0.08f) : _categoryColor.Darkened(0.48f);
            icon = affordable ? new Color(0.88f, 0.93f, 0.97f) : SkillTreeSpacePalette.TextMuted;
        }

        if (_recommended && !maxed && requirementsMet)
        {
            border = _categoryColor.Lightened(0.13f);
            borderWidth = 2;
        }

        Color transparent = new(0, 0, 0, 0);
        AddThemeStyleboxOverride("normal", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("hover", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("pressed", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        AddThemeStyleboxOverride("disabled", SkillTreeSpacePalette.Box(transparent, transparent, 0, 0));
        _starPlate?.SetState(fill, border, borderWidth);

        _icon.SelfModulate = icon;
        _rankBadge.AddThemeColorOverride("font_color", maxed ? SkillTreeSpacePalette.Text : SkillTreeSpacePalette.TextMuted);
        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;
        _lockGlyph.QueueRedraw();
        if (_spaceAura is not null)
        {
            _spaceAura.RingColor = maxed ? _categoryColor.Lightened(0.12f) : _categoryColor;
            _spaceAura.QueueRedraw();
            if (!IsHovered())
                _spaceAura.Modulate = new Color(1, 1, 1, maxed ? 0.12f : affordable && requirementsMet ? 0.09f : 0.025f);
        }
    }

    public void PlaySpacePurchaseBurst()
    {
        if (_spaceAura is null) return;
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _spaceAura.Modulate = new Color(1, 1, 1, 0.15f);
            return;
        }

        _spacePurchaseTween?.Kill();
        _spaceHoverTween?.Kill();
        _spaceAura.Scale = Vector2.One;
        _spaceAura.Modulate = new Color(1, 1, 1, 0.55f);
        _icon.Rotation = 0.0f;
        _icon.Scale = Vector2.One;

        _spacePurchaseTween = CreateTween();
        _spacePurchaseTween.SetParallel(true);
        _spacePurchaseTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _spacePurchaseTween.TweenProperty(_spaceAura, "scale", Vector2.One * 1.32f, 0.30f);
        _spacePurchaseTween.TweenProperty(_spaceAura, "modulate:a", 0.0f, 0.34f);
        _spacePurchaseTween.TweenProperty(_icon, "scale", Vector2.One * 1.075f, 0.12f);
        _spacePurchaseTween.Chain().TweenProperty(_icon, "scale", Vector2.One, 0.15f);
        _spacePurchaseTween.TweenCallback(Callable.From(() => SetSpaceHover(IsHovered())));
    }

    private void SetSpaceHover(bool hovered)
    {
        if (_spaceAura is null || _icon is null) return;
        _starPlate?.SetHovered(hovered);
        float auraAlpha = hovered ? 0.22f : (_purchased ? 0.10f : _affordable && _requirementsMet ? 0.075f : 0.018f);
        Vector2 iconScale = hovered ? Vector2.One * 1.045f : Vector2.One;

        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _spaceAura.Modulate = new Color(1, 1, 1, auraAlpha);
            _icon.Scale = iconScale;
            _icon.Rotation = 0.0f;
            return;
        }

        _spaceHoverTween?.Kill();
        _spaceHoverTween = CreateTween().SetParallel(true);
        _spaceHoverTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _spaceHoverTween.TweenProperty(_spaceAura, "modulate:a", auraAlpha, 0.13f);
        _spaceHoverTween.TweenProperty(_spaceAura, "scale", hovered ? Vector2.One * 1.025f : Vector2.One, 0.15f);
        _spaceHoverTween.TweenProperty(_icon, "scale", iconScale, 0.13f);
        _spaceHoverTween.TweenProperty(_icon, "rotation", 0.0f, 0.10f);
    }
}
