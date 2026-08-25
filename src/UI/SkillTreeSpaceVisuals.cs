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
    public static readonly Color Backdrop = new("#070d1c");
    public static readonly Color BackdropDeep = new("#030712");
    public static readonly Color Panel = new("#101a2f");
    public static readonly Color PanelRaised = new("#15233d");
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
        ["manual"] = new Color("#52dcc8"),
        ["automation"] = new Color("#55a9ff"),
        ["drill"] = new Color("#7e83ff"),
        ["patterns"] = new Color("#c978ff"),
        ["resources"] = new Color("#f3c75e"),
        ["tools"] = new Color("#ff8872"),
        ["events"] = new Color("#a979ff"),
        ["forest"] = new Color("#76d58a"),
        ["shovel"] = new Color("#ffad62"),
        ["finale"] = new Color("#ff78b7"),
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

        DrawNebula(Size * new Vector2(0.18f, 0.32f), MathF.Max(220.0f, Size.Y * 0.42f), new Color(0.20f, 0.28f, 0.64f, 0.055f));
        DrawNebula(Size * new Vector2(0.80f, 0.58f), MathF.Max(250.0f, Size.Y * 0.48f), new Color(0.50f, 0.19f, 0.62f, 0.045f));
        DrawNebula(Size * new Vector2(0.55f, 0.08f), MathF.Max(160.0f, Size.Y * 0.26f), new Color(0.08f, 0.54f, 0.58f, 0.035f));

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

        Color orbit = new(0.30f, 0.47f, 0.72f, 0.08f);
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

internal partial class SkillNodeSpaceAura : Control
{
    public Color RingColor { get; set; } = Colors.White;

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        float radius = MathF.Min(Size.X, Size.Y) * 0.43f;
        Color outer = RingColor;
        outer.A *= 0.42f;
        Color inner = RingColor;
        inner.A *= 0.72f;
        DrawArc(center, radius, 0, Mathf.Tau, 48, outer, 5.0f, true);
        DrawArc(center, MathF.Max(1.0f, radius - 4.0f), 0, Mathf.Tau, 48, inner, 1.2f, true);
    }
}

public partial class IncrementalSkillNodeButton
{
    private SkillNodeSpaceAura? _spaceAura;
    private Tween? _spaceHoverTween;
    private Tween? _spacePurchaseTween;
    private bool _spaceFeedbackInstalled;

