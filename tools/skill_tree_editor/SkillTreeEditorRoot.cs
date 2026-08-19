using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Tools.SkillTreeEditor;

public partial class SkillTreeEditorRoot : Control
{
    private const string DataPath = "res://data/skills/skill_tree.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private SkillEditorDocument _document = new();
    private SkillEditorCanvas _canvas = null!;
    private SkillNodeDefinition? _selected;
    private Label _status = null!;
    private LineEdit _id = null!;
    private LineEdit _name = null!;
    private LineEdit _category = null!;
    private LineEdit _cost = null!;
    private LineEdit _prerequisites = null!;
    private TextEdit _description = null!;
    private TextEdit _effects = null!;

    public override void _Ready()
    {
        GetWindow().Title = "1 Million Squared - Skill Tree Editor";
        BuildUi();
        Reload();
    }

    private void BuildUi()
    {
        var background = new ColorRect
        {
            Color = new Color(0.025f, 0.03f, 0.045f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var toolbar = new HBoxContainer
        {
            OffsetLeft = 12,
            OffsetTop = 10,
            OffsetRight = 900,
            OffsetBottom = 46,
        };
        toolbar.AddThemeConstantOverride("separation", 8);
        AddChild(toolbar);

        AddToolbarButton(toolbar, "Reload", Reload);
        AddToolbarButton(toolbar, "+ Node", AddNode);
        AddToolbarButton(toolbar, "Duplicate", DuplicateSelected);
        AddToolbarButton(toolbar, "Delete", DeleteSelected);
        AddToolbarButton(toolbar, "Save + Validate", Save);

        _status = new Label
        {
            Position = new Vector2(930, 16),
            Size = new Vector2(330, 28),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AddChild(_status);

        var canvasPanel = new PanelContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 12,
            OffsetTop = 56,
            OffsetRight = -362,
            OffsetBottom = -12,
        };
        AddChild(canvasPanel);

        _canvas = new SkillEditorCanvas
        {
            MouseFilter = MouseFilterEnum.Stop,
            ClipContents = true,
        };
        canvasPanel.AddChild(_canvas);
        _canvas.NodeSelected += SelectNode;
        _canvas.LayoutChanged += () => _status.Text = "Layout changed - save when ready";

        var inspectorPanel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -350,
            OffsetTop = 56,
            OffsetRight = -12,
            OffsetBottom = -12,
        };
        AddChild(inspectorPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        inspectorPanel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        var header = new Label { Text = "NODE INSPECTOR" };
        header.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(header);

        _id = AddField(column, "Stable ID");
        _name = AddField(column, "Display name");
        _category = AddField(column, "Category");
        _cost = AddField(column, "Cost");
        _prerequisites = AddField(column, "Prerequisites (comma-separated IDs)");

        column.AddChild(new Label { Text = "Description" });
        _description = new TextEdit { CustomMinimumSize = new Vector2(0, 90) };
        column.AddChild(_description);

        column.AddChild(new Label { Text = "Effects JSON" });
        _effects = new TextEdit { CustomMinimumSize = new Vector2(0, 170) };
        column.AddChild(_effects);

        var apply = new Button { Text = "Apply Inspector Changes" };
        apply.Pressed += ApplyInspectorAndRefresh;
        column.AddChild(apply);

        column.AddChild(new Label
        {
            Text = "Canvas: drag nodes with LMB. MMB drag pans. Mouse wheel zooms. Nodes snap to the skill grid on release.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
    }

    private static void AddToolbarButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(105, 32) };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private static LineEdit AddField(Control parent, string label)
    {
        parent.AddChild(new Label { Text = label });
        var edit = new LineEdit();
        parent.AddChild(edit);
        return edit;
    }

    private void Reload()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(DataPath);
            string json = System.IO.File.ReadAllText(path);
            _document = JsonSerializer.Deserialize<SkillEditorDocument>(json, _jsonOptions)
                ?? throw new InvalidOperationException("Skill tree file parsed to null.");
            _selected = null;
            _canvas.SetDocument(_document);
            ClearInspector();
            _status.Text = $"Loaded {_document.Nodes.Count} nodes";
        }
        catch (Exception exception)
        {
            _status.Text = "LOAD ERROR: " + exception.Message;
            GD.PushError(exception.ToString());
        }
    }

    private void Save()
    {
        try
        {
            ApplyInspector();
            string json = JsonSerializer.Serialize(_document, _jsonOptions);

            const string tempPath = "user://skill_tree_editor_validation.json";
            string tempAbsolute = ProjectSettings.GlobalizePath(tempPath);
            System.IO.File.WriteAllText(tempAbsolute, json);
            _ = SkillTreeCatalog.Load(tempPath);
            System.IO.File.Delete(tempAbsolute);

            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(DataPath), json);
            _canvas.SetDocument(_document);
            _status.Text = $"Saved + validated {_document.Nodes.Count} nodes";
        }
        catch (Exception exception)
        {
            _status.Text = "VALIDATION ERROR: " + FirstLine(exception.Message);
            GD.PushError(exception.ToString());
        }
    }

