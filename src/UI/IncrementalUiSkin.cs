using System;
using Godot;

namespace TenMillionBlocks.UI;

/// <summary>
/// Shared shell styling for menus that previously relied on Godot's stock controls. The skill tree has
/// a deliberately pale graph-specific theme; pause/main/settings instead use a restrained dark shell so
/// the game UI reads as one product without competing with the colored upgrade graph.
/// </summary>
internal static class IncrementalUiSkin
{
    private static readonly Color Panel = new("#171c22");
    private static readonly Color PanelBorder = new("#414951");
    private static readonly Color Text = new("#eef0eb");
    private static readonly Color Muted = new("#aab1ae");
    private static readonly Color Paper = new("#eef0e9");
    private static readonly Color PaperHover = new("#f8f9f3");
    private static readonly Color PaperPressed = new("#d9ddd7");
    private static readonly Color Ink = new("#454a49");
    private static readonly Color Green = new("#4e9a6b");
    private static readonly Color GreenHover = new("#5cab79");
    private static readonly Color GreenPressed = new("#407f59");
    private static readonly Color Danger = new("#8f5353");

    public static void ApplyMenu(Control? root)
    {
        if (root is null || !GodotObject.IsInstanceValid(root)) return;
        ApplyRecursive(root);
    }

    private static void ApplyRecursive(Node node)
    {
        switch (node)
        {
            case PanelContainer panel:
                SkinPanel(panel);
                break;
            case OptionButton option:
                SkinOptionButton(option);
                break;
            case CheckButton check:
                SkinCheckButton(check);
                break;
            case Button button:
                SkinButton(button);
                break;
            case Label label:
                SkinLabel(label);
                break;
            case HSeparator separator:
                separator.Modulate = new Color(0.52f, 0.56f, 0.57f, 0.55f);
                break;
        }

        foreach (Node child in node.GetChildren()) ApplyRecursive(child);
    }

    private static void SkinPanel(PanelContainer panel)
    {
        if (panel.HasThemeStyleboxOverride("panel")) return;
        StyleBoxFlat box = Flat(Panel, PanelBorder, 3, 1);
        box.ShadowColor = new Color(0, 0, 0, 0.34f);
        box.ShadowSize = 8;
        box.ShadowOffset = new Vector2(0, 3);
        panel.AddThemeStyleboxOverride("panel", box);
    }

    private static void SkinButton(Button button)
    {
        // Preserve specifically authored controls such as the skill tree and diagnostic harness.
        if (button.HasThemeStyleboxOverride("normal")) return;

        string text = button.Text?.Trim().ToUpperInvariant() ?? string.Empty;
        bool primary = text is "START GAME" or "CONTINUE" or "RESUME";
        bool destructive = text.Contains("CLEAR", StringComparison.Ordinal)
            || text.Contains("DELETE", StringComparison.Ordinal);

        Color normal = destructive ? Danger : primary ? Green : Paper;
        Color hover = destructive ? Danger.Lightened(0.08f) : primary ? GreenHover : PaperHover;
        Color pressed = destructive ? Danger.Darkened(0.10f) : primary ? GreenPressed : PaperPressed;
        Color border = destructive ? Danger.Lightened(0.16f) : primary ? Green.Darkened(0.18f) : new Color("#747b78");
        Color font = primary || destructive ? Colors.White : Ink;

        button.AddThemeStyleboxOverride("normal", Flat(normal, border, 2, 1));
        button.AddThemeStyleboxOverride("hover", Flat(hover, border.Lightened(0.08f), 2, 2));
        button.AddThemeStyleboxOverride("pressed", Flat(pressed, border, 2, 2));
        button.AddThemeStyleboxOverride("disabled", Flat(normal.Darkened(0.28f), border.Darkened(0.22f), 2, 1));
        button.AddThemeColorOverride("font_color", font);
        button.AddThemeColorOverride("font_hover_color", font);
        button.AddThemeColorOverride("font_pressed_color", font);
        button.AddThemeColorOverride("font_disabled_color", new Color(font, 0.48f));
    }

    private static void SkinOptionButton(OptionButton option)
    {
        SkinButton(option);
        option.AddThemeColorOverride("font_color", Ink);
        option.AddThemeColorOverride("font_hover_color", Ink);
        option.AddThemeColorOverride("font_pressed_color", Ink);
    }

    private static void SkinCheckButton(CheckButton check)
    {
        check.AddThemeColorOverride("font_color", Text);
        check.AddThemeColorOverride("font_hover_color", Colors.White);
        check.AddThemeColorOverride("font_pressed_color", Colors.White);
        check.AddThemeColorOverride("font_disabled_color", Muted.Darkened(0.25f));
    }

    private static void SkinLabel(Label label)
    {
        if (label.HasThemeColorOverride("font_color")) return;
        label.AddThemeColorOverride("font_color", Text);
    }

    private static StyleBoxFlat Flat(Color background, Color border, int radius, int width)
        => new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = width,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
}
