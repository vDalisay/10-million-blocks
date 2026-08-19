using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView : Node3D
{
    private readonly Dictionary<ChunkCoord, Node3D> _chunkRoots = new();
    private readonly HashSet<ChunkCoord> _dirtyChunks = new();

    private BlockAssetRegistry _assets = null!;
    private VirtualWorld _world = null!;

    public int VisibleChunkCount => _chunkRoots.Count;
    public int PendingChunkRebuilds => _dirtyChunks.Count;

    public void Initialize(BlockAssetRegistry assets, VirtualWorld world)
    {
        _assets = assets;
        _world = world;
        BuildInitialChunks();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        int budget = 2;
        while (budget-- > 0 && TryPopDirtyChunk(out ChunkCoord chunk))
        {
            RebuildChunk(chunk);
        }
    }

    public void MarkDirtyAround(Vector3I voxel)
    {
        int chunkSize = _world.Profile.ChunkSize;
        _dirtyChunks.Add(ChunkCoord.FromVoxel(voxel, chunkSize));
        foreach (Vector3I direction in VoxelMath.Neighbors)
        {
            _dirtyChunks.Add(ChunkCoord.FromVoxel(voxel + direction, chunkSize));
        }
    }

    public Vector3 VoxelToWorld(Vector3I voxel) => (Vector3)voxel * _world.Profile.BlockSpacing;

    private void BuildInitialChunks()
    {
        int max = _world.MaxCoordinate;
        int chunkSize = _world.Profile.ChunkSize;
        int minChunk = VoxelMath.FloorDiv(-max, chunkSize);
        int maxChunk = VoxelMath.FloorDiv(max, chunkSize);

        for (int z = minChunk; z <= maxChunk; z++)
        for (int y = minChunk; y <= maxChunk; y++)
        for (int x = minChunk; x <= maxChunk; x++)
        {
            RebuildChunk(new ChunkCoord(x, y, z));
        }
    }

    private void RebuildChunk(ChunkCoord chunk)
    {
        if (_chunkRoots.Remove(chunk, out Node3D? oldRoot))
        {
            oldRoot.QueueFree();
        }

        int chunkSize = _world.Profile.ChunkSize;
        int max = _world.MaxCoordinate;
        Vector3I min = chunk.MinVoxel(chunkSize);
        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        var treeTransforms = new List<Transform3D>();

        for (int z = 0; z < chunkSize; z++)
        for (int y = 0; y < chunkSize; y++)
        for (int x = 0; x < chunkSize; x++)
        {
            Vector3I voxel = min + new Vector3I(x, y, z);
            if (Math.Abs(voxel.X) > max || Math.Abs(voxel.Y) > max || Math.Abs(voxel.Z) > max)
            {
                continue;
            }

            BlockSample sample = _world.SampleVoxel(voxel);
            if (!sample.Present || !_world.IsExposed(voxel))
            {
                continue;
            }

            Vector3I outward = _world.Source.GetOutwardNormal(voxel);
            Basis blockBasis = ShouldOrientToCubeFace(sample.BlockId)
                ? BasisForNormal(outward)
                : Basis.Identity;
            AddTransform(batches, sample.BlockId, new Transform3D(blockBasis, VoxelToWorld(voxel)));

            if ((sample.BlockId == _world.Profile.SurfaceBlock || sample.BlockId == _world.Profile.SurfaceEdgeBlock)
                && _world.Source.TrySampleTree(voxel, out FeatureSample feature))
            {
                if (!_world.IsPresent(voxel + feature.OutwardNormal))
                {
                    // The supplied tree is deliberately enlarged slightly. At 1:1 it disappeared into the
                    // highly detailed grass silhouette and was difficult to read at the reference camera distance.
                    Basis treeBasis = BasisForNormal(feature.OutwardNormal).Scaled(Vector3.One * 1.38f);
                    Vector3 position = VoxelToWorld(voxel + feature.OutwardNormal);
                    treeTransforms.Add(new Transform3D(treeBasis, position));
                }
            }
        }

        if (batches.Count == 0 && treeTransforms.Count == 0)
        {
            return;
        }

        var chunkRoot = new Node3D { Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}" };
        AddChild(chunkRoot);

        foreach ((string blockId, List<Transform3D> transforms) in batches)
        {
            AddBatch(chunkRoot, blockId, transforms, true);
        }

        if (treeTransforms.Count > 0)
        {
            AddBatch(chunkRoot, "tree", treeTransforms, true);
        }

        _chunkRoots.Add(chunk, chunkRoot);
    }

    private void AddBatch(Node3D parent, string blockId, List<Transform3D> transforms, bool castShadow)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = _assets.GetMesh(blockId),
            InstanceCount = transforms.Count,
            VisibleInstanceCount = transforms.Count,
        };

        for (int i = 0; i < transforms.Count; i++)
        {
            multiMesh.SetInstanceTransform(i, transforms[i]);
        }

        parent.AddChild(new MultiMeshInstance3D
        {
            Name = $"Batch_{blockId}",
            Multimesh = multiMesh,
            MaterialOverride = _assets.GetMaterialOverride(blockId),
            CastShadow = castShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private bool ShouldOrientToCubeFace(string blockId)
    {
        BlockDefinition definition = _assets.GetDefinition(blockId);
        return definition.Tags.Contains("surface") || definition.Tags.Contains("water");
    }

    private static void AddTransform(Dictionary<string, List<Transform3D>> batches, string blockId, Transform3D transform)
    {
        if (!batches.TryGetValue(blockId, out List<Transform3D>? transforms))
        {
            transforms = new List<Transform3D>();
            batches.Add(blockId, transforms);
        }

        transforms.Add(transform);
    }

    private static Basis BasisForNormal(Vector3I normal)
    {
        if (normal == Vector3I.Up)
        {
            return Basis.Identity;
        }

        if (normal == Vector3I.Down)
        {
            return new Basis(Vector3.Right, Mathf.Pi);
        }

        if (normal == Vector3I.Right)
        {
            return new Basis(Vector3.Back, -Mathf.Pi * 0.5f);
        }

        if (normal == Vector3I.Left)
        {
            return new Basis(Vector3.Back, Mathf.Pi * 0.5f);
        }

        if (normal == Vector3I.Back)
        {
            return new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        }

        return new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
    }

    private bool TryPopDirtyChunk(out ChunkCoord chunk)
    {
        foreach (ChunkCoord candidate in _dirtyChunks)
        {
            chunk = candidate;
            _dirtyChunks.Remove(candidate);
            return true;
        }

        chunk = default;
        return false;
    }
}