    private void AddNode()
    {
        ApplyInspector();
        int suffix = 1;
        string id;
        do id = $"new_skill_{suffix++}";
        while (_document.Nodes.Any(node => node.Id == id));

        (int x, int y) = FindFreeCell();
        var node = new SkillNodeDefinition
        {
            Id = id,
            DisplayName = "New Skill",
            Description = "Describe what this skill changes.",
            GridX = x,
            GridY = y,
            Category = "general",
            Cost = 10,
            MaxRank = 1,
        };
        _document.Nodes.Add(node);
        _canvas.SetDocument(_document);
        SelectNode(node);
        _status.Text = "Added node";
    }

    private void DuplicateSelected()
    {
        if (_selected is null) return;
        ApplyInspector();

        string baseId = _selected.Id + "_copy";
        string id = baseId;
        int suffix = 2;
        while (_document.Nodes.Any(node => node.Id == id)) id = baseId + suffix++;

        var duplicate = new SkillNodeDefinition
        {
            Id = id,
            DisplayName = _selected.DisplayName + " Copy",
            Description = _selected.Description,
            GridX = _selected.GridX + 1,
            GridY = _selected.GridY,
            Category = _selected.Category,
            PrerequisiteNodeIds = new List<string>(_selected.PrerequisiteNodeIds),
            Cost = _selected.Cost,
            MaxRank = _selected.MaxRank,
            Effects = _selected.Effects.Select(effect => new SkillEffectDefinition
            {
                Type = effect.Type,
                Value = effect.Value,
                StringValue = effect.StringValue,
            }).ToList(),
        };

        _document.Nodes.Add(duplicate);
        _canvas.SetDocument(_document);
        SelectNode(duplicate);
        _status.Text = "Duplicated node";
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        string deletedId = _selected.Id;
        _document.Nodes.Remove(_selected);
        foreach (SkillNodeDefinition node in _document.Nodes)
        {
            node.PrerequisiteNodeIds.RemoveAll(id => id == deletedId);
        }

        _selected = null;
        _canvas.SetDocument(_document);
        ClearInspector();
        _status.Text = $"Deleted {deletedId}";
    }

    private void SelectNode(SkillNodeDefinition node)
    {
        ApplyInspector();
        _selected = node;
        _id.Text = node.Id;
        _name.Text = node.DisplayName;
        _category.Text = node.Category;
        _cost.Text = node.Cost.ToString(CultureInfo.InvariantCulture);
        _prerequisites.Text = string.Join(", ", node.PrerequisiteNodeIds);
        _description.Text = node.Description;
        _effects.Text = JsonSerializer.Serialize(node.Effects, _jsonOptions);
        _canvas.SetSelected(node);
    }

    private void ApplyInspectorAndRefresh()
    {
        try
        {
            ApplyInspector();
            _canvas.SetDocument(_document);
            if (_selected is not null) _canvas.SetSelected(_selected);
            _status.Text = "Inspector changes applied";
        }
        catch (Exception exception)
        {
            _status.Text = "EDIT ERROR: " + FirstLine(exception.Message);
        }
    }

