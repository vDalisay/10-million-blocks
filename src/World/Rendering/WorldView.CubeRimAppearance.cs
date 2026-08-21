using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Presentation-only correction for procedural cube seams. The generator remains authoritative for
/// terrain choice everywhere: grass stays grass, dirt-grass stays dirt-grass, sand stays sand, stone
/// stays stone, etc. The only exception is a plain dirt/soil block that lands on the outer cube-face
/// perimeter; that one is rendered with the clean green cube so the seam does not expose a brown strip.
/// Logical block IDs are never changed, so saves, mining rewards and replay baselines are unaffected.
/// </summary>
public partial class WorldView
{
    private const string OuterRimVisualBlockId = "grass_outer";
    private const string RimAppearanceAppliedMeta = "cube_rim_appearance_applied";
    private bool _rimAppearanceHooked;

    public override void _EnterTree()
    {
        if (_rimAppearanceHooked) return;
        ChildEnteredTree += OnWorldViewChildEnteredTree;
        _rimAppearanceHooked = true;
    }

    public override void _ExitTree()
    {
        if (!_rimAppearanceHooked) return;
        ChildEnteredTree -= OnWorldViewChildEnteredTree;
        _rimAppearanceHooked = false;
    }

    private void OnWorldViewChildEnteredTree(Node child)
    {
        if (child is not Node3D chunkRoot || !IsTerrainChunkRoot(chunkRoot.Name.ToString()))
        {
            return;
        }

        // WorldView adds the chunk root first and its MultiMesh batches immediately afterwards. Defer
        // one turn so the whole chunk exists before splitting its terrain instances by appearance.
        Callable.From(() => ApplyCubeRimAppearance(chunkRoot)).CallDeferred();
    }

    private static bool IsTerrainChunkRoot(string name)
        => name.StartsWith("Chunk_", StringComparison.Ordinal)
            || name.StartsWith("FullSurfaceChunk_", StringComparison.Ordinal)
            || name.StartsWith("StreamChunk_", StringComparison.Ordinal);

    private void ApplyCubeRimAppearance(Node3D chunkRoot)
    {
        if (!GodotObject.IsInstanceValid(chunkRoot)
            || chunkRoot.IsQueuedForDeletion()
            || chunkRoot.HasMeta(RimAppearanceAppliedMeta)
            || _world is null
            || _assets is null
            || _world.Profile.UsesSingleBlockGenerator
            || _world.Profile.UsesSolidCubeGenerator)
        {
            return;
        }

        chunkRoot.SetMeta(RimAppearanceAppliedMeta, true);

        // Only dirt can change appearance now. Ignore every other batch up front so procedural terrain
        // selection remains exactly as generated for grass, dirt-grass, sand, stone, water and specials.
        var batches = new List<MultiMeshInstance3D>();
        foreach (Node child in chunkRoot.GetChildren())
        {
            if (child is MultiMeshInstance3D batch
                && batch.Multimesh is not null
                && string.Equals(batch.Name.ToString(), $"Batch_{_world.Profile.SoilBlock}", StringComparison.Ordinal))
            {
                batches.Add(batch);
            }
        }

        foreach (MultiMeshInstance3D batch in batches)
        {
            string sourceBlockId = _world.Profile.SoilBlock;
            MultiMesh source = batch.Multimesh;
            int count = source.VisibleInstanceCount >= 0
                ? Math.Min(source.InstanceCount, source.VisibleInstanceCount)
                : source.InstanceCount;
            if (count <= 0) continue;

            var unchanged = new List<Transform3D>(count);
            var cleanGreen = new List<Transform3D>();
            for (int i = 0; i < count; i++)
            {
                Transform3D transform = source.GetInstanceTransform(i);
                Vector3I voxel = WorldPositionToVoxel(transform.Origin);
                if (IsOuterCubeFaceRim(voxel))
                {
                    cleanGreen.Add(transform);
                }
                else
                {
                    unchanged.Add(transform);
                }
            }

            if (cleanGreen.Count == 0)
            {
                continue;
            }

            if (unchanged.Count > 0)
            {
                AddAppearanceBatch(
                    chunkRoot,
                    sourceBlockId,
                    sourceBlockId,
                    unchanged,
                    batch.CastShadow,
                    batch.Visible);
            }

            AddAppearanceBatch(
                chunkRoot,
                sourceBlockId,
                OuterRimVisualBlockId,
                cleanGreen,
                batch.CastShadow,
                batch.Visible);

            batch.QueueFree();
        }
    }

    /// <summary>
    /// Returns only the presentation skin for mining feedback. The procedural block type is preserved;
    /// only plain soil exactly on the outer cube-face rim receives the clean-green visual replacement.
    /// </summary>
    private string ResolveSurfaceVisualBlockId(Vector3I voxel, string blockId)
    {
        if (_world is null
            || _world.Profile.UsesSingleBlockGenerator
            || _world.Profile.UsesSolidCubeGenerator)
        {
            return blockId;
        }

        if (string.Equals(blockId, _world.Profile.SoilBlock, StringComparison.Ordinal)
            && IsOuterCubeFaceRim(voxel))
        {
            return OuterRimVisualBlockId;
        }

        return blockId;
    }

    private bool IsOuterCubeFaceRim(Vector3I voxel)
    {
        if (!_world.IsPresent(voxel) || !_world.IsExposed(voxel)) return false;

        Vector3I normal = _world.Source.GetOutwardNormal(voxel);
        int tangentA;
        int tangentB;
        if (Math.Abs(normal.X) == 1)
        {
            tangentA = Math.Abs(voxel.Y);
            tangentB = Math.Abs(voxel.Z);
        }
        else if (Math.Abs(normal.Y) == 1)
        {
            tangentA = Math.Abs(voxel.X);
            tangentB = Math.Abs(voxel.Z);
        }
        else
        {
            tangentA = Math.Abs(voxel.X);
            tangentB = Math.Abs(voxel.Y);
        }

        // Procedural face coordinates use BaseRadius N+0.5, making floor(BaseRadius) the last integer
        // column before the adjacent face takes over. Requiring exposure prevents mined-through interior
        // soil from being recolored merely because it shares the same tangent coordinate.
        int faceBorder = Math.Max(1, Mathf.FloorToInt(_world.Profile.BaseRadius + 0.001f));
        return Math.Max(tangentA, tangentB) >= faceBorder;
    }

    private Vector3I WorldPositionToVoxel(Vector3 position)
    {
        float spacing = MathF.Max(0.0001f, _world.Profile.BlockSpacing);
        return new Vector3I(
            (int)MathF.Round(position.X / spacing),
            (int)MathF.Round(position.Y / spacing),
            (int)MathF.Round(position.Z / spacing));
    }

    private void AddAppearanceBatch(
        Node3D parent,
        string sourceBlockId,
        string visualBlockId,
        IReadOnlyList<Transform3D> transforms,
        GeometryInstance3D.ShadowCastingSetting castShadow,
        bool visible)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = _assets.GetMesh(visualBlockId),
            InstanceCount = transforms.Count,
            VisibleInstanceCount = transforms.Count,
        };

        for (int i = 0; i < transforms.Count; i++)
        {
            multiMesh.SetInstanceTransform(i, transforms[i]);
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = $"Batch_{visualBlockId}_from_{sourceBlockId}",
            Multimesh = multiMesh,
            MaterialOverride = _assets.GetMaterialOverride(visualBlockId),
            CastShadow = castShadow,
            Visible = visible,
        });
    }
}
