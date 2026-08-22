using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Generation;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Tools.WorldAuthoring;

public partial class WorldAuthoringRoot
{
    private enum AuthoringEditMode
    {
        Inspect,
        PaintBlock,
        Carve,
        AddTree,
        RemoveFeature,
        PlaceRedGem,
    }

    private readonly record struct DraftVoxel(bool Present, string BlockId, bool Mineable);
    private readonly record struct DraftFeature(bool Present, string BlockId, Vector3I Normal);
    private readonly record struct DraftCellState(DraftVoxel? Voxel, DraftFeature? Feature);
    private readonly record struct DraftEdit(Vector3I Coordinate, DraftCellState Before, DraftCellState After);

    private readonly Dictionary<Vector3I, DraftVoxel> _draftVoxels = new();
    private readonly Dictionary<Vector3I, DraftFeature> _draftFeatures = new();
    private readonly Stack<DraftEdit> _undoEdits = new();
    private readonly Stack<DraftEdit> _redoEdits = new();
    private readonly List<string> _paintBlockIds = new();

    private OptionButton? _editModePicker;
    private OptionButton? _paintBlockPicker;
    private Label? _editStatus;
    private Button? _undoButton;
    private Button? _redoButton;
    private bool _editingUiBuilt;
    private string _draftBaseHash = string.Empty;
    private string _previewOverridePath = string.Empty;
    private string _savedOverridePath = string.Empty;

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (!_editingUiBuilt && _status is not null && _profiles.Count > 0)
        {
            BuildEditingUi();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_editingUiBuilt
            || _editModePicker is null
            || @event is not InputEventMouseButton button
            || button.ButtonIndex != MouseButton.Left
            || !button.Pressed
            || _camera is null
            || _camera.IsManipulating)
        {
            return;
        }

        if (!TryCurrentPreview(out WorldView view, out VirtualWorld world)) return;

        float rayDistance = world.GetWorldBounds().Size.Length() * 2.5f;
        if (!VoxelRaycaster.TryRaycast(world, _camera.Camera, button.Position, rayDistance, out Vector3I voxel))
        {
            SetEditStatus("No surface voxel under cursor.");
            return;
        }

        EnsureDraftMatchesCurrentCandidate();
        AuthoringEditMode mode = (AuthoringEditMode)_editModePicker.Selected;
        if (mode == AuthoringEditMode.Inspect)
        {
            InspectVoxel(world, voxel);
            GetViewport().SetInputAsHandled();
            return;
        }

        DraftCellState before = CaptureDraftState(voxel);
        bool changed = mode switch
        {
            AuthoringEditMode.PaintBlock => PaintVoxel(voxel, SelectedPaintBlockId()),
            AuthoringEditMode.Carve => SetVoxel(voxel, new DraftVoxel(false, string.Empty, false)),
            AuthoringEditMode.AddTree => AddTree(world, voxel),
            AuthoringEditMode.RemoveFeature => SetFeature(voxel, new DraftFeature(false, string.Empty, Vector3I.Up)),
            AuthoringEditMode.PlaceRedGem => PaintVoxel(voxel, "gem_red"),
            _ => false,
        };

        if (!changed) return;

