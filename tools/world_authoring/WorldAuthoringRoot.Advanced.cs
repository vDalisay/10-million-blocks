using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Tools.WorldAuthoring;

public partial class WorldAuthoringRoot
{
    public int AuthoringMaxCoordinate => _profiles.Count == 0 ? 0 : CandidateProfile().MaxCoordinate;

    public IReadOnlyList<(string Id, string DisplayName)> AuthoringPaintBlocks
        => _content.Blocks.Values
            .Where(block => !block.Tags.Contains("water", StringComparer.Ordinal)
                && !block.Tags.Contains("tree", StringComparer.Ordinal))
            .OrderBy(block => block.DisplayName, StringComparer.Ordinal)
            .Select(block => (block.Id, block.DisplayName))
            .ToArray();

    public WorldView? CurrentAuthoringWorldView()
    {
        if (_previewRoot is null) return null;
        return _previewRoot.GetNodeOrNull<WorldView>("WorldView");
    }

    public int ApplyAuthoringBox(
        Vector3I center,
        Vector3I halfExtents,
        string blockId,
        bool carve)
    {
        halfExtents = new Vector3I(
            Math.Clamp(Math.Abs(halfExtents.X), 0, 12),
            Math.Clamp(Math.Abs(halfExtents.Y), 0, 12),
            Math.Clamp(Math.Abs(halfExtents.Z), 0, 12));

        var coordinates = new List<Vector3I>();
        for (int z = -halfExtents.Z; z <= halfExtents.Z; z++)
        for (int y = -halfExtents.Y; y <= halfExtents.Y; y++)
        for (int x = -halfExtents.X; x <= halfExtents.X; x++)
        {
            coordinates.Add(center + new Vector3I(x, y, z));
        }
        return ApplyAuthoringVoxelBatch(coordinates, blockId, carve, "box");
    }

    public int ApplyAuthoringSphere(Vector3I center, int radius, string blockId, bool carve)
    {
        radius = Math.Clamp(Math.Abs(radius), 1, 12);
        int radiusSquared = radius * radius;
        var coordinates = new List<Vector3I>();
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y + z * z > radiusSquared) continue;
            coordinates.Add(center + new Vector3I(x, y, z));
        }
        return ApplyAuthoringVoxelBatch(coordinates, blockId, carve, "sphere");
    }

    public int ApplyAuthoringPlane(
        int axis,
        int coordinate,
        int tangentRadius,
        string blockId,
        bool carve)
    {
        axis = Math.Clamp(axis, 0, 2);
        tangentRadius = Math.Clamp(Math.Abs(tangentRadius), 1, 24);
        var coordinates = new List<Vector3I>();
        for (int b = -tangentRadius; b <= tangentRadius; b++)
        for (int a = -tangentRadius; a <= tangentRadius; a++)
        {
            coordinates.Add(axis switch
            {
                0 => new Vector3I(coordinate, a, b),
                1 => new Vector3I(a, coordinate, b),
                _ => new Vector3I(a, b, coordinate),
            });
        }
        return ApplyAuthoringVoxelBatch(coordinates, blockId, carve, "plane");
    }

    public FrozenWorldManifest FreezeCurrentEditedCandidate(int worldVersion)
    {
        EnsureDraftMatchesCurrentCandidate();
        if (worldVersion <= 0) throw new ArgumentOutOfRangeException(nameof(worldVersion));

        WorldProfile profile = CandidateProfile();
        profile.WorldVersion = worldVersion;
        string manifestPath = $"res://data/worlds/frozen/{profile.Id}_v{worldVersion}.json";
        if (Godot.FileAccess.FileExists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Frozen version already exists and is immutable: {manifestPath}. Choose a new world version.");
        }

        if (_draftVoxels.Count > 0 || _draftFeatures.Count > 0)
        {
            string overridePath = $"res://data/worlds/overrides/{profile.Id}_v{worldVersion}.json";
            if (Godot.FileAccess.FileExists(overridePath))
            {
                throw new InvalidOperationException(
                    $"Versioned override already exists and will not be overwritten: {overridePath}.");
            }
            WriteOverrideDocument(overridePath, profile);
            profile.OverrideFile = overridePath;
        }
        else
        {
            profile.OverrideFile = string.Empty;
        }

        FrozenWorldManifest manifest = WorldFreezeService.Freeze(profile, worldVersion);
        _savedOverridePath = profile.OverrideFile;
        SetEditStatus(
            $"Frozen {profile.Id} v{worldVersion}: {manifest.MineableBlocks:N0} blocks · hash {manifest.ContentHash[..12]}…. " +
            "The versioned manifest/override are immutable; wire the approved profile into worlds.json deliberately.");
        return manifest;
    }

    private int ApplyAuthoringVoxelBatch(
        IEnumerable<Vector3I> requestedCoordinates,
        string blockId,
        bool carve,
        string label)
    {
        EnsureDraftMatchesCurrentCandidate();
        if (!carve && !_content.Blocks.ContainsKey(blockId))
        {
            throw new InvalidOperationException($"Unknown authoring block id '{blockId}'.");
        }

        int max = CandidateProfile().MaxCoordinate;
        var coordinates = requestedCoordinates
            .Where(c => Math.Abs(c.X) <= max && Math.Abs(c.Y) <= max && Math.Abs(c.Z) <= max)
            .Distinct()
            .Take(32_768)
            .ToArray();

        int changed = 0;
        DraftVoxel value = carve
            ? new DraftVoxel(false, string.Empty, false)
            : new DraftVoxel(true, blockId, true);

        foreach (Vector3I coordinate in coordinates)
        {
            DraftCellState before = CaptureDraftState(coordinate);
            if (_draftVoxels.TryGetValue(coordinate, out DraftVoxel existing) && existing.Equals(value)) continue;
            _draftVoxels[coordinate] = value;
            DraftCellState after = CaptureDraftState(coordinate);
            _undoEdits.Push(new DraftEdit(coordinate, before, after));
            changed++;
        }

        if (changed == 0)
        {
            SetEditStatus($"{label} edit made no changes.");
            return 0;
        }

        _redoEdits.Clear();
        RefreshEditButtons();
        RebuildEditedPreview(analyze: false);
        SetEditStatus(
            $"Applied {label} edit to {changed:N0} voxels. Undo history is voxel-granular so large authoring edits remain transparent and inspectable.");
        return changed;
    }
}
