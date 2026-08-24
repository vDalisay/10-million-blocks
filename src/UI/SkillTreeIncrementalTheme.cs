using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

internal static class SkillTreeIncrementalTheme
{
    public static readonly Color Paper = new("#eef0e9");
    public static readonly Color PaperGrid = new("#d9ddd3");
    public static readonly Color Ink = new("#3f4646");
    public static readonly Color MutedInk = new("#747b79");
    public static readonly Color BottomBar = new("#45494d");
    public static readonly Color BottomBarText = new("#f5f6f2");
    public static readonly Color Locked = new("#676b6c");
    public static readonly Color LockedDark = new("#515557");
    public static readonly Color Affordable = new("#4e9c70");
    public static readonly Color Purchased = new("#397e5a");

    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.Ordinal)
    {
        ["manual"] = new Color("#4c9a70"),
        ["automation"] = new Color("#4f8b9e"),
        ["drill"] = new Color("#607bb3"),
        ["patterns"] = new Color("#7165ad"),
        ["resources"] = new Color("#b0874f"),
        ["tools"] = new Color("#986653"),
        ["events"] = new Color("#795d9c"),
        ["forest"] = new Color("#59865d"),
        ["shovel"] = new Color("#ae8050"),
    };

    public static Color CategoryColor(string category)
        => CategoryColors.TryGetValue(category, out Color color) ? color : new Color("#5f8890");

    public static StyleBoxFlat FlatBox(Color background, Color border, int radius, int borderWidth = 1)
    {
        var box = new StyleBoxFlat
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
        return box;
    }
}

internal static class SkillTreeIconAtlas
{
    private const string SheetPath = "res://assets/ui/skill_icons.svg";
    private const int CellSize = 64;
    private static Texture2D? _sheet;

    private static readonly Dictionary<string, int> Indices = new(StringComparer.Ordinal)
    {
        ["manual_2x"] = 0,
        ["manual_3x"] = 1,
        ["hover_mining_unlock"] = 2,
        ["manual_hover_speed"] = 3,
        ["automation_unlock"] = 4,
        ["drill_hardened_bit"] = 5,
        ["drill_ore_bit"] = 6,
        ["miner_speed_1"] = 7,
        ["wide_bore_unlock"] = 8,
        ["resource_sensors"] = 9,
        ["pickaxe_unlock"] = 10,
        ["cloud_charger_unlock"] = 11,
        ["axe_unlock"] = 12,
        ["shovel_unlock"] = 13,
        ["shovel_speed"] = 14,
        ["shovel_high_torque"] = 15,
        ["shovel_vertical_sensing"] = 16,
        ["shovel_search_upgrade"] = 17,
    };

    public static Texture2D? ForSkill(string skillId)
    {
        _sheet ??= ResourceLoader.Load<Texture2D>(SheetPath);
        if (_sheet is null) return null;
        int index = Indices.GetValueOrDefault(skillId, 4);
        int column = index % 6;
        int row = index / 6;
        return new AtlasTexture
        {
            Atlas = _sheet,
            Region = new Rect2(column * CellSize, row * CellSize, CellSize, CellSize),
        };
    }
}

public partial class IncrementalSkillNodeButton : Button
{
    private TextureRect _icon = null!;
    private Label _rankBadge = null!;
    private Color _categoryColor;
    private bool _purchased;
    private bool _affordable;
    private bool _requirementsMet;
    private bool _initialized;
    private double _time;
    private Vector2 _baseScale = Vector2.One;

    public string SkillId { get; private set; } = string.Empty;

    public event Action<IncrementalSkillNodeButton>? Hovered;

