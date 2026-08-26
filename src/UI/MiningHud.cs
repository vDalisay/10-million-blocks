using System;
using System.Collections.Generic;
using System.Linq;
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
            PanelContainer card,
            Label status,
            Button action)
        {
            MinerId = minerId;
            SkillId = skillId;
            DisplayName = displayName;
            Card = card;
            Status = status;
            Action = action;
        }

        public string MinerId { get; }
        public string SkillId { get; }
        public string DisplayName { get; }
        public PanelContainer Card { get; }
        public Label Status { get; }
        public Button Action { get; }
    }

    private const float AutomationDrawerWidth = 356.0f;
    private const double AutomatedFeedbackInterval = 0.12;

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
    private bool _refreshPending;
    private double _feedbackTime;
    private double _automationFeedbackTime;
    private double _automatedFeedbackCooldown;
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

        // A single automated frame can emit many BlockMined/CurrencyChanged events. Those events now
        // dirty the HUD and the UI is formatted/layouted once on the next frame instead of repeatedly
        // rebuilding the same strings and automation cards inside the mining loop.
        mining.BlockMined += OnBlockMined;
        mining.BlockDamaged += OnBlockDamaged;
        mining.CurrencyChanged += OnCurrencyChanged;
        skills.Changed += RequestRefresh;
        miners.Changed += RequestRefresh;
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
            OffsetTop = -220.0f,
            OffsetRight = 690.0f,
            OffsetBottom = -54.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
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
        _automationToggle.Visible = _world.Profile.AutomationAvailable;
        root.AddChild(_automationToggle);
        BuildRetroHud(root);

        _placementHint = new Label
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -420.0f,
            OffsetTop = 58.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 132.0f,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(_placementHint);

        BuildAutomationDrawer(root);
        _automationDrawer.Visible = _world.Profile.AutomationAvailable;
        SetAutomationMenuOpen(false, immediate: true);
        Refresh();
        RefreshPlacementHint();
    }

    public override void _Process(double delta)
    {
        if (_refreshPending)
        {
            Refresh();
        }

        if (_automatedFeedbackCooldown > 0.0)
        {
            _automatedFeedbackCooldown = Math.Max(0.0, _automatedFeedbackCooldown - delta);
        }

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

        if (key.Keycode == Key.A && _world.Profile.AutomationAvailable)
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

        if (key.Keycode != Key.H) return;

        _detailsVisible = !_detailsVisible;
        _details.Visible = _detailsVisible;
        _panel.Visible = _detailsVisible;
        if (_detailsVisible) RefreshDetails();
        GetViewport().SetInputAsHandled();
    }

    private void BuildAutomationDrawer(Control root)
    {
        _automationDrawer = new PanelContainer
        {
            AnchorLeft = 0.0f,
            AnchorRight = 0.0f,
            AnchorTop = 0.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -AutomationDrawerWidth,
            OffsetTop = 116.0f,
            OffsetRight = 0.0f,
            OffsetBottom = -58.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _automationDrawer.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#5fd8cf"), 0.94f));
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
        var close = new Button { Text = "CLOSE", CustomMinimumSize = new Vector2(64.0f, 32.0f) };
        ApplyRetroButton(close, new Color("#5fd8cf"));
        close.Pressed += CloseAutomationMenu;
        header.AddChild(close);
        column.AddChild(header);

        _automationResources = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
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

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(list);

        AddAutomationEntry(list, "line_miner", "automation_unlock", "DRILL",
            "Straight-line miner. Unlock the class in the skill tree, then buy each physical Drill for its fixed unit price in the current world.");
        AddAutomationEntry(list, "shovel_miner", "shovel_unlock", "POWERED SHOVEL",
            "Surface crawler for soft terrain. Every physical Shovel is bought for the same fixed unit price and belongs to this world.");
        AddAutomationEntry(list, "pickaxe_miner", "pickaxe_unlock", "ROCK BREAKER",
            "Stone and ore miner. Permanent capability unlock; fixed-price physical units per world.");
        AddAutomationEntry(list, "axe_miner", "axe_unlock", "FOREST CUTTER",
            "Tree-clearing surface tool. Permanent capability unlock; fixed-price physical units per world.");
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
            CustomMinimumSize = new Vector2(0.0f, 124.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        card.AddThemeStyleboxOverride("panel", RetroHudPanel(new Color("#55788a"), 0.72f));
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
        ApplyRetroButton(action, new Color("#5fd8cf"));
        string id = minerId;
        action.Pressed += () => OnAutomationAction(id);
        column.AddChild(action);

        _automationEntries.Add(minerId, new AutomationEntry(minerId, skillId, displayName, card, status, action));
    }

    private void OnAutomationAction(string minerId)
    {
        if (!_automationEntries.TryGetValue(minerId, out AutomationEntry? entry)) return;

        SkillNodeDefinition unlockNode = _skills.Catalog.Get(entry.SkillId);
        if (!_world.Profile.IsSkillCategoryVisible(unlockNode.Category)) return;

        if (!_skills.IsMinerUnlocked(minerId))
        {
            ShowAutomationFeedback($"Unlock {entry.DisplayName} in the skill tree first.");
            return;
        }

        MinerDefinition definition = _miners.GetDefinition(minerId);
        if (_mining.Currency < definition.UnitPrice)
        {
            ShowAutomationFeedback($"Not enough resources. One {entry.DisplayName} costs {definition.UnitPrice:N0}.");
            return;
        }

        if (_placement.BeginUnitPurchasePlacement(minerId))
        {
            ShowAutomationFeedback(
                $"Preview {entry.DisplayName}. {definition.UnitPrice:N0} resources are charged only after a green placement is accepted.");
            CloseAutomationMenu();
        }
        else
        {
            ShowAutomationFeedback($"{entry.DisplayName} could not enter placement mode.");
        }
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
        if (!_world.Profile.AutomationAvailable) return;
        _placement.CancelPlacement();
        SetAutomationMenuOpen(true);
        if (preferredMinerId is not null && _automationEntries.TryGetValue(preferredMinerId, out AutomationEntry? entry))
        {
            SkillNodeDefinition unlockNode = _skills.Catalog.Get(entry.SkillId);
            if (_world.Profile.IsSkillCategoryVisible(unlockNode.Category) && entry.Card.Visible)
            {
                entry.Action.GrabFocus();
            }
        }
    }

    private void ToggleAutomationMenu()
    {
        if (_automationOpen) CloseAutomationMenu();
        else OpenAutomationMenu();
    }

    private void CloseAutomationMenu() => SetAutomationMenuOpen(false);

    private void SetAutomationMenuOpen(bool open, bool immediate = false)
    {
        if (_automationOpen == open && !immediate) return;

        _automationOpen = open;
        _manual.InputEnabled = !open;
        _automationToggle.Text = open ? "CLOSE AUTOMATION" : "AUTOMATION [A]";
        RefreshAutomationMenu();

        float targetLeft = open ? 14.0f : -AutomationDrawerWidth;
        float targetRight = open ? 14.0f + AutomationDrawerWidth : 0.0f;
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
        ShowRetroEvent(message, duration);
    }

    private void ShowAutomationFeedback(string message)
    {
        if (_automationFeedback is null) return;
        _automationFeedback.Text = message;
        _automationFeedbackTime = 3.0;
    }

    private void RequestRefresh()
    {
        _refreshPending = true;
    }

    private void OnCurrencyChanged(long _)
    {
        _refreshPending = true;
    }

    private void OnBlockMined(MiningResult result)
    {
        _refreshPending = true;
        if (_feedback is null) return;

        bool gem = result.BlockId.StartsWith("gem_", StringComparison.Ordinal);
        if (result.Source == MiningSource.Automated && !gem)
        {
            if (_automatedFeedbackCooldown > 0.0) return;
            _automatedFeedbackCooldown = AutomatedFeedbackInterval;
        }

        if (gem)
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

        if (result.BlockId == "bomb")
        {
            _feedback.Text = $"Unstable block: hit {result.DamageStage}/{result.DamageRequired}";
        }
        else
        {
            double damage = result.DamageStage / 100.0;
            double required = Math.Max(0.01, result.DamageRequired / 100.0);
            int percent = Math.Clamp((int)Math.Round(result.DamageStage * 100.0 / Math.Max(1, result.DamageRequired)), 1, 99);
            _feedback.Text = $"Breaker: {result.BlockId}  {damage:0.##}/{required:0.##} damage  ({percent}%)";
        }

        _feedback.Modulate = new Color(1.0f, 0.78f, 0.40f);
        _feedback.Visible = true;
        _feedbackTime = 1.0;
    }

    private void Refresh()
    {
        _refreshPending = false;
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
            if (_world.Profile.AutomationAvailable)
            {
                _automation.Text =
                    $"{_miners.Miners.Count} units  |  {_miners.BlocksPerSecond:0.##} blocks/s  |  [A] automation shop  [H] details";
            }
            else
            {
                _automation.Text = _world.Profile.SkillTreeAvailable
                    ? "LMB: mine the highlighted block  |  [K] skill tree"
                    : "LMB: mine the highlighted block";
            }
        }

        RefreshRetroHud();

        // The expensive four-card prerequisite/cost refresh is irrelevant while its drawer is hidden.
        // Opening the drawer refreshes it immediately, and while open it tracks the same coalesced tick.
        if (_automationOpen) RefreshAutomationMenu();
    }

    private void RefreshAutomationMenu()
    {
        if (!_world.Profile.AutomationAvailable || _automationResources is null || _skills is null) return;

        _automationResources.Text =
            $"Resources: {_mining.Currency:N0}\nClass unlocks persist. Physical units use fixed prices and remain in this world.";

        foreach (AutomationEntry entry in _automationEntries.Values)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(entry.SkillId);
            bool stageVisible = _world.Profile.IsSkillCategoryVisible(node.Category);
            entry.Card.Visible = stageVisible;
            if (!stageVisible) continue;

            bool unlocked = _skills.IsMinerUnlocked(entry.MinerId);
            bool prerequisites = _skills.PrerequisitesMet(node);
            MinerDefinition definition = _miners.GetDefinition(entry.MinerId);

            if (unlocked)
            {
                entry.Status.Text = $"UNLOCKED  |  Fixed unit price {definition.UnitPrice:N0}";
                entry.Action.Text = $"BUY & PLACE  |  {definition.UnitPrice:N0}";
                entry.Action.Disabled = false;
            }
            else if (!prerequisites)
            {
                entry.Status.Text = $"LOCKED  |  Requires {string.Join(", ", MissingPrerequisites(entry.SkillId))}";
                entry.Action.Text = "UNLOCK IN SKILL TREE";
                entry.Action.Disabled = true;
            }
            else
            {
                int rank = _skills.GetRank(entry.SkillId);
                long unlockCost = checked(node.Cost * (rank + 1L));
                entry.Status.Text = $"LOCKED  |  Capability costs {unlockCost:N0} in the skill tree";
                entry.Action.Text = "UNLOCK IN SKILL TREE [K]";
                entry.Action.Disabled = true;
            }
        }
    }

    private void RefreshPlacementHint()
    {
        if (_placementHint is null) return;

        if (_placement.IsPlacing && _automationEntries.TryGetValue(_placement.PendingMinerId!, out AutomationEntry? entry))
        {
            string action = _placement.IsMoving
                ? "Moving"
                : _placement.IsUnitPurchase ? "Buying + placing" : "Placing";
            string payment = string.Empty;
            if (_placement.IsUnitPurchase)
            {
                long price = _miners.GetDefinition(entry.MinerId).UnitPrice;
                payment = $"\nFixed unit price: {price:N0}. Charged only after a valid placement is accepted.";
            }
            _placementHint.Text =
                $"{action} {entry.DisplayName}\nGreen = valid · Red = blocked · LMB place · RMB orbit · Esc/Cancel button to cancel{payment}";
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
        string controls = "Controls: LMB mine   RMB orbit   Wheel zoom";
        if (_world.Profile.SkillTreeAvailable) controls += "   [K] Skill Tree";
        if (_world.Profile.AutomationAvailable) controls += "   [A] Automation";
        _details.Text =
            $"{controls}\n" +
            $"Mined: {_mining.TotalMined:N0}   render chunks: {_view.VisibleChunkCount}   dirty: {_view.PendingChunkRebuilds}   modified: {_world.State.ModifiedChunkCount}\n" +
            $"Drill: {_skills.Derived.DrillPatternId}, width {_skills.Derived.MinerPatternWidth}, speed x{_skills.Derived.MinerRateMultiplier:0.##}\n" +
            $"Shovel: {_skills.Derived.ShovelRateMultiplier:0.##}x speed, {slope}, search radius {_skills.Derived.ShovelSearchRadius}";
    }
}
