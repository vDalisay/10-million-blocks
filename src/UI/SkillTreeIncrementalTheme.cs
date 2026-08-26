using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

internal enum SkillNodeVisualKind
{
    Stat,
    Feature,
    Milestone,
}

internal static class SkillTreeIncrementalTheme
{
    public static readonly Color Paper = new("#eeefe9");
    public static readonly Color PaperBright = new("#f8f8f3");
    public static readonly Color PaperGrid = new("#d9dcd3");
    public static readonly Color Ink = new("#454a49");
    public static readonly Color MutedInk = new("#777d79");
    public static readonly Color BottomBar = new("#535659");
    public static readonly Color BottomBarText = new("#f6f6f2");
    public static readonly Color Locked = new("#626668");
    public static readonly Color LockedDark = new("#505355");
    public static readonly Color Affordable = new("#4f9f70");
    public static readonly Color Purchased = new("#3d875d");

    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.Ordinal)
    {
        ["manual"] = new Color("#4e9a6b"),
        ["automation"] = new Color("#4f8da0"),
        ["drill"] = new Color("#5c73b3"),
        ["patterns"] = new Color("#7061ad"),
        ["resources"] = new Color("#b08a4e"),
        ["tools"] = new Color("#9d6855"),
        ["events"] = new Color("#825f9f"),
        ["forest"] = new Color("#588659"),
        ["shovel"] = new Color("#b17e4d"),
        ["finale"] = new Color("#945f84"),
    };

    public static Color CategoryColor(string category)
        => CategoryColors.TryGetValue(category, out Color color) ? color : new Color("#5f8890");

    public static SkillNodeVisualKind VisualKind(SkillNodeDefinition node)
    {
        if (node.SpecialCosts.Count > 0
            || node.Effects.Any(effect => effect.Type is
                "unlock_miner" or
                "unlock_auto_cloud_charger" or
                "unlock_radioactive_cloud" or
                "unlock_orb_breaker" or
                "add_orb_breaker_count" or
                "unlock_manual_auto_collect" or
                "unlock_automation_auto_collect"))
        {
            return SkillNodeVisualKind.Milestone;
        }

        if (node.Effects.Any(effect => effect.Type is
                "multiply_manual_mining_rate" or
                "set_manual_mining_power" or
                "set_manual_footprint" or
                "set_collection_radius_blocks" or
                "multiply_collection_rate" or
                "multiply_resource_yield" or
                "multiply_precious_resource_yield" or
                "add_critical_yield_chance" or
                "set_critical_yield_multiplier" or
                "multiply_miner_rate" or
                "multiply_shovel_rate" or
                "multiply_cloud_charge_rate" or
                "multiply_radioactive_cloud_rate" or
                "add_radioactive_cloud_radius" or
                "multiply_orb_breaker_rate" or
                "add_orb_breaker_radius" or
                "add_lightning_radius" or
                "add_lightning_chain_count" or
                "multiply_meteor_spawn_rate" or
                "add_meteor_radius"))
        {
            return SkillNodeVisualKind.Stat;
        }

        return node.PurchaseMode == "repeatable" ? SkillNodeVisualKind.Stat : SkillNodeVisualKind.Feature;
    }

    public static StyleBoxFlat FlatBox(Color background, Color border, int radius, int borderWidth = 1)
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

internal static class SkillTreeIconAtlas
{
    private const string SheetPath = "res://assets/ui/skill_icons.svg";
    private const int CellSize = 64;
    private static Texture2D? _sheet;

