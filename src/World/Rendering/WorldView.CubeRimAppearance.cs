using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Presentation-only surface policy for cube worlds. The logical block IDs remain untouched so saves,
/// mining rewards and replay baselines do not change just because the terrain art is refined.
///
/// The very outer one-block rim of every procedural cube face is rendered as a clean solid-green block.
/// Everywhere else a logical grass surface uses the dirt-backed grass mesh, so natural ledges and holes
/// still expose brown soil rather than looking like solid green plastic.
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

        // Copy the child list because each processed batch is replaced in-place.
        var batches = new List<MultiMeshInstance3D>();
        foreach (Node child in chunkRoot.GetChildren())
        {
            if (child is MultiMeshInstance3D batch
                && batch.Multimesh is not null
                && batch.Name.ToString().StartsWith("Batch_", StringComparison.Ordinal))
            {
                batches.Add(batch);
            }
        }

        foreach (MultiMeshInstance3D batch in batches)
        {
            string batchName = batch.Name.ToString();
            string sourceBlockId = batchName["Batch_".Length..];
            BlockDefinition definition;
            try
            {
                definition = _assets.GetDefinition(sourceBlockId);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            // Trees, water, gems and other decorative/special batches are not terrain skin.
            if (!string.Equals(definition.RenderClass, "terrain", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MultiMesh source = batch.Multimesh;
            int count = source.VisibleInstanceCount >= 0
                ? Math.Min(source.InstanceCount, source.VisibleInstanceCount)
                : source.InstanceCount;
            if (count <= 0) continue;

            var groups = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                Transform3D transform = source.GetInstanceTransform(i);
                Vector3I voxel = WorldPositionToVoxel(transform.Origin);
                string visualBlockId = ResolveSurfaceVisualBlockId(voxel, sourceBlockId);
                if (!groups.TryGetValue(visualBlockId, out List<Transform3D>? transforms))
                {
                    transforms = new List<Transform3D>();
                    groups.Add(visualBlockId, transforms);
                }
                transforms.Add(transform);
            }

            // If nothing changes, keep the original batch untouched.
            if (groups.Count == 1
                && groups.TryGetValue(sourceBlockId, out List<Transform3D>? unchanged)
                && unchanged.Count == count)
            {
                continue;
            }

            foreach ((string visualBlockId, List<Transform3D> transforms) in groups)
            {
                AddAppearanceBatch(
                    chunkRoot,
                    sourceBlockId,
                    visualBlockId,
                    transforms,
                    batch.CastShadow,
                    batch.Visible);
            }

            batch.QueueFree();
        }
    }

    /// <summary>
    /// Returns only the visual skin for a logical terrain block. Mining/reward code continues to use
    /// the original block ID.
    /// </summary>
    private string ResolveSurfaceVisualBlockId(Vector3I voxel, string blockId)
    {
        if (_world is null || _assets is null || _world.Profile.UsesSingleBlockGenerator || _world.Profile.UsesSolidCubeGenerator)
        {
            return blockId;
        }

        BlockDefinition definition;
        try
        {
            definition = _assets.GetDefinition(blockId);
        }
        catch (KeyNotFoundException)
        {
            return blockId;
        }

        if (!string.Equals(definition.RenderClass, "terrain", StringComparison.OrdinalIgnoreCase))
        {
            return blockId;
        }

        // The cube-face perimeter is intentionally biome-neutral presentation: even a shoreline or
        // cliff that mathematically reaches the seam reads as the same clean green world border.
        if (IsOuterCubeFaceRim(voxel))
        {
            return OuterRimVisualBlockId;
        }

        // Away from the border, ordinary grass must retain dirt-backed sides. This is what makes an
        // interior cut, ledge or mined hole reveal brown soil while the upward surface stays grassy.
        if (string.Equals(blockId, _world.Profile.SurfaceBlock, StringComparison.Ordinal))
        {
            return _world.Profile.SurfaceEdgeBlock;
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
        // column before the adjacent face takes over. Requiring the current block to be exposed keeps
        // deeper blocks brown after the green rim itself has been mined away.
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