    public void InstallSpaceFeedback(SkillNodeDefinition node)
    {
        if (_spaceFeedbackInstalled) return;
        _spaceFeedbackInstalled = true;
        _categoryColor = SkillTreeSpacePalette.CategoryColor(node.Category);

        // Center the atlas cell itself rather than relying on each glyph's old hand-authored inset.
        _icon.Position = new Vector2(11, 11);
        _icon.Size = new Vector2(48, 48);
        _icon.PivotOffset = new Vector2(24, 24);
        _icon.Scale = Vector2.One;
        _icon.Rotation = 0.0f;

        _lockGlyph.Position = new Vector2(20, 20);
        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;
        _lockGlyph.QueueRedraw();

        _spaceAura = new SkillNodeSpaceAura
        {
            Position = new Vector2(-5, -5),
            Size = new Vector2(80, 80),
            PivotOffset = new Vector2(40, 40),
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
        int radius = _visualKind == SkillNodeVisualKind.Stat ? 35 : (_visualKind == SkillNodeVisualKind.Milestone ? 16 : 10);
        int borderWidth = _visualKind == SkillNodeVisualKind.Milestone ? 3 : 2;

        if (maxed)
        {
            fill = _categoryColor.Darkened(0.44f);
            border = _categoryColor.Lightened(0.16f);
            icon = SkillTreeSpacePalette.Text;
        }
        else if (!requirementsMet)
        {
            fill = SkillTreeSpacePalette.LockedDark;
            border = SkillTreeSpacePalette.Locked;
            icon = SkillTreeSpacePalette.TextFaint;
        }
        else
        {
            fill = affordable ? _categoryColor.Darkened(0.57f) : SkillTreeSpacePalette.PanelRaised;
            border = affordable ? _categoryColor.Lightened(0.08f) : _categoryColor.Darkened(0.28f);
            icon = affordable ? SkillTreeSpacePalette.Text : SkillTreeSpacePalette.TextMuted;
        }

        if (_recommended && !maxed && requirementsMet)
        {
            border = _categoryColor.Lightened(0.24f);
            borderWidth += 1;
        }

        AddThemeStyleboxOverride("normal", SkillTreeSpacePalette.Box(fill, border, radius, borderWidth));
        AddThemeStyleboxOverride("hover", SkillTreeSpacePalette.Box(fill.Lightened(0.08f), border.Lightened(0.16f), radius, borderWidth + 1));
        AddThemeStyleboxOverride("pressed", SkillTreeSpacePalette.Box(fill.Darkened(0.08f), border, radius, borderWidth + 1));
        AddThemeStyleboxOverride("disabled", SkillTreeSpacePalette.Box(fill, border, radius, borderWidth));

        _icon.SelfModulate = icon;
        _rankBadge.AddThemeColorOverride("font_color", maxed ? SkillTreeSpacePalette.Text : SkillTreeSpacePalette.TextMuted);
        _lockGlyph.GlyphColor = SkillTreeSpacePalette.TextMuted;
        _lockGlyph.QueueRedraw();
        if (_spaceAura is not null)
        {
            _spaceAura.RingColor = maxed ? _categoryColor.Lightened(0.18f) : _categoryColor;
            _spaceAura.QueueRedraw();
            if (!IsHovered())
                _spaceAura.Modulate = new Color(1, 1, 1, maxed ? 0.20f : affordable && requirementsMet ? 0.16f : 0.06f);
        }
    }

    public void PlaySpacePurchaseBurst()
    {
        if (_spaceAura is null) return;
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _spaceAura.Modulate = new Color(1, 1, 1, 0.22f);
            return;
        }

        _spacePurchaseTween?.Kill();
        _spaceHoverTween?.Kill();
        _spaceAura.Scale = Vector2.One;
        _spaceAura.Modulate = Colors.White;
        _icon.Rotation = 0.0f;
        _icon.Scale = Vector2.One;

        _spacePurchaseTween = CreateTween();
        _spacePurchaseTween.SetParallel(true);
        _spacePurchaseTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        _spacePurchaseTween.TweenProperty(_spaceAura, "scale", Vector2.One * 1.75f, 0.38f);
        _spacePurchaseTween.TweenProperty(_spaceAura, "modulate:a", 0.0f, 0.42f);
        _spacePurchaseTween.TweenProperty(_icon, "scale", Vector2.One * 1.20f, 0.13f);
        _spacePurchaseTween.TweenProperty(_icon, "rotation", 0.18f, 0.12f);
        _spacePurchaseTween.Chain().SetParallel(true);
        _spacePurchaseTween.TweenProperty(_icon, "scale", Vector2.One, 0.20f);
        _spacePurchaseTween.TweenProperty(_icon, "rotation", 0.0f, 0.20f);
        _spacePurchaseTween.TweenCallback(Callable.From(() => SetSpaceHover(IsHovered())));
    }

    private void SetSpaceHover(bool hovered)
    {
        if (_spaceAura is null || _icon is null) return;
        float auraAlpha = hovered ? 0.66f : (_purchased ? 0.20f : _affordable && _requirementsMet ? 0.16f : 0.06f);
        Vector2 iconScale = hovered ? Vector2.One * 1.12f : Vector2.One;
        float iconRotation = hovered ? -0.055f : 0.0f;

        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _spaceAura.Modulate = new Color(1, 1, 1, auraAlpha);
            _icon.Scale = iconScale;
            _icon.Rotation = iconRotation;
            return;
        }

        _spaceHoverTween?.Kill();
        _spaceHoverTween = CreateTween().SetParallel(true);
        _spaceHoverTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _spaceHoverTween.TweenProperty(_spaceAura, "modulate:a", auraAlpha, 0.12f);
        _spaceHoverTween.TweenProperty(_spaceAura, "scale", hovered ? Vector2.One * 1.08f : Vector2.One, 0.14f);
        _spaceHoverTween.TweenProperty(_icon, "scale", iconScale, 0.12f);
        _spaceHoverTween.TweenProperty(_icon, "rotation", iconRotation, 0.14f);
    }
}
