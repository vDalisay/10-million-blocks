using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

public partial class SkillTreeView : CanvasLayer
{
    private SkillTreeService _skills = null!;
    private MiningService _mining = null!;
    private ManualMiningController _manual = null!;
    private WorldProfile _profile = null!;
    private Control _root = null!;
    private Label _resources = null!;
    private Label _specialResources = null!;
    private Label _nextUpgrade = null!;
    private Label _controls = null!;
    private Label _feedback = null!;
    private PanelContainer _detailPanel = null!;
    private ColorRect _detailAccent = null!;
    private Label _detailTitle = null!;
    private Label _detailDescription = null!;
    private Label _detailCost = null!;
    private Label _detailRequirement = null!;
    private ScrollContainer _scroll = null!;
    private SkillGraphCanvas _graph = null!;
    private readonly Dictionary<string, IncrementalSkillNodeButton> _buttons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previouslyRevealed = new(StringComparer.Ordinal);
    private Tween? _transition;
    private double _feedbackTimer;
    private bool _refreshPending;

    public bool IsOpen => _root is not null && _root.Visible;

    public void Initialize(SkillTreeService skills, MiningService mining, ManualMiningController manual)
    {
        _skills = skills;
        _mining = mining;
        _manual = manual;
        _profile = manual.WorldProfile;

        skills.Changed += RequestRefresh;
        skills.SpecialResources.Changed += RequestRefresh;
        mining.CurrencyChanged += OnCurrencyChanged;
    }

    public override void _Ready()
    {
        Layer = 31;
        _root = new Control
        {
            Name = "SkillTreeOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var backdrop = new SkillTreeSpaceBackdrop
        {
            Name = "ConstellationBackdrop",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        BuildHeader();
        BuildGraph();
        BuildDetailCard();
        BuildBottomBar();
        BuildButtons();
        Refresh();
        UpdateResponsiveBottomBar();
    }

    public override void _Process(double delta)
    {
        if (_refreshPending && IsOpen) Refresh();
        if (!IsOpen) return;

        UpdateResponsiveBottomBar();

        float panSpeed = 560.0f * (float)Math.Max(0.0, delta);
        Vector2 pan = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= panSpeed;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += panSpeed;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= panSpeed;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += panSpeed;
        if (pan != Vector2.Zero) PanGraph(pan);

        if (_feedbackTimer <= 0.0 || string.IsNullOrEmpty(_feedback.Text)) return;
        _feedbackTimer -= delta;
        if (_feedbackTimer <= 0.0) _feedback.Text = string.Empty;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        if (key.Keycode == Key.K)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape && IsOpen)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        _transition?.Kill();
        _root.Visible = true;
        _manual.InputEnabled = false;
        Refresh();
        UpdateResponsiveBottomBar();

        bool reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;
        if (reducedMotion)
        {
            _root.Modulate = Colors.White;
            return;
        }

        _root.Modulate = new Color(1, 1, 1, 0);
        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 1.0f, 0.16f);

        int order = 0;
        foreach (IncrementalSkillNodeButton button in _buttons.Values
                     .Where(button => button.Visible)
                     .OrderBy(button => button.Position.Y)
                     .ThenBy(button => button.Position.X))
        {
            button.Modulate = new Color(1, 1, 1, 0);
            button.Scale = Vector2.One * 0.72f;
            Tween nodeTween = CreateTween().SetParallel(true);
            nodeTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            nodeTween.TweenProperty(button, "scale", Vector2.One, 0.28f).SetDelay(order * 0.014f);
            nodeTween.TweenProperty(button, "modulate:a", 1.0f, 0.16f).SetDelay(order * 0.014f);
            order++;
        }
    }

