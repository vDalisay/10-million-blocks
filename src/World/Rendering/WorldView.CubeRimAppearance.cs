using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Rendering;

/// <summary>
/// Presentation-only correction for one very specific procedural cube seam case.
/// The procedural generator remains authoritative everywhere: normal grass stays normal grass,
/// dirt stays dirt, sand stays sand, stone stays stone, and natural dirt-backed grass ledges keep
/// their brown sides and grassy fringe. Only a dirt-backed grass edge block that actually lies on
/// the geometric outer seam between cube faces is rendered with the clean green cube mesh.
/// Logical block IDs are never changed, so saves, rewards and replay baselines are unaffected.
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
        // one turn so the whole chunk exists before splitting the one eligible terrain batch.
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

        // The user's "dirt block with grass on top" is the procedural SurfaceEdgeBlock
        // (normally dirt_grass). Plain underground SoilBlock/dirt is deliberately NOT touched.
        string sourceBlockId = _world.Profile.SurfaceEdgeBlock;
        var batches = new List<MultiMeshInstance3D>();
        foreach (Node child in chunkRoot.GetChildren())
        {
            if (child is MultiMeshInstance3D batch
                && batch.Multimesh is not null
                && string.Equals(batch.Name.ToString(), $"Batch_{sourceBlockId}", StringComparison.Ordinal))
            {
                batches.Add(batch);
            }
        }

        foreach (MultiMeshInstance3D batch in batches)
        {
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
                if (IsTrueOuterCubeSeam(voxel))
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
    /// Returns only the presentation skin for mining feedback. Only the same dirt-backed grass block
    /// on the actual cube seam gets the clean-green mesh; every other generated block keeps its asset.
    /// </summary>
    private string ResolveSurfaceVisualBlockId(Vector3I voxel, string blockId)
    {
        if (_world is null
            || _world.Profile.UsesSingleBlockGenerator
            || _world.Profile.UsesSolidCubeGenerator)
        {
            return blockId;
        }

        if (string.Equals(blockId, _world.Profile.SurfaceEdgeBlock, StringComparison.Ordinal)
            && IsTrueOuterCubeSeam(voxel))
        {
            return OuterRimVisualBlockId;
        }

        return blockId;
    }

    /// <summary>
    /// A cube perimeter is where two (or three at a corner) equally outer coordinate axes meet.
    /// The previous implementation used "tangent >= BaseRadius", which described a broad border BAND;
    /// that is why legitimate interior dirt/grass ledges near an edge were incorrectly turned green.
    /// Requiring tied dominant axes reduces the rule to the actual one-block seam line.
    /// </summary>
    private bool IsTrueOuterCubeSeam(Vector3I voxel)
    {
        if (!_world.IsPresent(voxel) || !_world.IsExposed(voxel)) return false;

        int ax = Math.Abs(voxel.X);
        int ay = Math.Abs(voxel.Y);
        int az = Math.Abs(voxel.Z);
        int outer = Math.Max(ax, Math.Max(ay, az));

        // Avoid classifying an arbitrary tied coordinate inside a mined cavity as a world seam.
        int minimumSeamRadius = Math.Max(1, Mathf.FloorToInt(_world.Profile.BaseRadius + 0.001f));
        if (outer < minimumSeamRadius) return false;

        int dominantAxisCount = (ax == outer ? 1 : 0)
            + (ay == outer ? 1 : 0)
            + (az == outer ? 1 : 0);
        return dominantAxisCount >= 2;
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
