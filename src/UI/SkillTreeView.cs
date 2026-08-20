using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
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
    private Label _feedback = null!;
    private ScrollContainer _scroll = null!;
    private SkillGraphCanvas _graph = null!;
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private Tween? _transition;
    private double _feedbackTimer;

    public bool IsOpen => _root is not null && _root.Visible;

    public void Initialize(SkillTreeService skills, MiningService mining, ManualMiningController manual)
    {
        _skills = skills;
        _mining = mining;
        _manual = manual;
        _profile = manual.WorldProfile;
        skills.Changed += Refresh;
        mining.CurrencyChanged += _ => Refresh();
    }

    public override void _Ready()
    {
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
            Color = new Color(0.015f, 0.022f, 0.04f, 0.96f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var title = new Label { Text = "SKILL TREE", Position = new Vector2(32, 24) };
        title.AddThemeFontSizeOverride("font_size", 24);
        _root.AddChild(title);

        _resources = new Label { Position = new Vector2(32, 58) };
        _root.AddChild(_resources);

        _feedback = new Label
        {
            Position = new Vector2(32, 84),
            CustomMinimumSize = new Vector2(720, 28),
        };
        _root.AddChild(_feedback);

        var close = new Button
        {
            Text = "Close [K / Esc]",
            Position = new Vector2(32, 116),
            Size = new Vector2(150, 34),
        };
        close.Pressed += Close;
        _root.AddChild(close);

        _scroll = new ScrollContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 210.0f,
            OffsetTop = 72.0f,
            OffsetRight = -24.0f,
            OffsetBottom = -24.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _root.AddChild(_scroll);

        _graph = new SkillGraphCanvas
        {
            CustomMinimumSize = SkillGraphCanvas.RequiredSize(_skills.Catalog, _profile),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _graph.Initialize(_skills, _profile);
        _scroll.AddChild(_graph);

        BuildButtons();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_feedbackTimer <= 0.0 || _feedback is null || string.IsNullOrEmpty(_feedback.Text)) return;
        _feedbackTimer -= delta;
        if (_feedbackTimer <= 0.0)
        {
            _feedback.Text = string.Empty;
        }
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
        _root.Modulate = new Color(1, 1, 1, 0);
        _manual.InputEnabled = false;
        Refresh();

        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.Out);
        _transition.SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 1.0f, 0.16f);
    }

    public void Close()
    {
        if (!IsOpen) return;
        _transition?.Kill();
        _transition = CreateTween();
        _transition.SetEase(Tween.EaseType.In);
        _transition.SetTrans(Tween.TransitionType.Quad);
        _transition.TweenProperty(_root, "modulate:a", 0.0f, 0.11f);
        _transition.TweenCallback(Callable.From(() =>
        {
            _root.Visible = false;
            _root.Modulate = Colors.White;
            _manual.InputEnabled = true;
        }));
    }

    private void BuildButtons()
    {
        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            if (!_profile.IsSkillCategoryVisible(node.Category)) continue;

            var button = new Button
            {
                Position = SkillGraphCanvas.NodePosition(node),
                Size = new Vector2(174, 82),
                TooltipText = BuildTooltip(node),
            };
            string id = node.Id;
            button.Pressed += () => Purchase(id);
            _graph.AddChild(button);
            _buttons.Add(id, button);
        }
    }

    private string BuildTooltip(SkillNodeDefinition node)
    {
        string description = node.Description;
        if (TryGetMinerUnlock(node, out _))
        {
            description += "\nPlacement is previewed before payment; green commits, red is invalid, Esc/Cancel aborts.";
        }

        if (node.Prerequisites.Count == 0) return description;
        var requirements = new List<string>();
        foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
        {
            SkillNodeDefinition source = _skills.Catalog.Get(prerequisite.NodeId);
            if (!_profile.IsSkillCategoryVisible(source.Category)) continue;
            requirements.Add($"{source.DisplayName} rank {prerequisite.RequiredRank}");
        }
        return requirements.Count == 0
            ? description
            : description + "\nRequires: " + string.Join(", ", requirements);
    }

    private void Purchase(string skillId)
    {
        SkillNodeDefinition node = _skills.Catalog.Get(skillId);
        if (!_profile.IsSkillCategoryVisible(node.Category)) return;

        if (TryGetMinerUnlock(node, out string minerId) && _skills.GetRank(skillId) < node.MaxRank)
        {
            BeginAutomationPurchasePlacement(node, minerId);
            return;
        }

        SkillPurchaseResult result = _skills.Purchase(skillId);
        if (result.Success)
        {
            _feedback.Text = $"Purchased {node.DisplayName} rank {result.NewRank}.";
            _feedback.Modulate = new Color(0.60f, 1.0f, 0.70f);
            if (_buttons.TryGetValue(skillId, out Button? button))
            {
                Vector2 originalScale = button.Scale;
                button.PivotOffset = button.Size * 0.5f;
                var pulse = CreateTween();
                pulse.TweenProperty(button, "scale", originalScale * 1.05f, 0.07f);
                pulse.TweenProperty(button, "scale", originalScale, 0.11f);
            }
        }
        else
        {
            _feedback.Text = result.Failure switch
            {
                SkillPurchaseFailure.InsufficientResources => "Not enough resources.",
                SkillPurchaseFailure.MissingPrerequisite => "Required prerequisite rank has not been reached.",
                SkillPurchaseFailure.MaxRank => "Skill is already maxed.",
                _ => "Skill could not be purchased.",
            };
            _feedback.Modulate = new Color(1.0f, 0.62f, 0.55f);
        }

        _feedbackTimer = 2.0;
        Refresh();
    }

    private void BeginAutomationPurchasePlacement(SkillNodeDefinition node, string minerId)
    {
        int rank = _skills.GetRank(node.Id);
        long cost = checked(node.Cost * (rank + 1L));
        if (!_skills.PrerequisitesMet(node))
        {
            _feedback.Text = "Required prerequisite rank has not been reached.";
            _feedback.Modulate = new Color(1.0f, 0.62f, 0.55f);
            _feedbackTimer = 2.0;
            return;
        }
        if (_mining.Currency < cost)
        {
            _feedback.Text = $"Not enough resources. Need {cost:N0}.";
            _feedback.Modulate = new Color(1.0f, 0.62f, 0.55f);
            _feedbackTimer = 2.0;
            return;
        }

        MinerPlacementController? placement = GetParent()?.GetNodeOrNull<MinerPlacementController>("MinerPlacement");
        if (placement is null || !placement.BeginPurchasePlacement(minerId, node.Id))
        {
            _feedback.Text = "Automation placement could not be started.";
            _feedback.Modulate = new Color(1.0f, 0.62f, 0.55f);
            _feedbackTimer = 2.0;
            return;
        }

        Close();
    }

    private static bool TryGetMinerUnlock(SkillNodeDefinition node, out string minerId)
    {
        SkillEffectDefinition? effect = node.Effects.FirstOrDefault(candidate => candidate.Type == "unlock_miner");
        minerId = effect?.StringValue ?? string.Empty;
        return !string.IsNullOrWhiteSpace(minerId);
    }

    private void Refresh()
    {
        if (_resources is null) return;
        _resources.Text =
            $"Resources: {_mining.Currency:N0}   |   Manual footprint: {_skills.Derived.ManualFootprint}" +
            $"   |   Hover: {(_skills.Derived.HoverMiningUnlocked ? "unlocked" : "locked")}" +
            $"   |   Drill speed: x{_skills.Derived.MinerRateMultiplier:0.##}";

        foreach ((string id, Button button) in _buttons)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(id);
            int rank = _skills.GetRank(id);
            bool maxed = rank >= node.MaxRank;
            bool prerequisites = _skills.PrerequisitesMet(node);
            long cost = checked(node.Cost * (rank + 1L));
            bool affordable = _mining.Currency >= cost;
            bool automationUnlock = TryGetMinerUnlock(node, out _);

            if (maxed)
            {
                button.Text = node.PurchaseMode == "repeatable"
                    ? $"{node.DisplayName}\nMAX {rank}/{node.MaxRank}"
                    : $"{node.DisplayName}\nOWNED";
                button.Modulate = new Color(0.70f, 1.0f, 0.78f);
            }
            else if (automationUnlock)
            {
                button.Text = $"{node.DisplayName}\nBUY & PLACE  |  {cost:N0}";
                button.Modulate = prerequisites
                    ? (affordable ? Colors.White : new Color(0.78f, 0.82f, 0.88f))
                    : new Color(0.52f, 0.55f, 0.62f);
            }
            else if (node.PurchaseMode == "repeatable")
            {
                button.Text = $"{node.DisplayName}\nRank {rank}/{node.MaxRank}  |  {cost:N0}";
                button.Modulate = prerequisites
                    ? (affordable ? Colors.White : new Color(0.78f, 0.82f, 0.88f))
                    : new Color(0.52f, 0.55f, 0.62f);
            }
            else
            {
                button.Text = $"{node.DisplayName}\n{cost:N0} resources";
                button.Modulate = prerequisites
                    ? (affordable ? Colors.White : new Color(0.78f, 0.82f, 0.88f))
                    : new Color(0.52f, 0.55f, 0.62f);
            }

            button.Disabled = maxed || !prerequisites;
        }

        _graph.QueueRedraw();
    }
}