        DraftCellState after = CaptureDraftState(voxel);
        _undoEdits.Push(new DraftEdit(voxel, before, after));
        _redoEdits.Clear();
        RefreshEditButtons();
        RebuildEditedPreview(analyze: false);
        GetViewport().SetInputAsHandled();
    }

    private void BuildEditingUi()
    {
        _editingUiBuilt = true;
        EnsureDraftMatchesCurrentCandidate();

        var layer = new CanvasLayer { Name = "WorldAuthoringEditCanvas", Layer = 55 };
        AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        var panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -350.0f,
            OffsetTop = 16.0f,
            OffsetRight = -16.0f,
            OffsetBottom = 405.0f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 7);
        margin.AddChild(column);

        var title = new Label { Text = "SPARSE WORLD EDITS" };
        title.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(title);
        column.AddChild(new Label
        {
            Text = "LMB edits/inspects the visible voxel. RMB/MMB/wheel remain camera controls.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _editModePicker = new OptionButton();
        foreach (string name in new[] { "Inspect", "Paint block", "Carve / force empty", "Add tree", "Remove feature", "Place red gem" })
        {
            _editModePicker.AddItem(name);
        }
        _editModePicker.ItemSelected += _ => RefreshPaintPickerVisibility();
        column.AddChild(_editModePicker);

        _paintBlockPicker = new OptionButton();
        foreach (BlockDefinition block in _content.Blocks.Values.OrderBy(block => block.DisplayName, StringComparer.Ordinal))
        {
            // Water is generator-owned basin content. The sparse editor is intended for terrain/material
            // correction and explicit specials; painting isolated water cubes would violate the art rules.
            if (block.Tags.Contains("water", StringComparer.Ordinal)) continue;
            _paintBlockIds.Add(block.Id);
            _paintBlockPicker.AddItem(block.DisplayName);
        }
        column.AddChild(_paintBlockPicker);

        var history = new HBoxContainer();
        history.AddThemeConstantOverride("separation", 6);
        column.AddChild(history);
        _undoButton = new Button { Text = "Undo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _redoButton = new Button { Text = "Redo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _undoButton.Pressed += UndoAuthoringEdit;
        _redoButton.Pressed += RedoAuthoringEdit;
        history.AddChild(_undoButton);
        history.AddChild(_redoButton);

        var validate = new Button { Text = "Validate + exact metrics" };
        validate.Pressed += ValidateEditedCandidate;
        column.AddChild(validate);

        var save = new Button
        {
            Text = "Save override draft into repo",
            TooltipText = "Writes data/worlds/overrides/<world>_authoring_draft.json. It does not alter worlds.json or freeze a shipped version.",
        };
        save.Pressed += SaveOverrideDraft;
        column.AddChild(save);

        var clear = new Button { Text = "Clear sparse draft" };
        clear.Pressed += ClearAuthoringDraft;
        column.AddChild(clear);

        _editStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Inspect or edit a visible voxel. Freeze-for-shipping remains a deliberate reviewed step after the draft is committed.",
        };
        column.AddChild(_editStatus);

        RefreshPaintPickerVisibility();
        RefreshEditButtons();
    }

    private bool TryCurrentPreview(out WorldView view, out VirtualWorld world)
    {
        view = null!;
        world = null!;
        if (_previewRoot is null) return false;
        view = _previewRoot.GetNodeOrNull<WorldView>("WorldView")!;
        if (view is null) return false;
        world = view.WorldForAuthoring;
        return world is not null;
    }

    private void EnsureDraftMatchesCurrentCandidate()
    {
        WorldProfile profile = CandidateProfile();
        string hash = WorldFreezeService.ComputeContentHash(profile);
        if (string.Equals(hash, _draftBaseHash, StringComparison.Ordinal)) return;

        _draftBaseHash = hash;
        _draftVoxels.Clear();
        _draftFeatures.Clear();
        _undoEdits.Clear();
        _redoEdits.Clear();
        _savedOverridePath = string.Empty;
        _previewOverridePath = string.Empty;
        RefreshEditButtons();
        if (_editStatus is not null)
        {
            _editStatus.Text = "Candidate parameters/seed changed. Sparse edit history was reset for the new deterministic baseline.";
        }
    }

    private void InspectVoxel(VirtualWorld world, Vector3I voxel)
    {
        BlockSample sample = world.SampleVoxel(voxel);
        Vector3I outward = world.Source.GetOutwardNormal(voxel);
        bool tree = world.Source.TrySampleTree(voxel, out FeatureSample feature);
        string featureText = tree ? $" · feature {feature.BlockId}" : string.Empty;
        SetEditStatus(
            $"Voxel {voxel}: {(sample.Present ? sample.BlockId : "empty")} · mineable {sample.Mineable} · outward {outward}{featureText}");
    }

    private string SelectedPaintBlockId()
    {
        if (_paintBlockIds.Count == 0 || _paintBlockPicker is null) return "dirt";
        int index = Math.Clamp(_paintBlockPicker.Selected, 0, _paintBlockIds.Count - 1);
        return _paintBlockIds[index];
    }

    private bool PaintVoxel(Vector3I voxel, string blockId)
    {
        if (!_content.Blocks.ContainsKey(blockId))
        {
            SetEditStatus($"Unknown block id '{blockId}'.");
            return false;
        }
        return SetVoxel(voxel, new DraftVoxel(true, blockId, true));
    }

    private bool SetVoxel(Vector3I voxel, DraftVoxel value)
    {
        if (_draftVoxels.TryGetValue(voxel, out DraftVoxel existing) && existing.Equals(value)) return false;
        _draftVoxels[voxel] = value;
        SetEditStatus(value.Present ? $"Painted {voxel} as {value.BlockId}." : $"Carved {voxel}.");
        return true;
    }

    private bool AddTree(VirtualWorld world, Vector3I voxel)
    {
        BlockSample support = world.SampleVoxel(voxel);
        if (!support.Present)
        {
            SetEditStatus("Tree needs a present support voxel.");
            return false;
        }

        Vector3I outward = world.Source.GetOutwardNormal(voxel);
        if (world.SampleVoxel(voxel + outward).Present)
        {
            SetEditStatus("Tree support is not on the exposed outward surface.");
            return false;
        }

        return SetFeature(voxel, new DraftFeature(true, "tree", outward));
    }

    private bool SetFeature(Vector3I voxel, DraftFeature value)
    {
        if (_draftFeatures.TryGetValue(voxel, out DraftFeature existing) && existing.Equals(value)) return false;
        _draftFeatures[voxel] = value;
        SetEditStatus(value.Present ? $"Added tree feature at {voxel}." : $"Suppressed feature at {voxel}.");
        return true;
    }

    private DraftCellState CaptureDraftState(Vector3I voxel)
    {
        DraftVoxel? voxelValue = _draftVoxels.TryGetValue(voxel, out DraftVoxel voxelOverride) ? voxelOverride : null;
        DraftFeature? featureValue = _draftFeatures.TryGetValue(voxel, out DraftFeature featureOverride) ? featureOverride : null;
        return new DraftCellState(voxelValue, featureValue);
    }

    private void RestoreDraftState(Vector3I voxel, DraftCellState state)
    {
        if (state.Voxel is DraftVoxel voxelValue) _draftVoxels[voxel] = voxelValue;
        else _draftVoxels.Remove(voxel);

        if (state.Feature is DraftFeature featureValue) _draftFeatures[voxel] = featureValue;
        else _draftFeatures.Remove(voxel);
    }

    private void UndoAuthoringEdit()
    {
        if (_undoEdits.Count == 0) return;
        DraftEdit edit = _undoEdits.Pop();
        RestoreDraftState(edit.Coordinate, edit.Before);
        _redoEdits.Push(edit);
        RefreshEditButtons();
        RebuildEditedPreview(analyze: false);
        SetEditStatus($"Undo at {edit.Coordinate}.");
    }

    private void RedoAuthoringEdit()
    {
        if (_redoEdits.Count == 0) return;
        DraftEdit edit = _redoEdits.Pop();
        RestoreDraftState(edit.Coordinate, edit.After);
        _undoEdits.Push(edit);
        RefreshEditButtons();
        RebuildEditedPreview(analyze: false);
        SetEditStatus($"Redo at {edit.Coordinate}.");
    }

    private void ClearAuthoringDraft()
    {
        _draftVoxels.Clear();
        _draftFeatures.Clear();
        _undoEdits.Clear();
        _redoEdits.Clear();
        _savedOverridePath = string.Empty;
        RefreshEditButtons();
        RegeneratePreview(analyze: true);
        SetEditStatus("Sparse authoring draft cleared; preview returned to the deterministic base candidate.");
    }

    private void ValidateEditedCandidate()
    {
        EnsureDraftMatchesCurrentCandidate();
        RebuildEditedPreview(analyze: true);
        WorldProfile profile = CandidateProfile();
        profile.OverrideFile = _previewOverridePath;
        FrozenWorldManifest manifest = WorldFreezeService.BuildManifest(profile, profile.WorldVersion);
        SetEditStatus(
            $"Validation passed: {manifest.MineableBlocks:N0} mineable · {manifest.TreeCount:N0} trees · {manifest.GemCount:N0} gems · hash {manifest.ContentHash[..12]}…");
    }

    private void SaveOverrideDraft()
    {
        EnsureDraftMatchesCurrentCandidate();
        WorldProfile profile = CandidateProfile();
        string relative = $"res://data/worlds/overrides/{profile.Id}_authoring_draft.json";
        WriteOverrideDocument(relative, profile);
        _savedOverridePath = relative;
        SetEditStatus(
            $"Saved sparse draft to {relative}. Review/rename it and point the approved world profile at it before Freeze for Shipping.");
    }

    private void RebuildEditedPreview(bool analyze)
    {
        WorldProfile profile = CandidateProfile();
        _previewOverridePath = $"user://world_authoring_drafts/_preview_{profile.Id}.json";
        WriteOverrideDocument(_previewOverridePath, profile);
        profile.OverrideFile = _previewOverridePath;

        if (_previewRoot is not null)
        {
            RemoveChild(_previewRoot);
            _previewRoot.QueueFree();
        }

        _previewRoot = new Node3D { Name = "WorldAuthoringPreview" };
        AddChild(_previewRoot);
        var world = new VirtualWorld(profile);
        long blocks = world.InitializeMineableBlockCount();
        var view = new WorldView { Name = "WorldView" };
        _previewRoot.AddChild(view);
        view.Initialize(_assets, world, _camera);

        float extent = profile.BlockSpacing * (
            profile.BaseRadius + profile.TerrainAmplitude + profile.DetailAmplitude + MathF.Max(0.0f, profile.SeaLevelOffset));
        _camera.ConfigureWorldExtent(extent);
        _clouds.Visible = true;
        _clouds.SetWorldExtent(extent);

        _status.Text =
            $"Edited preview: seed {profile.Seed:N0}, count {blocks:N0}, {_draftVoxels.Count:N0} voxel edits, {_draftFeatures.Count:N0} feature edits.";
        if (analyze) ShowMetrics(profile, WorldAuthoringAnalyzer.Analyze(profile));
    }

    private void WriteOverrideDocument(string path, WorldProfile profile)
    {
        var document = new
        {
            schemaVersion = WorldOverrideSet.SupportedSchemaVersion,
            worldId = profile.Id,
            generationVersion = profile.GenerationVersion,
            overrides = _draftVoxels
                .OrderBy(pair => pair.Key.X)
                .ThenBy(pair => pair.Key.Y)
                .ThenBy(pair => pair.Key.Z)
                .Select(pair => new
                {
                    x = pair.Key.X,
                    y = pair.Key.Y,
                    z = pair.Key.Z,
                    present = pair.Value.Present,
                    blockId = pair.Value.BlockId,
                    mineable = pair.Value.Mineable,
                })
                .ToArray(),
            features = _draftFeatures
                .OrderBy(pair => pair.Key.X)
                .ThenBy(pair => pair.Key.Y)
                .ThenBy(pair => pair.Key.Z)
                .Select(pair => new
                {
                    x = pair.Key.X,
                    y = pair.Key.Y,
                    z = pair.Key.Z,
                    present = pair.Value.Present,
                    blockId = pair.Value.BlockId,
                    normalX = pair.Value.Normal.X,
                    normalY = pair.Value.Normal.Y,
                    normalZ = pair.Value.Normal.Z,
                })
                .ToArray(),
        };

        string absoluteDirectory = System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath(path)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(absoluteDirectory)) System.IO.Directory.CreateDirectory(absoluteDirectory);
        using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file is null) throw new InvalidOperationException($"Could not write world override draft '{path}'.");
        file.StoreString(JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshPaintPickerVisibility()
    {
        if (_paintBlockPicker is null || _editModePicker is null) return;
        _paintBlockPicker.Visible = (AuthoringEditMode)_editModePicker.Selected == AuthoringEditMode.PaintBlock;
    }

    private void RefreshEditButtons()
    {
        if (_undoButton is not null) _undoButton.Disabled = _undoEdits.Count == 0;
        if (_redoButton is not null) _redoButton.Disabled = _redoEdits.Count == 0;
    }

    private void SetEditStatus(string text)
    {
        if (_editStatus is not null) _editStatus.Text = text;
    }
}
