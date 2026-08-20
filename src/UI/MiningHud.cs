using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.UI;

public partial class MiningHud : CanvasLayer
{
    private sealed class AutomationEntry
    {
        public AutomationEntry(
            string minerId,
            string skillId,
            string displayName,
            Label status,
            Button action)
        {
            MinerId = minerId;
            SkillId = skillId;
            DisplayName = displayName;
            Status = status;
            Action = action;
        }

        public string MinerId { get; }
        public string SkillId { get; }
        public string DisplayName { get; }
        public Label Status { get; }
        public Button Action { get; }
    }

    private const float AutomationDrawerWidth = 356.0f;

    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private WorldView _view = null!;
    private SkillTreeService _skills = null!;
    private MinerSimulationService _miners = null!;
    private ManualMiningController _manual = null!;
    private MinerPlacementController _placement = null!;

    private PanelContainer _panel = null!;
    private Label _summary = null!;
    private ProgressBar _progress = null!;
    private Label _automation = null!;
    private Label _feedback = null!;
    private Label _details = null!;
    private Label _placementHint = null!;
    private Button _automationToggle = null!;
    private PanelContainer _automationDrawer = null!;
    private Label _automationResources = null!;
    private Label _automationFeedback = null!;
    private readonly Dictionary<string, AutomationEntry> _automationEntries = new(StringComparer.Ordinal);

    private bool _detailsVisible;
    private bool _automationOpen;
    private double _feedbackTime;
    private double _automationFeedbackTime;
    private double _detailRefreshTimer;
    private Tween? _automationTween;

    public void Initialize(
        VirtualWorld world,
        MiningService mining,
        WorldView view,
        SkillTreeService skills,
        MinerSimulationService miners,
        ManualMiningController manual,
        MinerPlacementController placement)
    {
        _world = world;
        _mining = mining;
        _view = view;
        _skills = skills;
        _miners = miners;
        _manual = manual;
        _placement = placement;
        mining.BlockMined += OnBlockMined;
        mining.BlockDamaged += OnBlockDamaged;
        mining.CurrencyChanged += _ => Refresh();
        skills.Changed += Refresh;
        miners.Changed += Refresh;
        placement.Changed += RefreshPlacementHint;
        placement.Feedback += ShowPlacementFeedback;
    }

    public override void _Ready()
    {
        Layer = 20;
        var root = new Control
        {
            Name = "MiningHudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _panel = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 16.0f,
            OffsetTop = -94.0f,
            OffsetRight = 690.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(_panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 7);
        _panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        _summary = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _progress = new ProgressBar
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            MinValue = 0.0,
            MaxValue = 100.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0.0f, 7.0f),
        };
        _automation = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feedback = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _details = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        column.AddChild(_summary);
        column.AddChild(_progress);
        column.AddChild(_automation);
        column.AddChild(_feedback);
        column.AddChild(_details);

        _automationToggle = new Button
        {
            Text = "AUTOMATION [A]",
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -180.0f,
            OffsetTop = 16.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 54.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _automationToggle.Pressed += ToggleAutomationMenu;
        root.AddChild(_automationToggle);

        _placementHint = new Label
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -360.0f,
            OffsetTop = 58.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 100.0f,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(_placementHint);

        BuildAutomationDrawer(root);
        SetAutomationMenuOpen(false, immediate: true);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_feedbackTime > 0.0)
        {
            _feedbackTime -= delta;
            if (_feedbackTime <= 0.0 && _feedback is not null)
            {
                _feedback.Text = string.Empty;
                _feedback.Visible = false;
            }
        }

        if (_automationFeedbackTime > 0.0)
        {
            _automationFeedbackTime -= delta;
            if (_automationFeedbackTime <= 0.0 && _automationFeedback is not null)
            {
                _automationFeedback.Text = string.Empty;
            }
        }

