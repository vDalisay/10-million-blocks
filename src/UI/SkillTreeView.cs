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
    private SkillGraphCanvas _graph = null!;
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);

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

        var title = new Label
        {
            Text = "SKILL TREE",
            Position = new Vector2(32, 24),
        };
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

        _graph = new SkillGraphCanvas
        {
            Position = new Vector2(220, 80),
            Size = new Vector2(1000, 590),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _graph.Initialize(_skills);
        _root.AddChild(_graph);

        BuildButtons();
        Refresh();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

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
        _root.Visible = true;
        _manual.InputEnabled = false;
        Refresh();
    }

    public void Close()
    {
        _root.Visible = false;
        _manual.InputEnabled = true;
    }

    private void BuildButtons()
    {
        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            var button = new Button
            {
                Position = SkillGraphCanvas.NodePosition(node),
                Size = new Vector2(174, 82),
                TooltipText = node.Description,
            };
            string id = node.Id;
            button.Pressed += () => Purchase(id);
            _graph.AddChild(button);
            _buttons.Add(id, button);
        }
    }

    private void Purchase(string skillId)
    {
        SkillPurchaseResult result = _skills.Purchase(skillId);
        if (result.Success)
        {
            _feedback.Text = $"Purchased {_skills.Catalog.Get(skillId).DisplayName}.";
        }
        else
        {
            _feedback.Text = result.Failure switch
            {
                SkillPurchaseFailure.InsufficientResources => "Not enough resources.",
                SkillPurchaseFailure.MissingPrerequisite => "Purchase prerequisite skills first.",
                SkillPurchaseFailure.MaxRank => "Skill is already maxed.",
                _ => "Skill could not be purchased.",
            };
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_resources is null) return;
        _resources.Text = $"Resources: {_mining.Currency:N0}   |   Manual: {_skills.Derived.ManualBlocksPerClick} blocks/click   |   Miner speed: x{_skills.Derived.MinerRateMultiplier:0.##}";

        foreach ((string id, Button button) in _buttons)
        {
            SkillNodeDefinition node = _skills.Catalog.Get(id);
            int rank = _skills.GetRank(id);
            bool maxed = rank >= node.MaxRank;
            bool prerequisites = _skills.PrerequisitesMet(node);
            long cost = checked(node.Cost * (rank + 1L));

            button.Text = maxed
                ? $"{node.DisplayName}\nOWNED"
                : $"{node.DisplayName}\n{cost:N0} resources";
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

        foreach (SkillNodeDefinition node in _skills.Catalog.Nodes.Values)
        {
            Vector2 to = NodePosition(node) + new Vector2(87, 41);
            foreach (string prerequisiteId in node.PrerequisiteNodeIds)
            {
                SkillNodeDefinition prerequisite = _skills.Catalog.Get(prerequisiteId);
                Vector2 from = NodePosition(prerequisite) + new Vector2(87, 41);
                bool owned = _skills.GetRank(prerequisiteId) > 0;
                DrawLine(
                    from,
                    to,
                    owned ? new Color(0.30f, 0.78f, 0.94f) : new Color(0.28f, 0.32f, 0.40f),
                    3.0f,
                    true);
            }
        }
    }

    public static Vector2 NodePosition(SkillNodeDefinition node)
        => new(24 + node.GridX * 200, 40 + node.GridY * 112);
}