    public void Close()
    {
        if (!IsOpen) return;
        _transition?.Kill();

        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            _root.Visible = false;
            _root.Modulate = Colors.White;
            _manual.InputEnabled = true;
            return;
        }

        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 0.0f, 0.10f);
        _transition.TweenCallback(Callable.From(() =>
        {
            _root.Visible = false;
            _root.Modulate = Colors.White;
            _manual.InputEnabled = true;
        }));
    }

    private void BuildHeader()
    {
        var title = new Label
        {
            Text = "UPGRADE CONSTELLATION",
            Position = new Vector2(24, 14),
            Size = new Vector2(420, 36),
            Modulate = SkillTreeSpacePalette.Text,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(title);

        var hint = new Label
        {
            Text = "Chart a route through the stars  ·  WASD / arrows / drag to pan  ·  click a node to buy",
            Position = new Vector2(24, 46),
            Size = new Vector2(880, 24),
            Modulate = SkillTreeSpacePalette.TextMuted,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        _root.AddChild(hint);

        var world = new Label
        {
            Text = _profile.DisplayName.ToUpperInvariant(),
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -360.0f,
            OffsetTop = 20.0f,
            OffsetRight = -24.0f,
            OffsetBottom = 48.0f,
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = SkillTreeSpacePalette.TextFaint,
        };
        world.AddThemeFontSizeOverride("font_size", 11);
        _root.AddChild(world);
    }

    private void BuildGraph()
    {
        _scroll = new ScrollContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 0,
            OffsetTop = 72,
            OffsetRight = 0,
            OffsetBottom = -86,
            MouseFilter = Control.MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        _root.AddChild(_scroll);

        _graph = new SkillGraphCanvas
        {
            CustomMinimumSize = SkillGraphCanvas.RequiredSize(_skills.Catalog, _profile),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _graph.Initialize(_skills, _profile);
        _graph.PanRequested += PanGraph;
        _scroll.AddChild(_graph);
    }

    private void BuildDetailCard()
    {
        _detailPanel = new PanelContainer
        {
            Position = new Vector2(24, 84),
            Size = new Vector2(390, 148),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 40,
        };
        _detailPanel.AddThemeStyleboxOverride("panel", SkillTreeSpacePalette.Box(
            new Color(0.055f, 0.095f, 0.17f, 0.97f), new Color("#344b70"), 12, 2));
        _root.AddChild(_detailPanel);

        var stack = new VBoxContainer();
        _detailPanel.AddChild(stack);

        _detailAccent = new ColorRect
        {
            Color = SkillTreeSpacePalette.Affordable,
            CustomMinimumSize = new Vector2(0, 5),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        stack.AddChild(_detailAccent);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        stack.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        margin.AddChild(column);

        _detailTitle = new Label
        {
            Text = "UPGRADE",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeSpacePalette.Text,
        };
        _detailTitle.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_detailTitle);

        _detailDescription = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(350, 42),
            Modulate = SkillTreeSpacePalette.TextMuted,
        };
        _detailDescription.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(_detailDescription);

        _detailRequirement = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeSpacePalette.TextFaint,
        };
        _detailRequirement.AddThemeFontSizeOverride("font_size", 10);
        column.AddChild(_detailRequirement);

        _detailCost = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeSpacePalette.Affordable,
        };
        _detailCost.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_detailCost);
    }

    private void BuildBottomBar()
    {
        var bar = new PanelContainer
        {
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetTop = -86,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 50,
        };
        bar.AddThemeStyleboxOverride("panel", SkillTreeSpacePalette.Box(
            new Color(0.035f, 0.071f, 0.14f, 0.98f), new Color("#1c3154"), 0, 1));
        _root.AddChild(bar);

        var content = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bar.AddChild(content);

        var row = new HBoxContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 18.0f,
            OffsetTop = 10.0f,
            OffsetRight = -194.0f,
            OffsetBottom = -10.0f,
        };
        row.AddThemeConstantOverride("separation", 14);
        content.AddChild(row);

        _resources = new Label
        {
            Text = "0 RESOURCES",
            CustomMinimumSize = new Vector2(210, 52),
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = SkillTreeSpacePalette.Affordable,
        };
        _resources.AddThemeFontSizeOverride("font_size", 24);
        row.AddChild(_resources);

        _specialResources = new Label
        {
            Text = string.Empty,
            CustomMinimumSize = new Vector2(140, 52),
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color("#a9bbd1"),
        };
        _specialResources.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_specialResources);

        _nextUpgrade = new Label
        {
            Text = "NEXT",
            CustomMinimumSize = new Vector2(220, 52),
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color("#f3c75e"),
        };
        _nextUpgrade.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_nextUpgrade);

        _controls = new Label
        {
            Text = "WASD  PAN     LMB  DRAG     CLICK  BUY",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = SkillTreeSpacePalette.TextFaint,
        };
        _controls.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(_controls);

        _feedback = new Label
        {
            Text = string.Empty,
            CustomMinimumSize = new Vector2(130, 52),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = SkillTreeSpacePalette.Text,
        };
        _feedback.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(_feedback);

        var close = new Button
        {
            Text = "CONTINUE",
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -176.0f,
            OffsetTop = -26.0f,
            OffsetRight = -18.0f,
            OffsetBottom = 26.0f,
            FocusMode = Control.FocusModeEnum.None,
        };
        close.AddThemeFontSizeOverride("font_size", 15);
        close.AddThemeStyleboxOverride("normal", SkillTreeSpacePalette.Box(new Color("#17385e"), new Color("#4c89c7"), 9, 2));
        close.AddThemeStyleboxOverride("hover", SkillTreeSpacePalette.Box(new Color("#20507f"), new Color("#6fb9f3"), 9, 2));
        close.AddThemeStyleboxOverride("pressed", SkillTreeSpacePalette.Box(new Color("#102c4c"), new Color("#4c89c7"), 9, 2));
        close.AddThemeColorOverride("font_color", SkillTreeSpacePalette.Text);
        close.Pressed += Close;
        content.AddChild(close);
    }

    private void UpdateResponsiveBottomBar()
    {
        if (_root is null || _controls is null) return;
        float width = _root.Size.X;
        _controls.Visible = width >= 1180.0f;
        _nextUpgrade.Visible = width >= 920.0f;
        _specialResources.CustomMinimumSize = new Vector2(width < 1080.0f ? 105.0f : 140.0f, 52.0f);
        _resources.CustomMinimumSize = new Vector2(width < 1080.0f ? 175.0f : 210.0f, 52.0f);
    }

    private void BuildButtons()
    {
        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            if (!_profile.IsSkillVisible(node.Id, node.Category)) continue;

            var button = new IncrementalSkillNodeButton
            {
                Position = SkillGraphCanvas.NodePosition(node),
            };
            button.Initialize(node);
            button.InstallSpaceFeedback(node);
            button.TooltipText = BuildTooltip(node);
            button.Hovered += _ => ShowDetails(node);
            string id = node.Id;
            button.Pressed += () =>
            {
                ShowDetails(node);
                Purchase(id);
            };
            _graph.AddChild(button);
            _buttons.Add(id, button);
        }
    }

    private string BuildTooltip(SkillNodeDefinition node)
    {
        string description = node.Description;
        if (node.SpecialCosts.Count > 0)
            description += "\nSpecial cost: " + string.Join(", ", node.SpecialCosts.Select(FormatSpecialCost));

        var requirements = new List<string>();
        foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
        {
            SkillNodeDefinition source = _skills.Catalog.Get(prerequisite.NodeId);
            if (!_profile.IsSkillVisible(source.Id, source.Category)) continue;
            requirements.Add($"{source.DisplayName} rank {prerequisite.RequiredRank}");
        }
        return requirements.Count == 0 ? description : description + "\nRequires: " + string.Join(", ", requirements);
    }

    private void ShowDetails(SkillNodeDefinition node)
    {
        int rank = _skills.GetRank(node.Id);
        bool maxed = rank >= node.MaxRank;
        bool prerequisites = _skills.PrerequisitesMet(node);
        long cost = checked(node.Cost * (rank + 1L));

        _detailAccent.Color = SkillTreeSpacePalette.CategoryColor(node.Category);
        _detailTitle.Text = node.DisplayName.ToUpperInvariant();
        _detailDescription.Text = node.Description;
        _detailRequirement.Text = BuildRequirementLine(node, prerequisites);
        _detailCost.Text = maxed
            ? (node.MaxRank > 1 ? $"MAX RANK {rank}/{node.MaxRank}" : "OWNED")
            : FormatCost(node, cost).ToUpperInvariant();
        _detailCost.Modulate = maxed
            ? SkillTreeSpacePalette.Purchased
            : (_mining.Currency >= cost && _skills.SpecialCostsAffordable(node) && prerequisites
                ? SkillTreeSpacePalette.Affordable
                : SkillTreeSpacePalette.TextFaint);
        _detailPanel.Visible = true;
        PositionDetailCard(node);
    }

    private void PositionDetailCard(SkillNodeDefinition node)
    {
        if (!_buttons.TryGetValue(node.Id, out IncrementalSkillNodeButton? button)) return;
        Vector2 size = _detailPanel.Size;
        Vector2 candidate = button.GlobalPosition + new Vector2(84, -34);
        float maxX = Math.Max(12.0f, _root.Size.X - size.X - 12.0f);
        float maxY = Math.Max(12.0f, _root.Size.Y - 86.0f - size.Y - 12.0f);

        if (candidate.X + size.X > _root.Size.X - 12.0f)
            candidate.X = button.GlobalPosition.X - size.X - 14.0f;

        _detailPanel.Position = new Vector2(
            Mathf.Clamp(candidate.X, 12.0f, maxX),
            Mathf.Clamp(candidate.Y, 76.0f, maxY));
    }

    private string BuildRequirementLine(SkillNodeDefinition node, bool requirementsMet)
    {
        if (node.Prerequisites.Count == 0) return "AVAILABLE";
        string text = string.Join(" + ", node.Prerequisites.Select(prerequisite =>
        {
            SkillNodeDefinition source = _skills.Catalog.Get(prerequisite.NodeId);
            return $"{source.DisplayName} RANK {prerequisite.RequiredRank}";
        }));
        return (requirementsMet ? "UNLOCKED BY  " : "REQUIRES  ") + text.ToUpperInvariant();
    }

    private void Purchase(string skillId)
    {
        SkillNodeDefinition node = _skills.Catalog.Get(skillId);
        if (!_profile.IsSkillVisible(node.Id, node.Category) || !_skills.IsRevealed(node)) return;

        SkillPurchaseResult result = _skills.Purchase(skillId);
        if (result.Success)
        {
            _feedback.Text = $"{node.DisplayName} upgraded.";
            _feedback.Modulate = SkillTreeSpacePalette.Affordable;
            if (_buttons.TryGetValue(skillId, out IncrementalSkillNodeButton? button))
            {
                button.PlayPurchaseAnimation();
                button.PlaySpacePurchaseBurst();
            }
        }
        else
        {
            _feedback.Text = result.Failure switch
            {
                SkillPurchaseFailure.InsufficientResources => "Not enough resources.",
                SkillPurchaseFailure.InsufficientSpecialResources => MissingSpecialResources(node),
                SkillPurchaseFailure.MissingPrerequisite => "Unlock the connected prerequisite first.",
                SkillPurchaseFailure.MaxRank => "This upgrade is already complete.",
                _ => "Upgrade could not be purchased.",
            };
            _feedback.Modulate = SkillTreeSpacePalette.Warning;
        }

        _feedbackTimer = 2.0;
        Refresh();
        ShowDetails(node);
    }

    private void RequestRefresh() => _refreshPending = true;
    private void OnCurrencyChanged(long _) => _refreshPending = true;

    private void Refresh()
    {
        _refreshPending = false;
        if (_resources is null) return;

        _resources.Text = $"{_mining.Currency:N0}  RESOURCES";
        _specialResources.Text = _skills.SpecialResources.Balances.Count == 0
            ? string.Empty
            : string.Join("   ", _skills.SpecialResources.Balances
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{DisplayResourceName(pair.Key)}  {pair.Value:N0}"));

        SkillNodeDefinition? recommended = _buttons.Keys
            .Select(id => _skills.Catalog.Get(id))
            .Where(node => _skills.IsRevealed(node))
            .Where(node => _skills.GetRank(node.Id) < node.MaxRank)
            .Where(_skills.PrerequisitesMet)
            .Where(_skills.SpecialCostsAffordable)
            .OrderBy(node => checked(node.Cost * (_skills.GetRank(node.Id) + 1L)))
            .ThenBy(node => node.GridY)
            .ThenBy(node => node.GridX)
            .FirstOrDefault();
        string? recommendedId = recommended?.Id;
        if (_nextUpgrade is not null)
        {
            _nextUpgrade.Text = recommended is null
                ? "NO READY UPGRADES"
                : $"NEXT  {recommended.DisplayName.ToUpperInvariant()}  {checked(recommended.Cost * (_skills.GetRank(recommended.Id) + 1L)):N0}";
        }

        foreach ((string id, IncrementalSkillNodeButton button) in _buttons)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(id);
            bool revealed = _skills.IsRevealed(node);
            bool newlyRevealed = revealed && !_previouslyRevealed.Contains(id);
            button.Visible = revealed;
            button.SetRecommended(revealed && id == recommendedId);
            if (!revealed) continue;

            int rank = _skills.GetRank(id);
            bool maxed = rank >= node.MaxRank;
            bool prerequisites = _skills.PrerequisitesMet(node);
            long cost = checked(node.Cost * (rank + 1L));
            bool affordable = _mining.Currency >= cost && _skills.SpecialCostsAffordable(node);
            button.ApplyState(rank, node.MaxRank, maxed, prerequisites, affordable, immediate: !IsOpen || !newlyRevealed);
            button.ApplySpaceState(rank, node.MaxRank, maxed, prerequisites, affordable);

            if (newlyRevealed && IsOpen) button.PlayRevealAnimation();
            _previouslyRevealed.Add(id);
        }

        _graph.QueueRedraw();
    }

    private void PanGraph(Vector2 delta)
    {
        if (_scroll is null) return;
        _scroll.ScrollHorizontal = Math.Max(0, _scroll.ScrollHorizontal + Mathf.RoundToInt(delta.X));
        _scroll.ScrollVertical = Math.Max(0, _scroll.ScrollVertical + Mathf.RoundToInt(delta.Y));

        if (_detailPanel.Visible)
            _detailPanel.Visible = false;
    }

    private static string FormatCost(SkillNodeDefinition node, long ordinaryCost)
    {
        string text = $"{ordinaryCost:N0} resources";
        foreach (SkillSpecialCostDefinition special in node.SpecialCosts) text += $" + {FormatSpecialCost(special)}";
        return text;
    }

    private static string FormatSpecialCost(SkillSpecialCostDefinition cost)
        => $"{cost.Amount:N0} {DisplayResourceName(cost.ResourceId)}";

    private string MissingSpecialResources(SkillNodeDefinition node)
    {
        var missing = new List<string>();
        foreach (SkillSpecialCostDefinition cost in node.SpecialCosts)
        {
            long have = _skills.SpecialResources.Get(cost.ResourceId);
            if (have < cost.Amount) missing.Add($"{DisplayResourceName(cost.ResourceId)} {have:N0}/{cost.Amount:N0}");
        }
        return missing.Count == 0 ? "Not enough special resources." : "Missing " + string.Join(", ", missing) + ".";
    }

    private static string DisplayResourceName(string resourceId)
        => resourceId switch
        {
            "gem_red" => "Core Gem",
            "gem_blue" => "Azure Gem",
            "gem_green" => "Verdant Gem",
            _ => resourceId.Replace('_', ' '),
        };
}

