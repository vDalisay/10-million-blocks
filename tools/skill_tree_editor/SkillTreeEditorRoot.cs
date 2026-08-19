using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Tools.SkillTreeEditor;

public sealed class SkillEditorEdgeRef
{
    public SkillNodeDefinition TargetNode { get; init; } = null!;
    public SkillPrerequisiteDefinition Prerequisite { get; init; } = null!;
}

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
    private SkillEditorEdgeRef? _selectedEdge;
    private Label _status = null!;
    private LineEdit _id = null!;
    private LineEdit _name = null!;
    private LineEdit _category = null!;
    private LineEdit _cost = null!;
    private LineEdit _maxRank = null!;
    private OptionButton _purchaseMode = null!;
    private Label _prerequisiteSummary = null!;
    private TextEdit _description = null!;
    private TextEdit _effects = null!;
    private Label _edgeInfo = null!;
    private LineEdit _edgeRequiredRank = null!;

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
            OffsetRight = 1020,
            OffsetBottom = 46,
        };
        toolbar.AddThemeConstantOverride("separation", 8);
        AddChild(toolbar);

        AddToolbarButton(toolbar, "Reload", Reload);
        AddToolbarButton(toolbar, "+ Node", AddNode);
        AddToolbarButton(toolbar, "Duplicate", DuplicateSelected);
        AddToolbarButton(toolbar, "Delete Node", DeleteSelected);
        AddToolbarButton(toolbar, "Connect", BeginConnect);
        AddToolbarButton(toolbar, "Delete Line", DeleteSelectedEdge);
        AddToolbarButton(toolbar, "Clear Route", ClearSelectedRoute);
        AddToolbarButton(toolbar, "Save + Validate", Save);

        _status = new Label
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -360,
            OffsetTop = 16,
            OffsetRight = -12,
            OffsetBottom = 44,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AddChild(_status);

        var canvasPanel = new PanelContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 12,
            OffsetTop = 56,
            OffsetRight = -382,
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
        _canvas.EdgeSelected += SelectEdge;
        _canvas.LayoutChanged += () => _status.Text = "Graph changed - save when ready";
        _canvas.ConnectionStatusChanged += message => _status.Text = message;

        var inspectorPanel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -370,
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
        column.AddThemeConstantOverride("separation", 5);
        margin.AddChild(column);

        var header = new Label { Text = "NODE INSPECTOR" };
        header.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(header);

        _id = AddField(column, "Stable ID");
        _name = AddField(column, "Display name");
        _category = AddField(column, "Category");
        _cost = AddField(column, "Base cost");

        column.AddChild(new Label { Text = "Purchase type" });
        _purchaseMode = new OptionButton();
        _purchaseMode.AddItem("One time", 0);
        _purchaseMode.AddItem("Repeatable", 1);
        column.AddChild(_purchaseMode);

        _maxRank = AddField(column, "Max rank (repeatable nodes can be bought multiple times)");

        column.AddChild(new Label { Text = "Prerequisites" });
        _prerequisiteSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 42),
        };
        column.AddChild(_prerequisiteSummary);

        column.AddChild(new Label { Text = "Description" });
        _description = new TextEdit { CustomMinimumSize = new Vector2(0, 72) };
        column.AddChild(_description);

        column.AddChild(new Label { Text = "Effects JSON" });
        _effects = new TextEdit { CustomMinimumSize = new Vector2(0, 130) };
        column.AddChild(_effects);

        var apply = new Button { Text = "Apply Node Changes" };
        apply.Pressed += ApplyInspectorAndRefresh;
        column.AddChild(apply);

        var edgeHeader = new Label { Text = "LINE / PREREQUISITE INSPECTOR" };
        edgeHeader.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(edgeHeader);

        _edgeInfo = new Label
        {
            Text = "Click a prerequisite line to select it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44),
        };
        column.AddChild(_edgeInfo);
        _edgeRequiredRank = AddField(column, "Required source rank");
        var applyEdge = new Button { Text = "Apply Line Requirement" };
        applyEdge.Pressed += ApplyEdgeInspector;
        column.AddChild(applyEdge);

        column.AddChild(new Label
        {
            Text = "Canvas: drag nodes with LMB. MMB pans. Wheel zooms. Use Connect, then click prerequisite -> dependent. Click a line to select it; click empty grid cells to add snapped route bends. RMB a bend to remove it.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
    }

    private static void AddToolbarButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(100, 32) };
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
            _selectedEdge = null;
            _canvas.SetDocument(_document);
            ClearInspector();
            ClearEdgeInspector();
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
            ApplyEdgeInspector(silent: true);
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
            PurchaseMode = "once",
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
            PurchaseMode = _selected.PurchaseMode,
            Prerequisites = _selected.Prerequisites.Select(ClonePrerequisite).ToList(),
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
            node.Prerequisites.RemoveAll(prerequisite => prerequisite.NodeId == deletedId);
        }

        _selected = null;
        _selectedEdge = null;
        _canvas.SetDocument(_document);
        ClearInspector();
        ClearEdgeInspector();
        _status.Text = $"Deleted {deletedId}";
    }

    private void BeginConnect()
    {
        ApplyInspector();
        _canvas.BeginConnectionMode();
        _status.Text = "Connect mode: click the prerequisite node, then the dependent node.";
    }

    private void DeleteSelectedEdge()
    {
        if (_selectedEdge is null) return;
        _selectedEdge.TargetNode.Prerequisites.Remove(_selectedEdge.Prerequisite);
        _selectedEdge = null;
        _canvas.SetSelectedEdge(null);
        ClearEdgeInspector();
        _canvas.QueueRedraw();
        _status.Text = "Deleted prerequisite line";
    }

    private void ClearSelectedRoute()
    {
        if (_selectedEdge is null) return;
        _selectedEdge.Prerequisite.Route.Clear();
        _canvas.QueueRedraw();
        _status.Text = "Cleared line route; connection now uses a direct segment";
    }

    private void SelectNode(SkillNodeDefinition node)
    {
        ApplyInspector();
        _selected = node;
        _id.Text = node.Id;
        _name.Text = node.DisplayName;
        _category.Text = node.Category;
        _cost.Text = node.Cost.ToString(CultureInfo.InvariantCulture);
        _purchaseMode.Selected = node.PurchaseMode == "repeatable" ? 1 : 0;
        _maxRank.Text = node.MaxRank.ToString(CultureInfo.InvariantCulture);
        _description.Text = node.Description;
        _effects.Text = JsonSerializer.Serialize(node.Effects, _jsonOptions);
        RefreshPrerequisiteSummary();
        _canvas.SetSelected(node);
    }

    private void SelectEdge(SkillEditorEdgeRef edge)
    {
        _selectedEdge = edge;
        _canvas.SetSelectedEdge(edge);
        SkillNodeDefinition? source = _document.Nodes.FirstOrDefault(node => node.Id == edge.Prerequisite.NodeId);
        string sourceName = source?.DisplayName ?? edge.Prerequisite.NodeId;
        _edgeInfo.Text = $"{sourceName} -> {edge.TargetNode.DisplayName}\nRoute bends: {edge.Prerequisite.Route.Count}";
        _edgeRequiredRank.Text = edge.Prerequisite.RequiredRank.ToString(CultureInfo.InvariantCulture);
        _status.Text = "Line selected. LMB empty grid to add a bend; RMB a bend to remove it.";
    }

    private void ApplyInspectorAndRefresh()
    {
        try
        {
            ApplyInspector();
            _canvas.SetDocument(_document);
            if (_selected is not null) _canvas.SetSelected(_selected);
            _status.Text = "Node changes applied";
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

        bool repeatable = _purchaseMode.Selected == 1;
        _selected.PurchaseMode = repeatable ? "repeatable" : "once";
        int parsedMaxRank = int.TryParse(_maxRank.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxRank)
            ? maxRank
            : throw new InvalidOperationException("Max rank must be an integer.");
        _selected.MaxRank = repeatable ? Math.Max(2, parsedMaxRank) : 1;

        _selected.Effects = JsonSerializer.Deserialize<List<SkillEffectDefinition>>(_effects.Text, _jsonOptions)
            ?? new List<SkillEffectDefinition>();

        if (newId != oldId)
        {
            foreach (SkillNodeDefinition node in _document.Nodes)
            {
                foreach (SkillPrerequisiteDefinition prerequisite in node.Prerequisites)
                {
                    if (prerequisite.NodeId == oldId) prerequisite.NodeId = newId;
                }
            }
        }

        RefreshPrerequisiteSummary();
    }

    private void ApplyEdgeInspector() => ApplyEdgeInspector(silent: false);

    private void ApplyEdgeInspector(bool silent)
    {
        if (_selectedEdge is null) return;
        if (!int.TryParse(_edgeRequiredRank.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank))
        {
            if (!silent) _status.Text = "LINE ERROR: Required rank must be an integer.";
            return;
        }

        _selectedEdge.Prerequisite.RequiredRank = Math.Max(1, rank);
        _canvas.QueueRedraw();
        RefreshPrerequisiteSummary();
        if (!silent) _status.Text = "Line requirement applied";
    }

    private void RefreshPrerequisiteSummary()
    {
        if (_selected is null)
        {
            _prerequisiteSummary.Text = string.Empty;
            return;
        }

        if (_selected.Prerequisites.Count == 0)
        {
            _prerequisiteSummary.Text = "None";
            return;
        }

        _prerequisiteSummary.Text = string.Join(", ", _selected.Prerequisites.Select(prerequisite =>
            $"{prerequisite.NodeId} @ rank {prerequisite.RequiredRank}"));
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
        _purchaseMode.Selected = 0;
        _maxRank.Text = "1";
        _prerequisiteSummary.Text = string.Empty;
        _description.Text = string.Empty;
        _effects.Text = "[]";
        _canvas.SetSelected(null);
    }

    private void ClearEdgeInspector()
    {
        if (_edgeInfo is null) return;
        _edgeInfo.Text = "Click a prerequisite line to select it.";
        _edgeRequiredRank.Text = "1";
    }

    private static SkillPrerequisiteDefinition ClonePrerequisite(SkillPrerequisiteDefinition source)
        => new()
        {
            NodeId = source.NodeId,
            RequiredRank = source.RequiredRank,
            Route = source.Route.Select(point => new SkillRoutePoint
            {
                GridX = point.GridX,
                GridY = point.GridY,
            }).ToList(),
        };

    private static string FirstLine(string value)
    {
        int index = value.IndexOf('\n');
        return index < 0 ? value : value[..index];
    }
}