public partial class SkillGraphCanvas : Control
{
    private SkillTreeService _skills = null!;
    private WorldProfile _profile = null!;

    public void Initialize(SkillTreeService skills, WorldProfile profile)
    {
        _skills = skills;
        _profile = profile;
    }

    public override void _Draw()
    {
        if (_skills is null) return;

        Color grid = new(0.16f, 0.22f, 0.31f, 0.32f);
        for (float x = 24; x < Size.X; x += 200)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), grid, 1.0f);
        }
        for (float y = 40; y < Size.Y; y += 112)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), grid, 1.0f);
        }

        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            if (!_profile.IsSkillCategoryVisible(node.Category)) continue;
            Vector2 target = NodeCenter(node);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            {
                SkillNodeDefinition sourceNode = _skills.Catalog.Get(prerequisite.NodeId);
                if (!_profile.IsSkillCategoryVisible(sourceNode.Category)) continue;

                Vector2 previous = NodeCenter(sourceNode);
                bool requirementMet = _skills.GetRank(prerequisite.NodeId) >= prerequisite.RequiredRank;
                Color color = requirementMet
                    ? new Color(0.30f, 0.78f, 0.94f)
                    : new Color(0.28f, 0.32f, 0.40f);

                foreach (SkillRoutePoint routePoint in prerequisite.Route)
                {
                    Vector2 next = RoutePointPosition(routePoint);
                    DrawLine(previous, next, color, 3.0f, true);
                    previous = next;
                }

                DrawLine(previous, target, color, 3.0f, true);
            }
        }
    }

    // Reserve two columns to the left so authored negative grid coordinates can be used for early
    // tutorial branches without placing buttons outside the canvas.
    private const float GridOriginX = 424.0f;

    public static Vector2 NodePosition(SkillNodeDefinition node)
        => new(GridOriginX + node.GridX * 200, 40 + node.GridY * 112);

    public static Vector2 RequiredSize(SkillTreeCatalog catalog, WorldProfile profile)
    {
        int maxX = 0;
        int maxY = 0;
        int minX = 0;
        foreach (SkillNodeDefinition node in catalog.Nodes.Values)
        {
            if (!profile.IsSkillCategoryVisible(node.Category)) continue;
            maxX = Math.Max(maxX, node.GridX);
            minX = Math.Min(minX, node.GridX);
            maxY = Math.Max(maxY, node.GridY);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            foreach (SkillRoutePoint point in prerequisite.Route)
            {
                maxX = Math.Max(maxX, point.GridX);
                minX = Math.Min(minX, point.GridX);
                maxY = Math.Max(maxY, point.GridY);
            }
        }

        return new Vector2(
            GridOriginX + (maxX + 1) * 200 + 190,
            40 + (maxY + 1) * 112 + 110);
    }

    private static Vector2 NodeCenter(SkillNodeDefinition node)
        => NodePosition(node) + new Vector2(87, 41);

    private static Vector2 RoutePointPosition(SkillRoutePoint point)
        => new(GridOriginX + point.GridX * 200 + 87, 40 + point.GridY * 112 + 41);
}
