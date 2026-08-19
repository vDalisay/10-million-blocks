using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.UI;

public partial class SkillTreeView : CanvasLayer
{
    private SkillTreeService _skills = null!;
    private MiningService _mining = null!;
    private ManualMiningController _manual = null!;
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

        // The authored tree is no longer constrained to the first few grid rows. Keep the graph data
        // coordinates exactly as authored and put the runtime canvas in a scroll container instead of
        // silently clipping late-game branches off-screen.
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
            CustomMinimumSize = SkillGraphCanvas.RequiredSize(_skills.Catalog),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _graph.Initialize(_skills);
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
        if (node.Prerequisites.Count == 0) return node.Description;
        var requirements = new List<string>();
        foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
        {
            requirements.Add($"{_skills.Catalog.Get(prerequisite.NodeId).DisplayName} rank {prerequisite.RequiredRank}");
        }
        return node.Description + "\nRequires: " + string.Join(", ", requirements);
    }

    private void Purchase(string skillId)
    {
        SkillPurchaseResult result = _skills.Purchase(skillId);
        if (result.Success)
        {
            _feedback.Text = $"Purchased {_skills.Catalog.Get(skillId).DisplayName} rank {result.NewRank}.";
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

    private void Refresh()
    {
        if (_resources is null) return;
        _resources.Text =
            $"Resources: {_mining.Currency:N0}   |   Manual: {_skills.Derived.ManualBlocksPerClick}/click" +
            $"   |   Drill speed: x{_skills.Derived.MinerRateMultiplier:0.##}" +
            $"   |   Shovel speed: x{_skills.Derived.ShovelRateMultiplier:0.##}";

        foreach ((string id, Button button) in _buttons)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(id);
            int rank = _skills.GetRank(id);
            bool maxed = rank >= node.MaxRank;
            bool prerequisites = _skills.PrerequisitesMet(node);
            long cost = checked(node.Cost * (rank + 1L));
            bool affordable = _mining.Currency >= cost;

            if (maxed)
            {
                button.Text = node.PurchaseMode == "repeatable"
                    ? $"{node.DisplayName}\nMAX {rank}/{node.MaxRank}"
                    : $"{node.DisplayName}\nOWNED";
                button.Modulate = new Color(0.70f, 1.0f, 0.78f);
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

    public void Initialize(SkillTreeService skills) => _skills = skills;

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
            Vector2 target = NodeCenter(node);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            {
                SkillNodeDefinition sourceNode = _skills.Catalog.Get(prerequisite.NodeId);
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

    public static Vector2 NodePosition(SkillNodeDefinition node)
        => new(24 + node.GridX * 200, 40 + node.GridY * 112);

    public static Vector2 RequiredSize(SkillTreeCatalog catalog)
    {
        int maxX = 0;
        int maxY = 0;
        foreach (SkillNodeDefinition node in catalog.Nodes.Values)
        {
            maxX = Math.Max(maxX, node.GridX);
            maxY = Math.Max(maxY, node.GridY);
            foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
            foreach (SkillRoutePoint point in prerequisite.Route)
            {
                maxX = Math.Max(maxX, point.GridX);
                maxY = Math.Max(maxY, point.GridY);
            }
        }

        return new Vector2(
            24 + (maxX + 1) * 200 + 190,
            40 + (maxY + 1) * 112 + 110);
    }

    private static Vector2 NodeCenter(SkillNodeDefinition node)
        => NodePosition(node) + new Vector2(87, 41);

    private static Vector2 RoutePointPosition(SkillRoutePoint point)
        => new(24 + point.GridX * 200 + 87, 40 + point.GridY * 112 + 41);
}