public sealed class SkillEditorDocument
{
    public int SchemaVersion { get; set; } = 2;
    public int ContentVersion { get; set; } = 2;
    public List<SkillNodeDefinition> Nodes { get; set; } = new();
}

public partial class SkillEditorCanvas : Control
{
    public event Action<SkillNodeDefinition>? NodeSelected;
    public event Action<SkillEditorEdgeRef>? EdgeSelected;
    public event Action? LayoutChanged;
    public event Action<string>? ConnectionStatusChanged;

    private const float CellX = 200.0f;
    private const float CellY = 112.0f;
    private static readonly Vector2 Origin = new(36, 40);
    private static readonly Vector2 CardSize = new(172, 72);

    private readonly Dictionary<SkillNodeDefinition, SkillEditorNodeCard> _cards = new();
    private SkillEditorDocument _document = new();
    private SkillNodeDefinition? _selected;
    private SkillEditorEdgeRef? _selectedEdge;
    private SkillNodeDefinition? _connectionSource;
    private bool _connectionMode;
    private Vector2 _viewOffset = new(20, 20);
    private float _zoom = 1.0f;
    private bool _panning;

    public void SetDocument(SkillEditorDocument document)
    {
        _document = document;
        _connectionMode = false;
        _connectionSource = null;
        _selectedEdge = null;
        foreach (SkillEditorNodeCard card in _cards.Values) card.QueueFree();
        _cards.Clear();

        foreach (SkillNodeDefinition node in document.Nodes)
        {
            var card = new SkillEditorNodeCard
            {
                Node = node,
                Text = NodeCardText(node),
                Size = CardSize,
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

    public void SetSelectedEdge(SkillEditorEdgeRef? edge)
    {
        _selectedEdge = edge;
        QueueRedraw();
    }

    public void BeginConnectionMode()
    {
        _connectionMode = true;
        _connectionSource = null;
        _selectedEdge = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawGrid();

        foreach (SkillNodeDefinition targetNode in _document.Nodes)
        {
            foreach (SkillPrerequisiteDefinition prerequisite in targetNode.Prerequisites)
            {
                SkillNodeDefinition? sourceNode = _document.Nodes.FirstOrDefault(candidate => candidate.Id == prerequisite.NodeId);
                if (sourceNode is null) continue;

                bool selected = _selectedEdge is not null
                    && ReferenceEquals(_selectedEdge.TargetNode, targetNode)
                    && ReferenceEquals(_selectedEdge.Prerequisite, prerequisite);
                Color color = selected ? new Color(1.0f, 0.77f, 0.24f) : new Color(0.33f, 0.58f, 0.72f);
                float width = selected ? 5.0f : 3.0f;

                Vector2 previous = CardCenter(sourceNode);
                foreach (SkillRoutePoint routePoint in prerequisite.Route)
                {
                    Vector2 next = RoutePointPosition(routePoint);
                    DrawLine(previous, next, color, width, true);
                    if (selected)
                    {
                        DrawCircle(next, 7.0f, new Color(1.0f, 0.86f, 0.45f));
                    }
                    previous = next;
                }
                DrawLine(previous, CardCenter(targetNode), color, width, true);
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

            if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                SkillEditorEdgeRef? hit = FindEdgeAt(button.Position, 10.0f);
                if (hit is not null)
                {
                    _selectedEdge = hit;
                    EdgeSelected?.Invoke(hit);
                    QueueRedraw();
                    AcceptEvent();
                    return;
                }

                if (_selectedEdge is not null && !_connectionMode)
                {
                    InsertRoutePoint(_selectedEdge, button.Position);
                    LayoutChanged?.Invoke();
                    QueueRedraw();
                    AcceptEvent();
                    return;
                }
            }

            if (button.Pressed && button.ButtonIndex == MouseButton.Right && _selectedEdge is not null)
            {
                if (RemoveRoutePointNear(_selectedEdge, button.Position))
                {
                    LayoutChanged?.Invoke();
                    QueueRedraw();
                    AcceptEvent();
                    return;
                }
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
        if (_connectionMode)
        {
            HandleConnectionClick(card.Node);
            return;
        }

        SetSelected(card.Node);
        NodeSelected?.Invoke(card.Node);
    }

    private void HandleConnectionClick(SkillNodeDefinition node)
    {
        if (_connectionSource is null)
        {
            _connectionSource = node;
            ConnectionStatusChanged?.Invoke($"Connection source: {node.DisplayName}. Now click the dependent node.");
            return;
        }

        if (ReferenceEquals(_connectionSource, node))
        {
            ConnectionStatusChanged?.Invoke("A node cannot depend on itself. Choose another dependent node.");
            return;
        }

        SkillPrerequisiteDefinition? existing = node.Prerequisites.FirstOrDefault(prerequisite => prerequisite.NodeId == _connectionSource.Id);
        if (existing is null)
        {
            existing = new SkillPrerequisiteDefinition
            {
                NodeId = _connectionSource.Id,
                RequiredRank = 1,
            };
            node.Prerequisites.Add(existing);
            LayoutChanged?.Invoke();
        }

        _selectedEdge = new SkillEditorEdgeRef
        {
            TargetNode = node,
            Prerequisite = existing,
        };
        EdgeSelected?.Invoke(_selectedEdge);
        _connectionMode = false;
        _connectionSource = null;
        ConnectionStatusChanged?.Invoke("Connection created. Click empty grid cells to route the selected line.");
        QueueRedraw();
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

    private SkillEditorEdgeRef? FindEdgeAt(Vector2 position, float threshold)
    {
        SkillEditorEdgeRef? best = null;
        float bestDistance = threshold;

        foreach (SkillNodeDefinition targetNode in _document.Nodes)
        {
            foreach (SkillPrerequisiteDefinition prerequisite in targetNode.Prerequisites)
            {
                SkillNodeDefinition? sourceNode = _document.Nodes.FirstOrDefault(candidate => candidate.Id == prerequisite.NodeId);
                if (sourceNode is null) continue;

                Vector2 previous = CardCenter(sourceNode);
                foreach (SkillRoutePoint routePoint in prerequisite.Route)
                {
                    Vector2 next = RoutePointPosition(routePoint);
                    float distance = DistanceToSegment(position, previous, next);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new SkillEditorEdgeRef { TargetNode = targetNode, Prerequisite = prerequisite };
                    }
                    previous = next;
                }

                float finalDistance = DistanceToSegment(position, previous, CardCenter(targetNode));
                if (finalDistance < bestDistance)
                {
                    bestDistance = finalDistance;
                    best = new SkillEditorEdgeRef { TargetNode = targetNode, Prerequisite = prerequisite };
                }
            }
        }

        return best;
    }

    private void InsertRoutePoint(SkillEditorEdgeRef edge, Vector2 screenPosition)
    {
        SkillRoutePoint point = SnapRoutePoint(screenPosition);
        if (edge.Prerequisite.Route.Any(existing => existing.GridX == point.GridX && existing.GridY == point.GridY))
        {
            return;
        }

        SkillNodeDefinition? sourceNode = _document.Nodes.FirstOrDefault(node => node.Id == edge.Prerequisite.NodeId);
        if (sourceNode is null) return;

        var points = new List<Vector2> { CardCenter(sourceNode) };
        points.AddRange(edge.Prerequisite.Route.Select(RoutePointPosition));
        points.Add(CardCenter(edge.TargetNode));

        int insertIndex = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            float distance = DistanceToSegment(screenPosition, points[i], points[i + 1]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                insertIndex = i;
            }
        }

        edge.Prerequisite.Route.Insert(insertIndex, point);
        EdgeSelected?.Invoke(edge);
    }

    private bool RemoveRoutePointNear(SkillEditorEdgeRef edge, Vector2 screenPosition)
    {
        int bestIndex = -1;
        float bestDistance = 15.0f;
        for (int i = 0; i < edge.Prerequisite.Route.Count; i++)
        {
            float distance = screenPosition.DistanceTo(RoutePointPosition(edge.Prerequisite.Route[i]));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;
        edge.Prerequisite.Route.RemoveAt(bestIndex);
        EdgeSelected?.Invoke(edge);
        return true;
    }

    private SkillRoutePoint SnapRoutePoint(Vector2 screenPosition)
    {
        Vector2 logical = (screenPosition - _viewOffset) / _zoom - Origin;
        int gridX = Math.Max(0, (int)Math.Round((logical.X - CardSize.X * 0.5f) / CellX));
        int gridY = Math.Max(0, (int)Math.Round((logical.Y - CardSize.Y * 0.5f) / CellY));
        return new SkillRoutePoint { GridX = gridX, GridY = gridY };
    }

    private void RefreshCardTransforms()
    {
        foreach ((SkillNodeDefinition node, SkillEditorNodeCard card) in _cards)
        {
            Vector2 logical = Origin + new Vector2(node.GridX * CellX, node.GridY * CellY);
            card.Position = _viewOffset + logical * _zoom;
            card.Scale = Vector2.One * _zoom;
            card.Text = NodeCardText(node);
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

    private Vector2 CardCenter(SkillNodeDefinition node)
    {
        if (_cards.TryGetValue(node, out SkillEditorNodeCard? card))
        {
            return card.Position + CardSize * _zoom * 0.5f;
        }

        Vector2 logical = Origin + new Vector2(node.GridX * CellX, node.GridY * CellY) + CardSize * 0.5f;
        return _viewOffset + logical * _zoom;
    }

    private Vector2 RoutePointPosition(SkillRoutePoint point)
    {
        Vector2 logical = Origin
            + new Vector2(point.GridX * CellX, point.GridY * CellY)
            + CardSize * 0.5f;
        return _viewOffset + logical * _zoom;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.0001f) return point.DistanceTo(start);
        float t = Mathf.Clamp((point - start).Dot(segment) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * t);
    }

    private static float PositiveModulo(float value, float divisor)
    {
        float result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static string NodeCardText(SkillNodeDefinition node)
        => node.PurchaseMode == "repeatable"
            ? $"{node.DisplayName}\n{node.Id}  (x{node.MaxRank})"
            : node.DisplayName + "\n" + node.Id;
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
