using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Tools.WorldAuthoring;

/// <summary>
/// Advanced authoring controls are a child node rather than more lifecycle code on WorldAuthoringRoot.
/// They operate entirely through the root's authoring API and presentation-only WorldView filters.
/// </summary>
public partial class WorldAuthoringAdvancedTools : Node
{
    private WorldAuthoringRoot _root = null!;
    private CheckBox _sliceEnabled = null!;
    private OptionButton _sliceAxis = null!;
    private SpinBox _sliceCoordinate = null!;
    private CheckBox _sliceKeepLower = null!;
    private readonly Dictionary<string, CheckBox> _tagToggles = new(StringComparer.Ordinal);
    private OptionButton _shape = null!;
    private SpinBox _centerX = null!;
    private SpinBox _centerY = null!;
    private SpinBox _centerZ = null!;
    private SpinBox _size = null!;
    private OptionButton _paintBlock = null!;
    private CheckBox _carve = null!;
    private SpinBox _freezeVersion = null!;
    private Label _status = null!;
    private IReadOnlyList<(string Id, string DisplayName)> _paintBlocks = Array.Empty<(string, string)>();
    private double _refreshPulse;
    private ulong _lastViewId;

    public override void _Ready()
    {
        _root = GetParent<WorldAuthoringRoot>();
        BuildUi();
        RefreshBounds(force: true);
        ApplyPresentationFilters();
    }

