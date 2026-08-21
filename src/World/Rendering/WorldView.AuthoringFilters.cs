using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly Dictionary<ulong, Transform3D[]> _authoringOriginalTransforms = new();
    private readonly HashSet<string> _authoringHiddenTags = new(StringComparer.Ordinal);
    private bool _authoringSliceEnabled;
    private int _authoringSliceAxis = 1;
    private int _authoringSliceCoordinate;
    private bool _authoringSliceKeepLower = true;

    /// <summary>
    /// Enables an exact presentation-only slice through authoring MultiMeshes. The immutable world and
    /// sparse authored overrides are untouched; instances are merely reordered so the visible prefix
    /// lies on the requested side of the plane. Axis is 0=X, 1=Y, 2=Z.
    /// </summary>
    public void ConfigureAuthoringSlice(bool enabled, int axis, int coordinate, bool keepLower)
    {
        _authoringSliceEnabled = enabled;
        _authoringSliceAxis = Math.Clamp(axis, 0, 2);
        _authoringSliceCoordinate = coordinate;
        _authoringSliceKeepLower = keepLower;
        RefreshAuthoringPresentationFilters();
    }

    /// <summary>
    /// Presentation-only material/feature category filtering for the standalone authoring tool.
    /// Tags come directly from the runtime block catalog, so the editor cannot drift into its own
    /// duplicate material taxonomy.
    /// </summary>
    public void SetAuthoringTagVisible(string tag, bool visible)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (visible) _authoringHiddenTags.Remove(tag);
        else _authoringHiddenTags.Add(tag);
        RefreshAuthoringPresentationFilters();
    }

    public void ResetAuthoringPresentationFilters()
    {
        _authoringHiddenTags.Clear();
        _authoringSliceEnabled = false;
        RefreshAuthoringPresentationFilters();
    }

    /// <summary>
    /// Re-applies authoring filters after an edited preview rebuild. The authoring UI calls this on a
    /// low-frequency pulse because chunk roots are intentionally replaceable renderer cache objects.
    /// </summary>
    public void RefreshAuthoringPresentationFilters()
    {
        if (_world is null || _assets is null) return;

        var liveIds = new HashSet<ulong>();
        foreach (Node3D chunkRoot in _chunkRoots.Values)
        {
            foreach (Node child in chunkRoot.GetChildren())
            {
                if (child is not MultiMeshInstance3D batch || batch.Multimesh is null) continue;

                ulong id = batch.GetInstanceId();
                liveIds.Add(id);
                Transform3D[] original = CaptureAuthoringTransforms(batch);
                string blockId = BatchBlockId(batch);

                bool hiddenByTag = false;
                if (!string.IsNullOrWhiteSpace(blockId))
                {
                    BlockDefinition definition = _assets.GetDefinition(blockId);
                    foreach (string tag in _authoringHiddenTags)
                    {
                        if (definition.Tags.Contains(tag, StringComparer.Ordinal))
                        {
                            hiddenByTag = true;
                            break;
                        }
                    }
                }

                batch.Visible = !hiddenByTag;
                ApplyAuthoringSlice(batch.Multimesh, original);
            }
        }

        if (_authoringOriginalTransforms.Count == liveIds.Count) return;
        var stale = new List<ulong>();
        foreach (ulong id in _authoringOriginalTransforms.Keys)
        {
            if (!liveIds.Contains(id)) stale.Add(id);
        }
        foreach (ulong id in stale) _authoringOriginalTransforms.Remove(id);
    }

    private Transform3D[] CaptureAuthoringTransforms(MultiMeshInstance3D batch)
    {
        ulong id = batch.GetInstanceId();
        if (_authoringOriginalTransforms.TryGetValue(id, out Transform3D[]? cached)) return cached;

        MultiMesh multiMesh = batch.Multimesh!;
        var transforms = new Transform3D[multiMesh.InstanceCount];
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i] = multiMesh.GetInstanceTransform(i);
        }
        _authoringOriginalTransforms[id] = transforms;
        return transforms;
    }

    private void ApplyAuthoringSlice(MultiMesh multiMesh, Transform3D[] original)
    {
        if (!_authoringSliceEnabled)
        {
            for (int i = 0; i < original.Length; i++) multiMesh.SetInstanceTransform(i, original[i]);
            multiMesh.VisibleInstanceCount = original.Length;
            return;
        }

        float spacing = MathF.Max(0.001f, _world.Profile.BlockSpacing);
        int visible = 0;

        // MultiMesh exposes only a visible prefix. Copy matching transforms to that prefix, then put the
        // hidden side behind it. The baseline snapshot remains immutable so toggling the slice is lossless.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < original.Length; i++)
            {
                int coordinate = ApproximateVoxelAxis(original[i].Origin, spacing, _authoringSliceAxis);
                bool keep = _authoringSliceKeepLower
                    ? coordinate <= _authoringSliceCoordinate
                    : coordinate >= _authoringSliceCoordinate;
                if ((pass == 0) != keep) continue;
                multiMesh.SetInstanceTransform(visible++, original[i]);
            }
        }

        int kept = 0;
        for (int i = 0; i < original.Length; i++)
        {
            int coordinate = ApproximateVoxelAxis(original[i].Origin, spacing, _authoringSliceAxis);
            bool keep = _authoringSliceKeepLower
                ? coordinate <= _authoringSliceCoordinate
                : coordinate >= _authoringSliceCoordinate;
            if (keep) kept++;
        }
        multiMesh.VisibleInstanceCount = kept;
    }

    private static int ApproximateVoxelAxis(Vector3 worldPosition, float spacing, int axis)
    {
        float value = axis switch
        {
            0 => worldPosition.X,
            1 => worldPosition.Y,
            _ => worldPosition.Z,
        };
        return (int)MathF.Round(value / spacing);
    }

    private static string BatchBlockId(MultiMeshInstance3D batch)
    {
        string name = batch.Name.ToString();
        const string prefix = "Batch_";
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : string.Empty;
    }
}
