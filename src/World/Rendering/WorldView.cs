using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView : Node3D
{
    // KayKit Forest Nature Pack variants. Trees are scattered by hash so a chunk rebuild always
    // reproduces the same forest.
    private static readonly string[] TreeVariants =
    [
        "tree_1_a", "tree_1_b", "tree_2_a", "tree_2_b", "tree_2_c",
        "tree_3_a", "tree_3_b", "tree_4_a", "tree_4_b",
    ];

    private readonly Dictionary<ChunkCoord, Node3D> _chunkRoots = new();
    private readonly HashSet<ChunkCoord> _dirtyChunks = new();
    private readonly Dictionary<string, float> _treeScales = new(StringComparer.Ordinal);

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
        var treeBatches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);

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
                    AddTransform(treeBatches, PickTree(voxel), TreeTransform(voxel, feature.OutwardNormal));
                }
            }
        }

        if (batches.Count == 0 && treeBatches.Count == 0)
        {
            return;
        }

        var chunkRoot = new Node3D { Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}" };
        AddChild(chunkRoot);

        foreach ((string blockId, List<Transform3D> transforms) in batches)
        {
            AddBatch(chunkRoot, blockId, transforms, true);
        }

        foreach ((string variant, List<Transform3D> transforms) in treeBatches)
        {
            AddBatch(chunkRoot, variant, transforms, true);
        }

        _chunkRoots.Add(chunk, chunkRoot);
    }

    private string PickTree(Vector3I voxel)
    {
        float roll = DeterministicNoise.Hash01(voxel.X, voxel.Y, voxel.Z, _world.Profile.Seed + 44017);
        int index = Math.Clamp((int)(roll * TreeVariants.Length), 0, TreeVariants.Length - 1);
        return TreeVariants[index];
    }

    private Transform3D TreeTransform(Vector3I voxel, Vector3I outward)
    {
        string variant = PickTree(voxel);
        float spacing = _world.Profile.BlockSpacing;
        float yaw = DeterministicNoise.Hash01(voxel.X, voxel.Y, voxel.Z, _world.Profile.Seed + 44019) * Mathf.Tau;
        float sizeJitter = 0.85f + DeterministicNoise.Hash01(voxel.X, voxel.Y, voxel.Z, _world.Profile.Seed + 44023) * 0.34f;

        Basis basis = BasisForNormal(outward)
            * new Basis(Vector3.Up, yaw)
            * Basis.Identity.Scaled(Vector3.One * TreeScale(variant) * sizeJitter);

        // Source trees have their origin at the trunk base, so they stand on the block face rather
        // than in the middle of the empty cell above it.
        Vector3 position = VoxelToWorld(voxel) + (Vector3)outward * spacing * 0.5f;
        return new Transform3D(basis, position);
    }

    private float TreeScale(string variant)
    {
        if (_treeScales.TryGetValue(variant, out float cached))
        {
            return cached;
        }

        // Pack heights range from ~2.9 to ~7 source units; normalise so every variant reads as
        // roughly two blocks tall regardless of which one the hash picked.
        Aabb bounds = _assets.GetMesh(variant).GetAabb();
        float scale = bounds.Size.Y > 0.001f ? _world.Profile.BlockSpacing * 2.0f / bounds.Size.Y : 1.0f;
        _treeScales[variant] = scale;
        return scale;
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