    public void Initialize(SkillNodeDefinition node)
    {
        SkillId = node.Id;
        _categoryColor = SkillTreeIncrementalTheme.CategoryColor(node.Category);
        Size = new Vector2(70, 70);
        CustomMinimumSize = Size;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = false;
        Text = string.Empty;
        TooltipText = node.Description;
        PivotOffset = Size * 0.5f;

        _icon = new TextureRect
        {
            Texture = SkillTreeIconAtlas.ForSkill(node.Id),
            Position = new Vector2(13, 13),
            Size = new Vector2(44, 44),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_icon);

        _rankBadge = new Label
        {
            Position = new Vector2(46, 48),
            Size = new Vector2(28, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _rankBadge.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_rankBadge);

        MouseEntered += () =>
        {
            Hovered?.Invoke(this);
            AnimateScale(1.10f, 0.09f);
        };
        MouseExited += () => AnimateScale(1.0f, 0.10f);
        ButtonDown += () => AnimateScale(0.94f, 0.05f);
        ButtonUp += () => AnimateScale(IsHovered() ? 1.10f : 1.0f, 0.10f);

        _initialized = true;
        ApplyState(0, 1, false, false, false, immediate: true);
    }

    public override void _Process(double delta)
    {
        if (!_initialized || GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        _time += delta;

        if (_affordable && _requirementsMet && !_purchased && !Disabled)
        {
            float pulse = 1.0f + 0.018f * (float)Math.Sin(_time * 3.3);
            if (!IsHovered()) Scale = _baseScale * pulse;
        }
    }

    public void ApplyState(int rank, int maxRank, bool maxed, bool requirementsMet, bool affordable, bool immediate = false)
    {
        bool wasVisible = Visible;
        _purchased = maxed || rank > 0;
        _affordable = affordable;
        _requirementsMet = requirementsMet;
        Disabled = maxed || !requirementsMet;
        _rankBadge.Text = maxRank > 1 ? $"{rank}/{maxRank}" : (maxed ? "✓" : string.Empty);

        Color fill;
        Color border;
        Color icon;
        if (maxed)
        {
            fill = SkillTreeIncrementalTheme.Purchased;
            border = _categoryColor.Lightened(0.18f);
            icon = Colors.White;
        }
        else if (!requirementsMet)
        {
            fill = SkillTreeIncrementalTheme.LockedDark;
            border = SkillTreeIncrementalTheme.Locked;
            icon = new Color(0.72f, 0.73f, 0.71f);
        }
        else if (affordable)
        {
            fill = _categoryColor;
            border = _categoryColor.Lightened(0.28f);
            icon = Colors.White;
        }
        else
        {
            fill = _categoryColor.Darkened(0.30f);
            border = _categoryColor.Darkened(0.05f);
            icon = new Color(0.88f, 0.90f, 0.87f);
        }

        AddThemeStyleboxOverride("normal", SkillTreeIncrementalTheme.FlatBox(fill, border, 35, 3));
        AddThemeStyleboxOverride("hover", SkillTreeIncrementalTheme.FlatBox(fill.Lightened(0.08f), border.Lightened(0.08f), 35, 4));
        AddThemeStyleboxOverride("pressed", SkillTreeIncrementalTheme.FlatBox(fill.Darkened(0.08f), border, 35, 4));
        AddThemeStyleboxOverride("disabled", SkillTreeIncrementalTheme.FlatBox(fill, border, 35, 3));
        _icon.SelfModulate = icon;
        _rankBadge.AddThemeColorOverride("font_color", Colors.White);

        if (!immediate && !wasVisible && Visible) PlayRevealAnimation();
    }

    public void PlayRevealAnimation()
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            Modulate = Colors.White;
            Scale = Vector2.One;
            return;
        }

        Modulate = new Color(1, 1, 1, 0);
        Scale = Vector2.One * 0.62f;
        Tween tween = CreateTween().SetParallel(true);
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "scale", Vector2.One, 0.23f);
        tween.TweenProperty(this, "modulate:a", 1.0f, 0.16f);
    }

    public void PlayPurchaseAnimation()
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        Tween tween = CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "scale", Vector2.One * 1.24f, 0.11f);
        tween.TweenProperty(this, "scale", Vector2.One, 0.22f);
    }

    private void AnimateScale(float multiplier, float duration)
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            Scale = _baseScale * multiplier;
            return;
        }
        Tween tween = CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "scale", _baseScale * multiplier, duration);
    }
}
