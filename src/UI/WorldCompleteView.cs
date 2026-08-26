using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.UI;

public partial class WorldCompleteView : CanvasLayer
{
    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Label _kicker = null!;
    private Label _title = null!;
    private Label _progression = null!;
    private Label _stats = null!;
    private Label _next = null!;
    private Button _replay = null!;
    private Button _continue = null!;
    private Button _mainMenu = null!;
    private Tween? _transition;
    private bool _hasNextWorld;

    public event Action? ContinueRequested;
    public event Action? ReplayRequested;
    public event Action? ReturnToMainMenuRequested;
    public bool IsOpen => _root is not null && _root.Visible;

    public override void _Ready()
    {
        Layer = 20;
        _root = new Control
        {
            Name = "WorldCompleteOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new ColorRect
        {
            Color = new Color(0.002f, 0.006f, 0.016f, 0.965f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        // A restrained central glow keeps the final screen in the same deep-space visual family as the
        // constellation without competing with the copy or reading like a separate arcade modal.
        var glow = new ColorRect
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -430,
            OffsetTop = -310,
            OffsetRight = 430,
            OffsetBottom = 310,
            Color = new Color(0.035f, 0.12f, 0.17f, 0.16f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(glow);

        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -310,
            OffsetTop = -270,
            OffsetRight = 310,
            OffsetBottom = 270,
            PivotOffset = new Vector2(310, 270),
        };
        _panel.AddThemeStyleboxOverride("panel", BuildPanelStyle());
        _root.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 34);
        margin.AddThemeConstantOverride("margin_top", 30);
        margin.AddThemeConstantOverride("margin_right", 34);
        margin.AddThemeConstantOverride("margin_bottom", 28);
        _panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 13);
        margin.AddChild(column);

        _kicker = new Label
        {
            Text = "WORLD RUN COMPLETE",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _kicker.AddThemeFontSizeOverride("font_size", 11);
        _kicker.AddThemeColorOverride("font_color", new Color("#7998b2"));
        column.AddChild(_kicker);

        _title = new Label { Text = "WORLD CLEARED", HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 30);
        _title.AddThemeColorOverride("font_color", new Color("#f1f6f5"));
        column.AddChild(_title);

        var divider = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 1),
            Color = new Color(0.30f, 0.82f, 0.78f, 0.42f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(divider);

        _progression = new Label
        {
            Text = "1³   ·   5³   ·   10³   ·   15³   ·   20³   ·   40³   ·   50³",
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _progression.AddThemeFontSizeOverride("font_size", 13);
        _progression.AddThemeColorOverride("font_color", new Color("#73d9cd"));
        column.AddChild(_progression);

        _stats = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _stats.AddThemeFontSizeOverride("font_size", 15);
        _stats.AddThemeColorOverride("font_color", new Color("#d7e2e5"));
        column.AddChild(_stats);

        _next = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _next.AddThemeFontSizeOverride("font_size", 13);
        _next.AddThemeColorOverride("font_color", new Color("#9eafbd"));
        column.AddChild(_next);

        _replay = new Button
        {
            Text = "Watch Replay",
            CustomMinimumSize = new Vector2(0, 42),
            Visible = false,
        };
        _replay.Pressed += OnReplayPressed;
        column.AddChild(_replay);

        _continue = new Button
        {
            Text = "Continue",
            CustomMinimumSize = new Vector2(0, 48),
        };
        _continue.Pressed += OnContinuePressed;
        column.AddChild(_continue);

        _mainMenu = new Button
        {
            Text = "Return to Main Menu",
            CustomMinimumSize = new Vector2(0, 42),
            Visible = false,
        };
        _mainMenu.Pressed += () => ReturnToMainMenuRequested?.Invoke();
        column.AddChild(_mainMenu);

        IncrementalUiSkin.ApplyMenu(_root);
    }

    public void ShowCompletion(
        WorldProfile completed,
        WorldProfile? next,
        long blocksMined,
        long resources,
        long manualBlocks,
        long automatedBlocks,
        bool replayAvailable)
    {
        _hasNextWorld = next is not null;
        bool demoFinale = next is null && completed.Id == "reference_ridges";

        _kicker.Text = demoFinale ? "VERDANT CUBE // STEAM DEMO" : "WORLD RUN COMPLETE";
        _title.Text = demoFinale
            ? "STEAM DEMO COMPLETE"
            : $"{completed.DisplayName.ToUpperInvariant()} CLEARED";
        _progression.Visible = demoFinale;

        long otherBlocks = Math.Max(0L, blocksMined - manualBlocks - automatedBlocks);
        string sourceLine = otherBlocks > 0
            ? $"Manual {manualBlocks:N0}   ·   Automation {automatedBlocks:N0}   ·   Events {otherBlocks:N0}"
            : $"Manual {manualBlocks:N0}   ·   Automation {automatedBlocks:N0}";
        _stats.Text = $"{blocksMined:N0} BLOCKS REMOVED\n{sourceLine}\n{resources:N0} RESOURCES AVAILABLE";

        if (next is null)
        {
            _next.Text = demoFinale
                ? "Every mineable block in the 50³ finale is gone. The complete demo route is cleared. The 100³ destination remains beyond the demo; for now you can revisit finished cubes or watch the run back as a replay."
                : "Current authored progression complete.";
            _continue.Text = demoFinale ? "Browse Completed Worlds" : "Close";
        }
        else
        {
            string role = string.IsNullOrWhiteSpace(next.IntroText)
                ? "The next world is generated from its own authored profile and seed."
                : next.IntroText;
            _next.Text = $"NEXT CUBE  //  {next.DisplayName.ToUpperInvariant()}\n{role}";
            _continue.Text = "Continue";
        }

        _mainMenu.Visible = demoFinale;
        _mainMenu.Disabled = false;
        _replay.Visible = replayAvailable;
        _replay.Disabled = false;
        _continue.Disabled = false;
        _transition?.Kill();
        _transition = null;
        _root.Visible = true;

        ResetAnimatedState();
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true) return;

        _root.Modulate = new Color(1, 1, 1, 0);
        _panel.Scale = Vector2.One * 0.955f;
        _kicker.Modulate = new Color(1, 1, 1, 0);
        _title.Modulate = new Color(1, 1, 1, 0);
        _progression.Modulate = new Color(1, 1, 1, demoFinale ? 0 : 1);

        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.Out);
        _transition.SetTrans(Tween.TransitionType.Quart);
        _transition.TweenProperty(_root, "modulate:a", 1.0f, 0.18f);
        _transition.Parallel().TweenProperty(_panel, "scale", Vector2.One, 0.32f);
        _transition.Parallel().TweenProperty(_kicker, "modulate:a", 1.0f, 0.22f).SetDelay(0.05f);
        _transition.Parallel().TweenProperty(_title, "modulate:a", 1.0f, 0.26f).SetDelay(0.09f);
        if (demoFinale)
            _transition.Parallel().TweenProperty(_progression, "modulate:a", 1.0f, 0.30f).SetDelay(0.15f);
    }

    public void HideCompletion()
    {
        _transition?.Kill();
        _transition = null;
        if (_root is not null)
        {
            _root.Visible = false;
            _root.Modulate = Colors.White;
        }
        ResetAnimatedState();
    }

    private void ResetAnimatedState()
    {
        if (_panel is not null) _panel.Scale = Vector2.One;
        if (_kicker is not null) _kicker.Modulate = Colors.White;
        if (_title is not null) _title.Modulate = Colors.White;
        if (_progression is not null) _progression.Modulate = Colors.White;
    }

    private static StyleBoxFlat BuildPanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color("#07101d"),
            BorderColor = new Color("#38566e"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ShadowColor = new Color(0, 0, 0, 0.58f),
            ShadowSize = 18,
            ShadowOffset = new Vector2(0, 6),
        };
        return style;
    }

    private void OnReplayPressed()
    {
        WorldLoadingScreen.RunTransition(this, "LOADING REPLAY", () => ReplayRequested?.Invoke());
    }

    private void OnContinuePressed()
    {
        if (_hasNextWorld)
        {
            WorldLoadingScreen.RunTransition(this, "LOADING NEXT WORLD", () => ContinueRequested?.Invoke());
            return;
        }

        ContinueRequested?.Invoke();
    }
}