    private static readonly Dictionary<string, int> Indices = new(StringComparer.Ordinal)
    {
        ["manual_2x"] = 0,
        ["manual_3x"] = 1,
        ["manual_5x"] = 1,
        ["hover_mining_unlock"] = 2,
        ["manual_hover_speed"] = 3,
        ["manual_hover_speed_2"] = 3,
        ["manual_hover_speed_3"] = 3,
        ["manual_hover_speed_4"] = 3,
        ["manual_hover_speed_5"] = 3,
        ["manual_power_1"] = 10,
        ["manual_power_2"] = 10,
        ["manual_power_3"] = 10,
        ["manual_power_4"] = 10,
        ["manual_power_5"] = 10,
        ["manual_aftershock"] = 23,
        ["collection_reach_1"] = 9,
        ["collection_rate_1"] = 9,
        ["collection_reach_2"] = 9,
        ["collection_rate_2"] = 9,
        ["collection_auto_manual"] = 9,
        ["collection_auto_automation"] = 9,
        ["automation_unlock"] = 4,
        ["drill_hardened_bit"] = 5,
        ["drill_ore_bit"] = 6,
        ["drill_gem_bit"] = 6,
        ["miner_speed_1"] = 7,
        ["miner_speed_2"] = 7,
        ["miner_speed_3"] = 7,
        ["miner_speed_4"] = 7,
        ["wide_bore_unlock"] = 8,
        ["resource_sensors"] = 9,
        ["resource_density_1"] = 9,
        ["resource_density_2"] = 9,
        ["precious_yield_1"] = 9,
        ["critical_yield_1"] = 18,
        ["critical_yield_2"] = 18,
        ["pickaxe_unlock"] = 10,
        ["cloud_charger_unlock"] = 11,
        ["radioactive_cloud_unlock"] = 19,
        ["radioactive_cloud_frequency_1"] = 19,
        ["radioactive_cloud_radius_1"] = 19,
        ["orb_breaker_unlock"] = 20,
        ["orb_breaker_split_1"] = 20,
        ["orb_breaker_speed_1"] = 20,
        ["orb_breaker_speed_2"] = 20,
        ["orb_breaker_radius_1"] = 20,
        ["orb_breaker_swarm"] = 20,
        ["lightning_frequency_1"] = 11,
        ["lightning_radius_1"] = 11,
        ["lightning_chain_1"] = 22,
        ["lightning_chain_2"] = 22,
        ["meteor_frequency_1"] = 21,
        ["meteor_radius_1"] = 21,
        ["meteor_radius_2"] = 21,
        ["axe_unlock"] = 12,
        ["shovel_unlock"] = 13,
        ["shovel_speed"] = 14,
        ["shovel_speed_2"] = 14,
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

internal partial class SkillNodeLockGlyph : Control
{
    public Color GlyphColor { get; set; } = new("#d8d9d4");

    public override void _Draw()
    {
        DrawRect(new Rect2(7, 13, 16, 13), GlyphColor, false, 2.3f);
        DrawArc(new Vector2(15, 13), 6.5f, Mathf.Pi, Mathf.Tau, 18, GlyphColor, 2.3f, true);
        DrawCircle(new Vector2(15, 19), 1.7f, GlyphColor);
        DrawLine(new Vector2(15, 20), new Vector2(15, 23), GlyphColor, 1.8f, true);
    }
}

public partial class IncrementalSkillNodeButton : Button
{
    private TextureRect _icon = null!;
    private Label _rankBadge = null!;
    private Label _recommendBadge = null!;
    private SkillNodeLockGlyph _lockGlyph = null!;
    private Color _categoryColor;
    private SkillNodeVisualKind _visualKind;
    private bool _purchased;
    private bool _affordable;
    private bool _requirementsMet;
    private bool _recommended;
    private bool _initialized;
    private double _time;
    private Vector2 _baseScale = Vector2.One;

    public string SkillId { get; private set; } = string.Empty;
    internal SkillNodeVisualKind VisualKind => _visualKind;
    public event Action<IncrementalSkillNodeButton>? Hovered;

    public void Initialize(SkillNodeDefinition node)
    {
        SkillId = node.Id;
        _categoryColor = SkillTreeIncrementalTheme.CategoryColor(node.Category);
        _visualKind = SkillTreeIncrementalTheme.VisualKind(node);
        Size = new Vector2(70, 70);
        CustomMinimumSize = Size;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = false;
        Text = string.Empty;
        TooltipText = node.Description;
        PivotOffset = Size * 0.5f;

        int iconInset = _visualKind == SkillNodeVisualKind.Stat ? 16 : 13;
        int iconSize = _visualKind == SkillNodeVisualKind.Stat ? 38 : 44;
        _icon = new TextureRect
        {
            Texture = SkillTreeIconAtlas.ForSkill(node.Id),
            Position = new Vector2(iconInset, iconInset),
            Size = new Vector2(iconSize, iconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_icon);

        _lockGlyph = new SkillNodeLockGlyph
        {
            Position = new Vector2(20, 19),
            Size = new Vector2(30, 30),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_lockGlyph);

        _rankBadge = new Label
        {
            Position = new Vector2(45, 48),
            Size = new Vector2(30, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _rankBadge.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_rankBadge);

        _recommendBadge = new Label
        {
            Text = "NEXT",
            Position = new Vector2(-2, -18),
            Size = new Vector2(74, 17),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            Modulate = _categoryColor.Darkened(0.10f),
        };
        _recommendBadge.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_recommendBadge);

        MouseEntered += () => { Hovered?.Invoke(this); AnimateScale(1.11f, 0.08f); };
        MouseExited += () => AnimateScale(1.0f, 0.10f);
        ButtonDown += () => AnimateScale(0.92f, 0.045f);
        ButtonUp += () => AnimateScale(IsHovered() ? 1.11f : 1.0f, 0.09f);

        _initialized = true;
        ApplyState(0, 1, false, false, false, immediate: true);
    }

    public void SetRecommended(bool recommended)
    {
        _recommended = recommended;
        if (_recommendBadge is not null) _recommendBadge.Visible = recommended && !_purchased;
    }

    public override void _Process(double delta)
    {
        if (!_initialized || GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        _time += delta;
        if ((_affordable || _recommended) && _requirementsMet && !_purchased && !Disabled)
        {
            float amplitude = _recommended ? 0.030f : 0.014f;
            float frequency = _recommended ? 3.8f : 3.1f;
            float pulse = 1.0f + amplitude * (float)Math.Sin(_time * frequency);
            if (!IsHovered()) Scale = _baseScale * pulse;
        }
    }

    public void ApplyState(int rank, int maxRank, bool maxed, bool requirementsMet, bool affordable, bool immediate = false)
    {
        bool wasVisible = Visible;
        bool becameAffordable = !_affordable && affordable && requirementsMet && !maxed;
        _purchased = maxed || rank > 0;
        _affordable = affordable;
        _requirementsMet = requirementsMet;
        Disabled = maxed || !requirementsMet;
        _rankBadge.Text = maxRank > 1 ? $"{rank}/{maxRank}" : (maxed ? "✓" : string.Empty);
        _lockGlyph.Visible = !requirementsMet && !maxed;
        if (_recommendBadge is not null) _recommendBadge.Visible = _recommended && !_purchased;

        Color fill;
        Color border;
        Color icon;
        int radius = _visualKind == SkillNodeVisualKind.Stat ? 35 : (_visualKind == SkillNodeVisualKind.Milestone ? 9 : 3);
        int borderWidth = _visualKind == SkillNodeVisualKind.Milestone ? 4 : 3;

        if (maxed)
        {
            fill = _categoryColor;
            border = _categoryColor.Darkened(0.12f);
            icon = Colors.White;
        }
        else if (!requirementsMet)
        {
            fill = SkillTreeIncrementalTheme.LockedDark;
            border = SkillTreeIncrementalTheme.Locked;
            icon = new Color(0.50f, 0.52f, 0.51f);
        }
        else if (_visualKind == SkillNodeVisualKind.Stat)
        {
            fill = affordable ? _categoryColor : _categoryColor.Darkened(0.30f);
            border = affordable ? _categoryColor.Darkened(0.10f) : _categoryColor.Darkened(0.20f);
            icon = Colors.White;
        }
        else
        {
            fill = SkillTreeIncrementalTheme.PaperBright;
            border = affordable ? _categoryColor : _categoryColor.Darkened(0.20f);
            icon = affordable ? _categoryColor.Darkened(0.30f) : SkillTreeIncrementalTheme.MutedInk;
        }

        if (_recommended && !maxed && requirementsMet)
        {
            border = _categoryColor.Lightened(0.08f);
            borderWidth += 1;
        }

        AddThemeStyleboxOverride("normal", SkillTreeIncrementalTheme.FlatBox(fill, border, radius, borderWidth));
        AddThemeStyleboxOverride("hover", SkillTreeIncrementalTheme.FlatBox(fill.Lightened(0.035f), border.Lightened(0.06f), radius, borderWidth + 1));
        AddThemeStyleboxOverride("pressed", SkillTreeIncrementalTheme.FlatBox(fill.Darkened(0.05f), border, radius, borderWidth + 1));
        AddThemeStyleboxOverride("disabled", SkillTreeIncrementalTheme.FlatBox(fill, border, radius, borderWidth));
        _icon.SelfModulate = icon;
        _rankBadge.AddThemeColorOverride("font_color", maxed || _visualKind == SkillNodeVisualKind.Stat ? Colors.White : SkillTreeIncrementalTheme.Ink);

        if (!immediate && !wasVisible && Visible) PlayRevealAnimation();
        else if (!immediate && becameAffordable) PlayAvailableAnimation();
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
        Scale = Vector2.One * 0.58f;
        Tween tween = CreateTween().SetParallel(true);
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "scale", Vector2.One, 0.25f);
        tween.TweenProperty(this, "modulate:a", 1.0f, 0.15f);
    }

    public void PlayPurchaseAnimation()
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        Tween tween = CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "scale", Vector2.One * 1.27f, 0.10f);
        tween.TweenProperty(this, "scale", Vector2.One * 0.96f, 0.08f);
        tween.TweenProperty(this, "scale", Vector2.One, 0.16f);
    }

    private void PlayAvailableAnimation()
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;
        Tween tween = CreateTween();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "scale", Vector2.One * 1.13f, 0.10f);
        tween.TweenProperty(this, "scale", Vector2.One, 0.18f);
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
