using System;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.UI;

public partial class WorldCompleteView : CanvasLayer
{
    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _stats = null!;
    private Label _next = null!;
    private Button _replay = null!;
    private Button _continue = null!;
    private Tween? _transition;
    private bool _hasNextWorld;

    public event Action? ContinueRequested;
    public event Action? ReplayRequested;
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
            Color = new Color(0.006f, 0.012f, 0.026f, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -270,
            OffsetTop = -215,
            OffsetRight = 270,
            OffsetBottom = 215,
            PivotOffset = new Vector2(270, 215),
        };
        _root.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 26);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        _title = new Label { Text = "WORLD CLEARED", HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 28);
        column.AddChild(_title);

        _stats = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_stats);

        _next = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_next);

        _replay = new Button
        {
            Text = "Watch Replay",
            CustomMinimumSize = new Vector2(0, 44),
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
        _title.Text = demoFinale
            ? "STEAM DEMO COMPLETE"
            : $"{completed.DisplayName.ToUpperInvariant()} CLEARED";
        _stats.Text =
            $"Blocks mined: {blocksMined:N0}\n" +
            $"Manual: {manualBlocks:N0}   Automation: {automatedBlocks:N0}\n" +
            $"Resources available: {resources:N0}";

        if (next is null)
        {
            _next.Text = demoFinale
                ? "You cleared every mineable block in the 50-cube finale. The 100-cube world is reserved for the full release. Your completed worlds and replays remain available from the next session."
                : "Current authored progression complete.";
            _continue.Text = demoFinale ? "Return to Main Menu" : "Close";
        }
        else
        {
            string role = string.IsNullOrWhiteSpace(next.IntroText)
                ? "The next world is generated from its own authored profile and seed."
                : next.IntroText;
            _next.Text = $"Next world: {next.DisplayName}\n{role}";
            _continue.Text = "Continue";
        }

        _replay.Visible = replayAvailable;
        _replay.Disabled = false;
        _continue.Disabled = false;
        _transition?.Kill();
        _root.Visible = true;
        _root.Modulate = new Color(1, 1, 1, 0);
        _panel.Scale = Vector2.One * 0.93f;

        _transition = CreateTween();
        _transition.SetParallel(true);
        _transition.SetEase(Tween.EaseType.Out);
        _transition.SetTrans(Tween.TransitionType.Cubic);
        _transition.TweenProperty(_root, "modulate:a", 1.0f, 0.24f);
        _transition.TweenProperty(_panel, "scale", Vector2.One, 0.28f);
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
        if (_panel is not null)
        {
            _panel.Scale = Vector2.One;
        }
    }

    private void OnReplayPressed()
    {
        if (_replay.Disabled) return;
        _replay.Disabled = true;
        WorldLoadingScreen.RunTransition(this, "LOADING REPLAY", () => ReplayRequested?.Invoke());
    }

    private void OnContinuePressed()
    {
        if (_continue.Disabled) return;
        _continue.Disabled = true;

        _transition?.Kill();
        _transition = CreateTween();
        _transition.SetParallel(true);
        _transition.SetEase(Tween.EaseType.In);
        _transition.SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 0.0f, 0.16f);
        _transition.TweenProperty(_panel, "scale", Vector2.One * 0.97f, 0.16f);
        _transition.Chain().TweenCallback(Callable.From(() =>
        {
            if (_hasNextWorld)
            {
                WorldLoadingScreen.RunTransition(this, "LOADING NEXT WORLD", () => ContinueRequested?.Invoke());
            }
            else
            {
                ContinueRequested?.Invoke();
            }
        }));
    }
}
