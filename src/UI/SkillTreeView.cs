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

        var backdrop = new ColorRect
        {
            Color = SkillTreeIncrementalTheme.Paper,
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
    }

    public override void _Process(double delta)
    {
        if (_refreshPending && IsOpen) Refresh();
        if (!IsOpen) return;

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

        bool reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;
        if (reducedMotion)
        {
            _root.Modulate = Colors.White;
            return;
        }

        _root.Modulate = new Color(1, 1, 1, 0);
        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 1.0f, 0.12f);

        int order = 0;
        foreach (IncrementalSkillNodeButton button in _buttons.Values
                     .Where(button => button.Visible)
                     .OrderBy(button => button.Position.Y)
                     .ThenBy(button => button.Position.X))
        {
            button.Modulate = new Color(1, 1, 1, 0);
            button.Scale = Vector2.One * 0.80f;
            Tween nodeTween = CreateTween().SetParallel(true);
            nodeTween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            nodeTween.TweenProperty(button, "scale", Vector2.One, 0.20f).SetDelay(order * 0.010f);
            nodeTween.TweenProperty(button, "modulate:a", 1.0f, 0.12f).SetDelay(order * 0.010f);
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
        _transition.TweenProperty(_root, "modulate:a", 0.0f, 0.09f);
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
            Text = "UPGRADES",
            Position = new Vector2(24, 16),
            Size = new Vector2(230, 34),
            Modulate = SkillTreeIncrementalTheme.Ink,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(title);

        var hint = new Label
        {
            Text = "PAN TREE  WASD / ARROWS / DRAG   ·   BUY UPGRADE  CLICK",
            Position = new Vector2(24, 47),
            Size = new Vector2(720, 24),
            Modulate = SkillTreeIncrementalTheme.MutedInk,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        _root.AddChild(hint);
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
            OffsetBottom = -78,
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
            Size = new Vector2(360, 132),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 40,
        };
        _detailPanel.AddThemeStyleboxOverride("panel", SkillTreeIncrementalTheme.FlatBox(
            SkillTreeIncrementalTheme.PaperBright, SkillTreeIncrementalTheme.Ink, 1, 2));
        _root.AddChild(_detailPanel);

        var stack = new VBoxContainer();
        _detailPanel.AddChild(stack);

        _detailAccent = new ColorRect
        {
            Color = SkillTreeIncrementalTheme.Affordable,
            CustomMinimumSize = new Vector2(0, 22),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        stack.AddChild(_detailAccent);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 7);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        stack.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        _detailTitle = new Label
        {
            Text = "UPGRADE",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeIncrementalTheme.Ink,
        };
        _detailTitle.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(_detailTitle);

        _detailDescription = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(330, 34),
            Modulate = SkillTreeIncrementalTheme.MutedInk,
        };
        _detailDescription.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(_detailDescription);

        _detailRequirement = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeIncrementalTheme.MutedInk,
        };
        _detailRequirement.AddThemeFontSizeOverride("font_size", 10);
        column.AddChild(_detailRequirement);

        _detailCost = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = SkillTreeIncrementalTheme.Affordable,
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
            OffsetTop = -78,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 50,
        };
        bar.AddThemeStyleboxOverride("panel", SkillTreeIncrementalTheme.FlatBox(
            SkillTreeIncrementalTheme.BottomBar, SkillTreeIncrementalTheme.BottomBar, 0, 0));
        _root.AddChild(bar);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        bar.AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 15);
        margin.AddChild(row);

        _resources = new Label
        {
            Text = "0 RESOURCES",
            CustomMinimumSize = new Vector2(230, 52),
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color("#65bd80"),
        };
        _resources.AddThemeFontSizeOverride("font_size", 25);
        row.AddChild(_resources);

        _specialResources = new Label
        {
            Text = string.Empty,
            CustomMinimumSize = new Vector2(190, 52),
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.81f, 0.84f, 0.82f),
        };
        _specialResources.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_specialResources);

        _nextUpgrade = new Label
        {
            Text = "NEXT",
            CustomMinimumSize = new Vector2(250, 52),
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color("#f0d899"),
        };
        _nextUpgrade.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_nextUpgrade);

        var controls = new Label
        {
            Text = "WASD  PAN     LMB  DRAG     CLICK  BUY",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.77f, 0.79f, 0.78f),
        };
        controls.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(controls);

        _feedback = new Label
        {
            Text = string.Empty,
            CustomMinimumSize = new Vector2(200, 52),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = SkillTreeIncrementalTheme.BottomBarText,
        };
        _feedback.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_feedback);

        var close = new Button
        {
            Text = "CONTINUE",
            CustomMinimumSize = new Vector2(210, 50),
            FocusMode = Control.FocusModeEnum.None,
        };
        close.AddThemeFontSizeOverride("font_size", 17);
        close.AddThemeStyleboxOverride("normal", SkillTreeIncrementalTheme.FlatBox(new Color("#687f98"), new Color("#8798a9"), 1, 2));
        close.AddThemeStyleboxOverride("hover", SkillTreeIncrementalTheme.FlatBox(new Color("#748ca6"), new Color("#a1afbd"), 1, 2));
        close.AddThemeStyleboxOverride("pressed", SkillTreeIncrementalTheme.FlatBox(new Color("#5b6f84"), new Color("#8798a9"), 1, 2));
        close.AddThemeColorOverride("font_color", Colors.White);
        close.Pressed += Close;
        row.AddChild(close);
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

        _detailAccent.Color = SkillTreeIncrementalTheme.CategoryColor(node.Category);
        _detailTitle.Text = node.DisplayName.ToUpperInvariant();
        _detailDescription.Text = node.Description;
        _detailRequirement.Text = BuildRequirementLine(node, prerequisites);
        _detailCost.Text = maxed
            ? (node.MaxRank > 1 ? $"MAX RANK {rank}/{node.MaxRank}" : "OWNED")
            : $"{FormatCost(node, cost).ToUpperInvariant()}";
        _detailCost.Modulate = maxed
            ? SkillTreeIncrementalTheme.Purchased
            : (_mining.Currency >= cost && _skills.SpecialCostsAffordable(node) && prerequisites
                ? SkillTreeIncrementalTheme.Affordable
                : SkillTreeIncrementalTheme.Locked);
        _detailPanel.Visible = true;
        PositionDetailCard(node);
    }

    private void PositionDetailCard(SkillNodeDefinition node)
    {
        if (!_buttons.TryGetValue(node.Id, out IncrementalSkillNodeButton? button)) return;
        Vector2 size = _detailPanel.Size;
        Vector2 candidate = button.GlobalPosition + new Vector2(82, -28);
        float maxX = Math.Max(12.0f, _root.Size.X - size.X - 12.0f);
        float maxY = Math.Max(12.0f, _root.Size.Y - 78.0f - size.Y - 12.0f);

        if (candidate.X + size.X > _root.Size.X - 12.0f)
            candidate.X = button.GlobalPosition.X - size.X - 12.0f;

        _detailPanel.Position = new Vector2(
            Mathf.Clamp(candidate.X, 12.0f, maxX),
            Mathf.Clamp(candidate.Y, 12.0f, maxY));
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
            _feedback.Modulate = new Color("#91d7a7");
            if (_buttons.TryGetValue(skillId, out IncrementalSkillNodeButton? button))
                button.PlayPurchaseAnimation();
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
            _feedback.Modulate = new Color("#f1a28f");
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
            ? ""
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

    private const float GridOriginX = 360.0f;
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
                Color branch = SkillTreeIncrementalTheme.CategoryColor(node.Category);
                Color lineColor = requirementMet ? branch : SkillTreeIncrementalTheme.Locked;
                float lineWidth = requirementMet ? 4.5f : 4.0f;
                List<Vector2> points = BuildEdgePoints(sourceNode, node, prerequisite);

                for (int i = 0; i < points.Count - 1; i++)
                {
                    DrawLine(points[i], points[i + 1], lineColor, lineWidth, false);
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
        for (float x = 20; x < Size.X; x += 76)
        for (float y = 18; y < Size.Y; y += 76)
            DrawCircle(new Vector2(x, y), 1.1f, SkillTreeIncrementalTheme.PaperGrid);
    }

    private void DrawFlowDot(Vector2 from, Vector2 to, Color color, double offset)
    {
        float t = (float)((_flowTime * 0.40 + offset) % 1.0);
        Vector2 position = from.Lerp(to, t);
        DrawCircle(position, 3.4f, color.Lightened(0.20f));
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