    public override void _Process(double delta)
    {
        _refreshPulse += Math.Max(0.0, delta);
        if (_refreshPulse < 0.25) return;
        _refreshPulse = 0.0;

        RefreshBounds(force: false);
        WorldView? view = _root.CurrentAuthoringWorldView();
        if (view is null) return;
        ulong id = view.GetInstanceId();
        if (id != _lastViewId)
        {
            _lastViewId = id;
            ApplyPresentationFilters();
        }
        else
        {
            // Renderer chunk roots are cache objects and edited previews replace them. Reapplying is
            // cheap at authoring scale and keeps filters stable across rebuilds.
            view.RefreshAuthoringPresentationFilters();
        }
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "WorldAuthoringAdvancedCanvas", Layer = 56 };
        AddChild(layer);
        var rootControl = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rootControl);

        var panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = -350.0f,
            OffsetTop = -360.0f,
            OffsetRight = -16.0f,
            OffsetBottom = -16.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        rootControl.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddChild(scroll);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(column);

        var title = new Label { Text = "ADVANCED AUTHORING" };
        title.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(title);

        column.AddChild(new Label { Text = "Cross-section / slice" });
        var sliceRow = new HBoxContainer();
        sliceRow.AddThemeConstantOverride("separation", 5);
        column.AddChild(sliceRow);
        _sliceEnabled = new CheckBox { Text = "Slice" };
        _sliceEnabled.Toggled += _ => ApplyPresentationFilters();
        sliceRow.AddChild(_sliceEnabled);
        _sliceAxis = new OptionButton { CustomMinimumSize = new Vector2(70, 0) };
        foreach (string axis in new[] { "X", "Y", "Z" }) _sliceAxis.AddItem(axis);
        _sliceAxis.Selected = 1;
        _sliceAxis.ItemSelected += _ => ApplyPresentationFilters();
        sliceRow.AddChild(_sliceAxis);
        _sliceCoordinate = MakeInteger(-64, 64, 0);
        _sliceCoordinate.ValueChanged += _ => ApplyPresentationFilters();
        sliceRow.AddChild(_sliceCoordinate);
        _sliceKeepLower = new CheckBox { Text = "≤ plane", ButtonPressed = true };
        _sliceKeepLower.Toggled += _ => ApplyPresentationFilters();
        sliceRow.AddChild(_sliceKeepLower);

        column.AddChild(new Label { Text = "Presentation categories" });
        var visibilityGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddChild(visibilityGrid);
        AddTagToggle(visibilityGrid, "water", "Water");
        AddTagToggle(visibilityGrid, "tree", "Trees");
        AddTagToggle(visibilityGrid, "ore", "Ores + gems");
        AddTagToggle(visibilityGrid, "sand", "Soft surface");

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Volume edit" });
        _shape = new OptionButton();
        foreach (string shape in new[] { "Box", "Sphere", "Plane" }) _shape.AddItem(shape);
        column.AddChild(_shape);

        var centerGrid = new GridContainer { Columns = 6, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        centerGrid.AddChild(new Label { Text = "X" });
        _centerX = MakeInteger(-64, 64, 0);
        centerGrid.AddChild(_centerX);
        centerGrid.AddChild(new Label { Text = "Y" });
        _centerY = MakeInteger(-64, 64, 0);
        centerGrid.AddChild(_centerY);
        centerGrid.AddChild(new Label { Text = "Z" });
        _centerZ = MakeInteger(-64, 64, 0);
        centerGrid.AddChild(_centerZ);
        column.AddChild(centerGrid);

        var sizeRow = new HBoxContainer();
        sizeRow.AddChild(new Label { Text = "Radius / half-size" });
        _size = MakeInteger(0, 24, 1);
        _size.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sizeRow.AddChild(_size);
        column.AddChild(sizeRow);

        _paintBlock = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _paintBlocks = _root.AuthoringPaintBlocks;
        foreach ((string _, string displayName) in _paintBlocks) _paintBlock.AddItem(displayName);
        column.AddChild(_paintBlock);

        _carve = new CheckBox { Text = "Carve / force empty" };
        _carve.Toggled += enabled => _paintBlock.Disabled = enabled;
        column.AddChild(_carve);

        var apply = new Button { Text = "Apply volume edit", CustomMinimumSize = new Vector2(0, 34) };
        apply.Pressed += ApplyVolumeEdit;
        column.AddChild(apply);

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Freeze for Shipping" });
        var freezeRow = new HBoxContainer();
        freezeRow.AddChild(new Label { Text = "World version" });
        _freezeVersion = MakeInteger(1, 9999, 1);
        _freezeVersion.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        freezeRow.AddChild(_freezeVersion);
        column.AddChild(freezeRow);

        var freeze = new Button
        {
            Text = "Freeze immutable version",
            CustomMinimumSize = new Vector2(0, 36),
            TooltipText = "Runs exact metrics/hash validation and writes a new versioned manifest. Existing frozen versions are never overwritten.",
        };
        freeze.Pressed += FreezeForShipping;
        column.AddChild(freeze);

        _status = new Label
        {
            Text = "Slice/category controls are presentation-only. Volume edits are sparse deterministic overrides.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_status);
    }

    private void AddTagToggle(GridContainer grid, string tag, string label)
    {
        var toggle = new CheckBox { Text = label, ButtonPressed = true };
        toggle.Toggled += _ => ApplyPresentationFilters();
        _tagToggles[tag] = toggle;
        grid.AddChild(toggle);
    }

    private static SpinBox MakeInteger(int minimum, int maximum, int value)
        => new()
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = 1,
            Value = value,
            CustomMinimumSize = new Vector2(72, 30),
        };

    private void RefreshBounds(bool force)
    {
        int max = Math.Max(1, _root.AuthoringMaxCoordinate);
        foreach (SpinBox field in new[] { _sliceCoordinate, _centerX, _centerY, _centerZ })
        {
            if (!force && Math.Abs(field.MaxValue - max) < 0.5 && Math.Abs(field.MinValue + max) < 0.5) continue;
            field.MinValue = -max;
            field.MaxValue = max;
            field.Value = Math.Clamp(field.Value, -max, max);
        }
    }

    private void ApplyPresentationFilters()
    {
        WorldView? view = _root.CurrentAuthoringWorldView();
        if (view is null) return;
        _lastViewId = view.GetInstanceId();

        view.ResetAuthoringPresentationFilters();
        foreach ((string tag, CheckBox toggle) in _tagToggles)
        {
            view.SetAuthoringTagVisible(tag, toggle.ButtonPressed);
        }
        view.ConfigureAuthoringSlice(
            _sliceEnabled.ButtonPressed,
            _sliceAxis.Selected,
            checked((int)Math.Round(_sliceCoordinate.Value)),
            _sliceKeepLower.ButtonPressed);
    }

    private void ApplyVolumeEdit()
    {
        try
        {
            var center = new Vector3I(
                checked((int)Math.Round(_centerX.Value)),
                checked((int)Math.Round(_centerY.Value)),
                checked((int)Math.Round(_centerZ.Value)));
            int size = checked((int)Math.Round(_size.Value));
            bool carve = _carve.ButtonPressed;
            string blockId = _paintBlocks.Count == 0
                ? "dirt"
                : _paintBlocks[Math.Clamp(_paintBlock.Selected, 0, _paintBlocks.Count - 1)].Id;

            int changed = _shape.Selected switch
            {
                0 => _root.ApplyAuthoringBox(center, new Vector3I(size, size, size), blockId, carve),
                1 => _root.ApplyAuthoringSphere(center, Math.Max(1, size), blockId, carve),
                _ => _root.ApplyAuthoringPlane(_sliceAxis.Selected, AxisValue(center, _sliceAxis.Selected), Math.Max(1, size), blockId, carve),
            };
            _status.Text = $"Volume edit changed {changed:N0} voxel overrides.";
            ApplyPresentationFilters();
        }
        catch (Exception exception)
        {
            _status.Text = "Volume edit failed: " + exception.Message;
        }
    }

    private void FreezeForShipping()
    {
        try
        {
            int version = checked((int)Math.Round(_freezeVersion.Value));
            FrozenWorldManifest manifest = _root.FreezeCurrentEditedCandidate(version);
            _status.Text =
                $"Frozen {manifest.WorldId} v{manifest.WorldVersion}: {manifest.MineableBlocks:N0} blocks, " +
                $"{manifest.TreeCount:N0} trees, hash {manifest.ContentHash[..12]}….";
        }
        catch (Exception exception)
        {
            _status.Text = "Freeze refused: " + exception.Message;
        }
    }

    private static int AxisValue(Vector3I coordinate, int axis)
        => axis switch
        {
            0 => coordinate.X,
            1 => coordinate.Y,
            _ => coordinate.Z,
        };
}