public partial class SkillGraphCanvas : Control
{
    private SkillTreeService _skills = null!;
    private WorldProfile _profile = null!;
    private bool _dragging;
    private double _flowTime;

    public event Action<Vector2>? PanRequested;

    private const float GridOriginX = 300.0f;
    private const float GridOriginY = 120.0f;
    private const float GridStepX = 128.0f;
    private const float GridStepY = 94.0f;
    private static readonly Vector2 NodeCenterOffset = new(35, 35);

    public void Initialize(SkillTreeService skills, WorldProfile profile)
    {
        _skills = skills;
        _profile = profile;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true) _flowTime += delta;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _dragging = button.Pressed;
            MouseDefaultCursorShape = _dragging ? CursorShape.Drag : CursorShape.Arrow;
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragging)
        {
            PanRequested?.Invoke(-motion.Relative);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_skills is null) return;
        DrawBackdropPattern();

        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            if (!_profile.IsSkillVisible(node.Id, node.Category) || !_skills.IsRevealed(node)) continue;
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            {
                SkillNodeDefinition sourceNode = _skills.Catalog.Get(prerequisite.NodeId);
                if (!_profile.IsSkillVisible(sourceNode.Id, sourceNode.Category) || !_skills.IsRevealed(sourceNode)) continue;

                bool requirementMet = _skills.GetRank(prerequisite.NodeId) >= prerequisite.RequiredRank;
                Color branch = SkillTreeSpacePalette.CategoryColor(node.Category);
                Color lineColor = requirementMet ? branch : SkillTreeSpacePalette.Locked;
                float lineWidth = requirementMet ? 3.2f : 2.4f;
                List<Vector2> points = BuildEdgePoints(sourceNode, node, prerequisite);

                for (int i = 0; i < points.Count - 1; i++)
                {
                    Color glow = lineColor;
                    glow.A = requirementMet ? 0.18f : 0.10f;
                    DrawLine(points[i], points[i + 1], glow, lineWidth + 6.0f, true);
                    DrawLine(points[i], points[i + 1], lineColor, lineWidth, true);
                    if (requirementMet && GraphicsSettingsRuntime.Current?.ReducedMotionEnabled != true)
                        DrawFlowDot(points[i], points[i + 1], branch, i * 0.17);
                }
            }
        }
    }

    private List<Vector2> BuildEdgePoints(
        SkillNodeDefinition source,
        SkillNodeDefinition target,
        SkillPrerequisiteDefinition prerequisite)
    {
        Vector2 from = NodeCenter(source);
        Vector2 to = NodeCenter(target);
        var points = new List<Vector2> { from };

        if (prerequisite.Route.Count > 0)
        {
            points.AddRange(prerequisite.Route.Select(RoutePointPosition));
        }
        else if (!Mathf.IsEqualApprox(from.X, to.X) && !Mathf.IsEqualApprox(from.Y, to.Y))
        {
            float midX = Mathf.Round((from.X + to.X) * 0.5f);
            points.Add(new Vector2(midX, from.Y));
            points.Add(new Vector2(midX, to.Y));
        }

        points.Add(to);
        return points;
    }

    private void DrawBackdropPattern()
    {
        Color small = new(0.35f, 0.52f, 0.76f, 0.13f);
        Color bright = new(0.57f, 0.73f, 0.96f, 0.24f);
        int index = 0;
        for (float x = 24; x < Size.X; x += 82)
        for (float y = 18; y < Size.Y; y += 82)
        {
            float ox = ((index * 37) % 29) - 14;
            float oy = ((index * 61) % 31) - 15;
            Vector2 p = new(x + ox, y + oy);
            DrawCircle(p, index % 7 == 0 ? 1.45f : 0.85f, index % 7 == 0 ? bright : small);
            index++;
        }
    }

    private void DrawFlowDot(Vector2 from, Vector2 to, Color color, double offset)
    {
        float t = (float)((_flowTime * 0.34 + offset) % 1.0);
        Vector2 position = from.Lerp(to, t);
        Color glow = color;
        glow.A = 0.24f;
        DrawCircle(position, 7.0f, glow);
        DrawCircle(position, 2.7f, color.Lightened(0.30f));
    }

    public static Vector2 NodePosition(SkillNodeDefinition node)
        => new(GridOriginX + node.GridX * GridStepX, GridOriginY + node.GridY * GridStepY);

    public static Vector2 RequiredSize(SkillTreeCatalog catalog, WorldProfile profile)
    {
        int minX = 0;
        int maxX = 0;
        int maxY = 0;
        foreach (SkillNodeDefinition node in catalog.Nodes.Values)
        {
            if (!profile.IsSkillVisible(node.Id, node.Category)) continue;
            minX = Math.Min(minX, node.GridX);
            maxX = Math.Max(maxX, node.GridX);
            maxY = Math.Max(maxY, node.GridY);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            foreach (SkillRoutePoint point in prerequisite.Route)
            {
                minX = Math.Min(minX, point.GridX);
                maxX = Math.Max(maxX, point.GridX);
                maxY = Math.Max(maxY, point.GridY);
            }
        }

        float width = GridOriginX + (maxX + 2) * GridStepX + Math.Abs(minX) * 34.0f;
        float height = GridOriginY + (maxY + 2) * GridStepY;
        return new Vector2(Math.Max(width, 1360), Math.Max(height, 860));
    }

    private static Vector2 NodeCenter(SkillNodeDefinition node)
        => NodePosition(node) + NodeCenterOffset;

    private static Vector2 RoutePointPosition(SkillRoutePoint point)
        => new(GridOriginX + point.GridX * GridStepX + NodeCenterOffset.X,
            GridOriginY + point.GridY * GridStepY + NodeCenterOffset.Y);
}