        if (_detailsVisible)
        {
            _detailRefreshTimer += delta;
            if (_detailRefreshTimer >= 0.25)
            {
                _detailRefreshTimer = 0.0;
                RefreshDetails();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.A)
        {
            ToggleAutomationMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.M)
        {
            OpenAutomationMenu("line_miner");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.N)
        {
            OpenAutomationMenu("shovel_miner");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.P)
        {
            OpenAutomationMenu("pickaxe_miner");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.C)
        {
            OpenAutomationMenu("axe_miner");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Escape && _automationOpen)
        {
            CloseAutomationMenu();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode != Key.H)
        {
            return;
        }

        _detailsVisible = !_detailsVisible;
        _details.Visible = _detailsVisible;
        _panel.OffsetTop = _detailsVisible ? -222.0f : -94.0f;
        if (_detailsVisible) RefreshDetails();
        GetViewport().SetInputAsHandled();
    }

    private void BuildAutomationDrawer(Control root)
    {
        _automationDrawer = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorTop = 0.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -AutomationDrawerWidth,
            OffsetTop = 72.0f,
            OffsetRight = -16.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddChild(_automationDrawer);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _automationDrawer.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var title = new Label { Text = "AUTOMATION", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        header.AddChild(title);
        var close = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(64.0f, 32.0f),
        };
        close.Pressed += CloseAutomationMenu;
        header.AddChild(close);
        column.AddChild(header);

        _automationResources = new Label();
        column.AddChild(_automationResources);

        _automationFeedback = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0.0f, 24.0f),
        };
        column.AddChild(_automationFeedback);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0.0f, 260.0f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(list);

        AddAutomationEntry(
            list,
            "line_miner",
            "automation_unlock",
            "DRILL",
            "Straight-line miner. Buy it once, then select a visible cube surface to place it.");
        AddAutomationEntry(
            list,
            "shovel_miner",
            "shovel_unlock",
            "POWERED SHOVEL",
            "Sand crawler. It follows exposed sand and can be placed on a sand surface.");
        AddAutomationEntry(
            list,
            "pickaxe_miner",
            "pickaxe_unlock",
            "ROCK BREAKER",
            "Stone and ore miner. Select it, then place it on a visible cube surface.");
        AddAutomationEntry(
            list,
            "axe_miner",
            "axe_unlock",
            "FOREST CUTTER",
            "Surface tool. Select it, then place it on a visible tree-bearing surface.");
    }

    private void AddAutomationEntry(
        VBoxContainer list,
        string minerId,
        string skillId,
        string displayName,
        string description)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0.0f, 132.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        list.AddChild(card);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 9);
        card.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        margin.AddChild(column);

        var name = new Label { Text = displayName };
        name.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(name);
        column.AddChild(new Label
        {
            Text = description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0.0f, 42.0f),
        });

        var status = new Label();
        column.AddChild(status);

        var action = new Button { CustomMinimumSize = new Vector2(0.0f, 34.0f) };
        string id = minerId;
        action.Pressed += () => OnAutomationAction(id);
        column.AddChild(action);

        _automationEntries.Add(minerId, new AutomationEntry(minerId, skillId, displayName, status, action));
    }

    private void OnAutomationAction(string minerId)
    {
        if (!_automationEntries.TryGetValue(minerId, out AutomationEntry? entry))
        {
            return;
        }

        if (_skills.IsMinerUnlocked(minerId))
        {
            if (_placement.BeginPlacement(minerId))
            {
                ShowAutomationFeedback($"{entry.DisplayName} selected. Click a highlighted cube surface to place it.");
                CloseAutomationMenu();
            }
            else
            {
                ShowAutomationFeedback($"{entry.DisplayName} is not available yet.");
            }

            return;
        }

        SkillPurchaseResult result = _skills.Purchase(entry.SkillId);
        if (result.Success)
        {
            ShowAutomationFeedback($"Bought {entry.DisplayName}. Select it again to place it.");
        }
        else
        {
            ShowAutomationFeedback(PurchaseFailureText(entry.SkillId, result));
        }

        RefreshAutomationMenu();
    }

    private string PurchaseFailureText(string skillId, SkillPurchaseResult result)
    {
        return result.Failure switch
        {
            SkillPurchaseFailure.InsufficientResources => "Not enough resources.",
            SkillPurchaseFailure.MissingPrerequisite =>
                $"Requires: {string.Join(", ", MissingPrerequisites(skillId))}.",
            SkillPurchaseFailure.MaxRank => "Already owned.",
            _ => "Automation could not be bought.",
        };
    }

    private IEnumerable<string> MissingPrerequisites(string skillId)
    {
        SkillNodeDefinition node = _skills.Catalog.Get(skillId);
        foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
        {
            if (_skills.GetRank(prerequisite.NodeId) < prerequisite.RequiredRank)
            {
                yield return $"{_skills.Catalog.Get(prerequisite.NodeId).DisplayName} rank {prerequisite.RequiredRank}";
            }
        }
    }

    private void OpenAutomationMenu(string? preferredMinerId = null)
    {
        _placement.CancelPlacement();
        SetAutomationMenuOpen(true);
        if (preferredMinerId is not null && _automationEntries.TryGetValue(preferredMinerId, out AutomationEntry? entry))
        {
            entry.Action.GrabFocus();
        }
    }

    private void ToggleAutomationMenu()
    {
        if (_automationOpen) CloseAutomationMenu();
        else OpenAutomationMenu();
    }

    private void CloseAutomationMenu()
    {
        SetAutomationMenuOpen(false);
    }

    private void SetAutomationMenuOpen(bool open, bool immediate = false)
    {
        if (_automationOpen == open && !immediate)
        {
            return;
        }

        _automationOpen = open;
        _manual.InputEnabled = !open;
        _automationToggle.Text = open ? "CLOSE AUTOMATION" : "AUTOMATION [A]";
        RefreshAutomationMenu();

        float targetLeft = open ? -AutomationDrawerWidth : 0.0f;
        float targetRight = open ? -16.0f : AutomationDrawerWidth;
        _automationTween?.Kill();
        if (immediate)
        {
            _automationDrawer.OffsetLeft = targetLeft;
            _automationDrawer.OffsetRight = targetRight;
            return;
        }

        _automationTween = CreateTween();
        _automationTween.SetEase(Tween.EaseType.Out);
        _automationTween.SetTrans(Tween.TransitionType.Quad);
        _automationTween.TweenProperty(_automationDrawer, "offset_left", targetLeft, 0.18);
        _automationTween.Parallel().TweenProperty(_automationDrawer, "offset_right", targetRight, 0.18);
    }

    private void ShowPlacementFeedback(string message)
    {
        ShowFeedback(message, 2.5);
        ShowAutomationFeedback(message);
    }

    private void ShowFeedback(string message, double duration)
    {
        if (_feedback is null) return;
        _feedback.Text = message;
        _feedback.Visible = true;
        _feedbackTime = duration;
    }

    private void ShowAutomationFeedback(string message)
    {
        if (_automationFeedback is null) return;
        _automationFeedback.Text = message;
        _automationFeedbackTime = 3.0;
    }

    private void OnBlockMined(MiningResult result)
    {
        Refresh();
        if (_feedback is null) return;

        if (result.BlockId.StartsWith("gem_", System.StringComparison.Ordinal))
        {
            _feedback.Text = $"Gem found: {result.BlockId.Replace("gem_", string.Empty)}  +{result.Reward}";
            _feedback.Modulate = new Color(0.72f, 0.92f, 1.0f);
            _feedbackTime = 1.4;
        }
        else
        {
            string source = result.Source == MiningSource.Automated ? "Auto" : "Mined";
            _feedback.Text = $"{source}: {result.BlockId}  +{result.Reward}";
            _feedback.Modulate = Colors.White;
            _feedbackTime = 0.65;
        }
        _feedback.Visible = true;
    }

    private void OnBlockDamaged(MiningResult result)
    {
        if (_feedback is null) return;
        _feedback.Text = $"Unstable block: hit {result.DamageStage}/{result.DamageRequired}";
        _feedback.Modulate = new Color(1.0f, 0.78f, 0.40f);
        _feedback.Visible = true;
        _feedbackTime = 1.0;
    }

    private void Refresh()
    {
        if (_summary is not null)
        {
            long total = _mining.TotalMined + _mining.Remaining;
            double percent = total <= 0 ? 100.0 : _mining.TotalMined * 100.0 / total;
            _summary.Text =
                $"{_world.Profile.DisplayName}  |  {_mining.Remaining:N0} left  |  {_mining.Currency:N0} resources  |  {percent:0.0}%";
        }

        if (_progress is not null)
        {
            long total = _mining.TotalMined + _mining.Remaining;
            _progress.Value = total <= 0 ? 100.0 : _mining.TotalMined * 100.0 / total;
        }

        if (_automation is not null)
        {
            string drill = _skills.IsMinerUnlocked("line_miner") ? "Drill" : "Drill locked";
            string shovel = _skills.IsMinerUnlocked("shovel_miner") ? "Shovel" : "Shovel locked";
            string rock = _skills.IsMinerUnlocked("pickaxe_miner") ? "Rock" : "Rock locked";
            string forest = _skills.IsMinerUnlocked("axe_miner") ? "Forest" : "Forest locked";
            _automation.Text =
                $"{_miners.Miners.Count} miners  |  {_miners.BlocksPerSecond:0.##} blocks/s  |  {drill} · {shovel} · {rock} · {forest}  |  [A] automation  [H] details";
        }

        RefreshAutomationMenu();
        RefreshPlacementHint();
        if (_detailsVisible) RefreshDetails();
    }

    private void RefreshAutomationMenu()
    {
        if (_automationResources is null || _skills is null) return;

        _automationResources.Text = $"Resources: {_mining.Currency:N0}  |  Scroll for automation";
        foreach (AutomationEntry entry in _automationEntries.Values)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(entry.SkillId);
            int rank = _skills.GetRank(entry.SkillId);
            bool unlocked = _skills.IsMinerUnlocked(entry.MinerId);
            bool prerequisites = _skills.PrerequisitesMet(node);
            long cost = checked(node.Cost * (rank + 1L));

            if (unlocked)
            {
                entry.Status.Text = "OWNED  |  Select to place on the cube";
                entry.Action.Text = "SELECT TO PLACE";
                entry.Action.Disabled = false;
            }
            else if (!prerequisites)
            {
                entry.Status.Text = $"LOCKED  |  Requires {string.Join(", ", MissingPrerequisites(entry.SkillId))}";
                entry.Action.Text = "PREREQUISITES REQUIRED";
                entry.Action.Disabled = true;
            }
            else
            {
                entry.Status.Text = $"AVAILABLE  |  {cost:N0} resources";
                entry.Action.Text = $"BUY  |  {cost:N0} RESOURCES";
                entry.Action.Disabled = false;
            }
        }
    }

    private void RefreshPlacementHint()
    {
        if (_placementHint is null) return;

        if (_placement.IsPlacing && _automationEntries.TryGetValue(_placement.PendingMinerId!, out AutomationEntry? entry))
        {
            _placementHint.Text = $"Placing {entry.DisplayName}\nClick a highlighted cube surface · RMB/Esc cancels";
            _placementHint.Visible = true;
        }
        else
        {
            _placementHint.Text = string.Empty;
            _placementHint.Visible = false;
        }
    }

    private void RefreshDetails()
    {
        if (_details is null) return;

        string slope = _skills.Derived.ShovelHeightTolerance > 0
            ? $"+/-{_skills.Derived.ShovelHeightTolerance} height"
            : "same height only";
        _details.Text =
            $"Controls: [A] Automation   [K] Skill Tree   [M] Drill menu   [N] Shovel menu   [P] Rock   [C] Forest\n" +
            $"Mined: {_mining.TotalMined:N0}   render chunks: {_view.VisibleChunkCount}   dirty: {_view.PendingChunkRebuilds}   modified: {_world.State.ModifiedChunkCount}\n" +
            $"Drill: {_skills.Derived.DrillPatternId}, width {_skills.Derived.MinerPatternWidth}, speed x{_skills.Derived.MinerRateMultiplier:0.##}\n" +
            $"Shovel: {_skills.Derived.ShovelRateMultiplier:0.##}x speed, {slope}, search radius {_skills.Derived.ShovelSearchRadius}";
    }
}