    private void ApplyInspector()
    {
        if (_selected is null) return;

        string oldId = _selected.Id;
        string newId = _id.Text.Trim();
        if (string.IsNullOrWhiteSpace(newId)) throw new InvalidOperationException("Stable ID cannot be empty.");

        _selected.Id = newId;
        _selected.DisplayName = _name.Text.Trim();
        _selected.Category = _category.Text.Trim();
        _selected.Description = _description.Text;
        _selected.Cost = long.TryParse(_cost.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long cost)
            ? Math.Max(0, cost)
            : throw new InvalidOperationException("Cost must be an integer.");
        _selected.PrerequisiteNodeIds = _prerequisites.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _selected.Effects = JsonSerializer.Deserialize<List<SkillEffectDefinition>>(_effects.Text, _jsonOptions)
            ?? new List<SkillEffectDefinition>();

        if (newId != oldId)
        {
            foreach (SkillNodeDefinition node in _document.Nodes)
            {
                for (int i = 0; i < node.PrerequisiteNodeIds.Count; i++)
                {
                    if (node.PrerequisiteNodeIds[i] == oldId) node.PrerequisiteNodeIds[i] = newId;
                }
            }
        }
    }

    private (int X, int Y) FindFreeCell()
    {
        var occupied = _document.Nodes.Select(node => (node.GridX, node.GridY)).ToHashSet();
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            if (!occupied.Contains((x, y))) return (x, y);
        }

        return (0, _document.Nodes.Count);
    }

    private void ClearInspector()
    {
        if (_id is null) return;
        _id.Text = string.Empty;
        _name.Text = string.Empty;
        _category.Text = string.Empty;
        _cost.Text = string.Empty;
        _prerequisites.Text = string.Empty;
        _description.Text = string.Empty;
        _effects.Text = "[]";
        _canvas.SetSelected(null);
    }

    private static string FirstLine(string value)
    {
        int index = value.IndexOf('\n');
        return index < 0 ? value : value[..index];
    }
}

public sealed class SkillEditorDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int ContentVersion { get; set; } = 1;
    public List<SkillNodeDefinition> Nodes { get; set; } = new();
}

public partial class SkillEditorCanvas : Control
{
    public event Action<SkillNodeDefinition>? NodeSelected;
    public event Action? LayoutChanged;

    private const float CellX = 200.0f;
    private const float CellY = 112.0f;
    private static readonly Vector2 Origin = new(36, 40);

    private readonly Dictionary<SkillNodeDefinition, SkillEditorNodeCard> _cards = new();
    private SkillEditorDocument _document = new();
    private SkillNodeDefinition? _selected;
    private Vector2 _viewOffset = new(20, 20);
    private float _zoom = 1.0f;
    private bool _panning;

    public void SetDocument(SkillEditorDocument document)
    {
        _document = document;
        foreach (SkillEditorNodeCard card in _cards.Values) card.QueueFree();
        _cards.Clear();

        foreach (SkillNodeDefinition node in document.Nodes)
        {
            var card = new SkillEditorNodeCard
            {
                Node = node,
                Text = node.DisplayName + "\n" + node.Id,
                Size = new Vector2(172, 72),
                TooltipText = node.Description,
            };
            card.Selected += OnCardSelected;
            card.Dragged += OnCardDragged;
            card.DragEnded += OnCardDragEnded;
            AddChild(card);
            _cards.Add(node, card);
        }

        RefreshCardTransforms();
        QueueRedraw();
    }

    public void SetSelected(SkillNodeDefinition? node)
    {
        _selected = node;
        foreach ((SkillNodeDefinition definition, SkillEditorNodeCard card) in _cards)
        {
            card.Modulate = ReferenceEquals(definition, node) ? new Color(0.75f, 0.95f, 1.0f) : Colors.White;
        }
    }

