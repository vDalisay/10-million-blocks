using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Save;

namespace TenMillionBlocks.Tutorial;

/// <summary>
/// Contextual tutorial presenter. It consumes semantic events instead of being embedded in mechanics,
/// persists one-time milestones, and deliberately stays silent in main-game worlds except for the
/// authored world-start intro. Tips are queued so two milestones reached in quick succession cannot
/// overwrite each other before the player has had a chance to read them. F1 recalls the last relevant
/// message without mutating tutorial progress, so an auto-dismissed instruction is never permanently lost.
/// </summary>
public partial class TutorialDirector : CanvasLayer
{
    private const double DefaultVisibleSeconds = 6.5;
    private const double WorldStartVisibleSeconds = 8.5;

    private readonly record struct TutorialMessage(string Title, string Body, double VisibleSeconds);

    private WorldProfile _profile = null!;
    private GameSaveData _save = null!;
    private GameplayEventHub _hub = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _body = null!;
    private readonly Queue<TutorialMessage> _pending = new();
    private TutorialMessage? _lastMessage;
    private double _hideTimer;

    public event Action? StateChanged;

    public void Initialize(WorldProfile profile, GameSaveData save, GameplayEventHub hub)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    }

    public override void _Ready()
    {
        Layer = 33;
        BuildUi();
        _hub.EventPublished += OnGameplayEvent;
    }

    public override void _ExitTree()
    {
        if (_hub is not null) _hub.EventPublished -= OnGameplayEvent;
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible || _hideTimer <= 0.0) return;
        _hideTimer -= Math.Max(0.0, delta);
        if (_hideTimer <= 0.0)
        {
            DismissCurrent(showNext: true);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F1)
        {
            return;
        }

        TutorialMessage message = _lastMessage ?? BuildWorldHelpMessage();
        if (_panel.Visible)
        {
            // F1 while a tip is already visible acts as "give me more time" rather than replacing a
            // queued semantic tutorial event with stale text.
            _hideTimer = Math.Max(_hideTimer, message.VisibleSeconds);
        }
        else
        {
            ShowMessage(message);
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildUi()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -382.0f,
            OffsetTop = 88.0f,
            OffsetRight = -18.0f,
            OffsetBottom = 220.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        root.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);
        margin.AddChild(column);

        var header = new HBoxContainer();
        _title = new Label
        {
            Text = "TIP",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(_title);

        var close = new Button
        {
            Text = "×",
            Flat = true,
            CustomMinimumSize = new Vector2(30, 30),
            TooltipText = "Dismiss. Press F1 at any time to recall the last tip.",
        };
        close.Pressed += () => DismissCurrent(showNext: true);
        header.AddChild(close);
        column.AddChild(header);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(_body);
    }

    private void OnGameplayEvent(GameplayEvent gameplayEvent)
    {
        if (!string.Equals(gameplayEvent.WorldId, _profile.Id, StringComparison.Ordinal)) return;
        if (!TryMessage(gameplayEvent, out string title, out string body)) return;

        string milestone = $"{_profile.Id}:{gameplayEvent.Kind}";
        if (!_save.SeenTutorialEvents.Add(milestone)) return;

        StateChanged?.Invoke();
        var message = new TutorialMessage(
            title,
            body,
            gameplayEvent.Kind == GameplayEventKind.WorldStarted
                ? WorldStartVisibleSeconds
                : DefaultVisibleSeconds);

        if (_panel.Visible)
        {
            _pending.Enqueue(message);
            return;
        }

        ShowMessage(message);
    }

    private void ShowMessage(TutorialMessage message)
    {
        _lastMessage = message;
        _title.Text = message.Title;
        _body.Text = message.Body;
        _panel.Visible = true;
        _panel.Modulate = Colors.White;
        _panel.Scale = Vector2.One;
        _panel.PivotOffset = _panel.Size * 0.5f;
        _hideTimer = message.VisibleSeconds;

        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            return;
        }

        _panel.Scale = Vector2.One * 0.96f;
        Tween tween = CreateTween();
        tween.SetEase(Tween.EaseType.Out);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(_panel, "scale", Vector2.One, 0.20f);
    }

    private void DismissCurrent(bool showNext)
    {
        _hideTimer = 0.0;
        _panel.Visible = false;
        if (!showNext || _pending.Count == 0) return;

        TutorialMessage next = _pending.Dequeue();
        Callable.From(() =>
        {
            if (IsInsideTree()) ShowMessage(next);
        }).CallDeferred();
    }

    private TutorialMessage BuildWorldHelpMessage()
    {
        string body = string.IsNullOrWhiteSpace(_profile.IntroText)
            ? "Mine every block to clear this world."
            : _profile.IntroText;
        return new TutorialMessage(_profile.DisplayName.ToUpperInvariant(), body, WorldStartVisibleSeconds);
    }

    private bool TryMessage(GameplayEvent gameplayEvent, out string title, out string body)
    {
        title = "TIP";
        body = string.Empty;

        if (gameplayEvent.Kind == GameplayEventKind.WorldStarted)
        {
            title = _profile.DisplayName.ToUpperInvariant();
            body = string.IsNullOrWhiteSpace(_profile.IntroText)
                ? "Mine every block to clear this world."
                : _profile.IntroText;
            return true;
        }

        bool tutorial = _profile.Id is
            "tutorial_single_block" or
            "tutorial_dirt_5" or
            "tutorial_lake_core_10" or
            "tutorial_trees_gem_15";
        if (!tutorial) return false;

        switch (gameplayEvent.Kind)
        {
            case GameplayEventKind.FirstManualMine:
                title = "BLOCK MINED";
                body = "Each mined block increases the world counter and awards its resources. Clear the whole cube to continue.";
                return true;
            case GameplayEventKind.HoverMiningUnlocked:
                title = "HOVER MINING UNLOCKED";
                body = "Use the HOVER MINING toggle to repeatedly mine the block under your cursor without holding the mouse button.";
                return true;
            case GameplayEventKind.FirstAreaMine:
                title = "AREA MINING";
                body = "Your manual footprint now removes several exposed blocks per action. Only the highest valid layer is affected.";
                return true;
            case GameplayEventKind.AutomationClassUnlocked:
                title = "AUTOMATION UNLOCKED";
                body = "The automation class is permanently unlocked. Physical units are still purchased and placed separately in each world.";
                return true;
            case GameplayEventKind.AutomationPlaced:
                title = "AUTOMATION PLACED";
                body = "Automations keep mining on their own. You can keep clicking, upgrade them, or work somewhere else on the cube.";
                return true;
            case GameplayEventKind.ShovelStoppedByWater:
                title = "SHOVEL BLOCKED";
                body = "The Powered Shovel follows soft surface terrain. Water interrupts its route, so clear or route around the obstacle.";
                return true;
            case GameplayEventKind.ShovelStoppedByStone:
                title = "SHOVEL BLOCKED";
                body = "The Powered Shovel cannot cut stone. A different tool or manual mining is needed here.";
                return true;
            case GameplayEventKind.TreeBlockedShovel:
                title = "TREE BLOCKS THE SHOVEL";
                body = "A tree owns the surface tile beneath it. Clear this one manually for now; the stopped Shovel resumes when the route opens. A dedicated tree-clearing machine arrives in the next world.";
                return true;
            case GameplayEventKind.SpecialResourceFound:
                title = "SPECIAL RESOURCE";
                body = "Special resources have their own counter. Some transformation upgrades consume them in addition to ordinary resources.";
                return true;
            case GameplayEventKind.TransformationPurchased:
                title = "WIDE BORE";
                body = "Wide Bore transforms the Drill class. Existing and future compatible Drills now use the larger mining pattern.";
                return true;
            case GameplayEventKind.AutomationStopped:
                title = "AUTOMATION NEEDS ATTENTION";
                body = "A machine can stop when it reaches material it does not understand. Use the stopped-automation alert to focus it and clear or upgrade the blocker.";
                return true;
            default:
                return false;
        }
    }
}