    public override void _Draw()
    {
        DrawGrid();

        foreach (SkillNodeDefinition node in _document.Nodes)
        {
            if (!_cards.TryGetValue(node, out SkillEditorNodeCard? targetCard)) continue;
            Vector2 to = targetCard.Position + targetCard.Size * _zoom * 0.5f;

            foreach (string prerequisiteId in node.PrerequisiteNodeIds)
            {
                SkillNodeDefinition? prerequisite = _document.Nodes.FirstOrDefault(candidate => candidate.Id == prerequisiteId);
                if (prerequisite is null || !_cards.TryGetValue(prerequisite, out SkillEditorNodeCard? sourceCard)) continue;
                Vector2 from = sourceCard.Position + sourceCard.Size * _zoom * 0.5f;
                DrawLine(from, to, new Color(0.33f, 0.58f, 0.72f), 3.0f, true);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle)
            {
                _panning = button.Pressed;
                AcceptEvent();
                return;
            }

            if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(_zoom * 1.12f, button.Position);
                AcceptEvent();
                return;
            }

            if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(_zoom / 1.12f, button.Position);
                AcceptEvent();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion && _panning)
        {
            _viewOffset += motion.Relative;
            RefreshCardTransforms();
            QueueRedraw();
            AcceptEvent();
        }
    }

    private void OnCardSelected(SkillEditorNodeCard card)
    {
        SetSelected(card.Node);
        NodeSelected?.Invoke(card.Node);
    }

    private void OnCardDragged(SkillEditorNodeCard card, Vector2 relative)
    {
        card.Position += relative;
        QueueRedraw();
    }

    private void OnCardDragEnded(SkillEditorNodeCard card)
    {
        Vector2 logical = (card.Position - _viewOffset - Origin * _zoom) / _zoom;
        card.Node.GridX = Math.Max(0, (int)Math.Round(logical.X / CellX));
        card.Node.GridY = Math.Max(0, (int)Math.Round(logical.Y / CellY));
        RefreshCardTransforms();
        QueueRedraw();
        LayoutChanged?.Invoke();
    }

    private void RefreshCardTransforms()
    {
        foreach ((SkillNodeDefinition node, SkillEditorNodeCard card) in _cards)
        {
            Vector2 logical = Origin + new Vector2(node.GridX * CellX, node.GridY * CellY);
            card.Position = _viewOffset + logical * _zoom;
            card.Scale = Vector2.One * _zoom;
        }
    }

    private void SetZoom(float value, Vector2 pivot)
    {
        float old = _zoom;
        _zoom = Mathf.Clamp(value, 0.55f, 1.75f);
        if (Mathf.IsEqualApprox(old, _zoom)) return;

        Vector2 logicalPivot = (pivot - _viewOffset) / old;
        _viewOffset = pivot - logicalPivot * _zoom;
        RefreshCardTransforms();
        QueueRedraw();
    }

    private void DrawGrid()
    {
        float stepX = CellX * _zoom;
        float stepY = CellY * _zoom;
        float startX = PositiveModulo(_viewOffset.X + Origin.X * _zoom, stepX);
        float startY = PositiveModulo(_viewOffset.Y + Origin.Y * _zoom, stepY);
        Color gridColor = new(0.15f, 0.18f, 0.24f, 0.72f);

        for (float x = startX; x < Size.X; x += stepX) DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), gridColor, 1.0f);
        for (float y = startY; y < Size.Y; y += stepY) DrawLine(new Vector2(0, y), new Vector2(Size.X, y), gridColor, 1.0f);
    }

    private static float PositiveModulo(float value, float divisor)
    {
        float result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

public partial class SkillEditorNodeCard : Button
{
    public event Action<SkillEditorNodeCard>? Selected;
    public event Action<SkillEditorNodeCard, Vector2>? Dragged;
    public event Action<SkillEditorNodeCard>? DragEnded;

    public SkillNodeDefinition Node { get; set; } = null!;
    private bool _dragging;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                _dragging = true;
                Selected?.Invoke(this);
            }
            else if (_dragging)
            {
                _dragging = false;
                DragEnded?.Invoke(this);
            }
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragging)
        {
            Dragged?.Invoke(this, motion.Relative);
            AcceptEvent();
        }
    }
}
