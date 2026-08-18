using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Core;

namespace TenMillionBlocks.World;

public readonly record struct BlockDamageResult(
    bool Hit,
    bool Destroyed,
    BlockType Type,
    float RemainingHealth,
    float MaxHealth,
    int Reward);

public sealed partial class VoxelWorld : Node3D
{
    public event Action? WorldCleared;
    public event Action<int, int>? BlockCountChanged;
    public event Action? WorldGenerated;

    private readonly Dictionary<Vector3I, BlockType> _blocks = [];
    private readonly Dictionary<Vector3I, float> _damage = [];
    private readonly HashSet<Vector3I> _surfaceBlocks = [];
    private readonly Dictionary<Vector3I, MeshInstance3D> _chunkMeshes = [];
    private readonly HashSet<Vector3I> _dirtyChunks = [];

    private Node3D? _decorationRoot;
    private StandardMaterial3D? _voxelMaterial;

    public int TargetBlockCount { get; private set; }
    public int RemainingBlockCount => _blocks.Count;
    public Aabb Bounds { get; private set; } = new(Vector3.Zero, Vector3.One);
    public int Seed { get; private set; }

    public override void _Ready()
    {
        EnsureMaterial();
    }

    public override void _Process(double delta)
    {
        if (_dirtyChunks.Count == 0)
        {
            return;
        }

        int rebuilt = 0;
        foreach (Vector3I chunkKey in _dirtyChunks.ToArray())
        {
            _dirtyChunks.Remove(chunkKey);
            RebuildChunk(chunkKey);

            rebuilt++;
            if (rebuilt >= GameConfig.MaxDirtyChunksPerFrame)
            {
                break;
            }
        }
    }

    public void GenerateWorld(int targetBlockCount, int seed)
    {
        TargetBlockCount = targetBlockCount;
        Seed = seed;
        EnsureMaterial();

        _blocks.Clear();
        _damage.Clear();
        _surfaceBlocks.Clear();
        _dirtyChunks.Clear();

        foreach ((Vector3I position, BlockType type) in ProceduralWorldGenerator.Generate(targetBlockCount, seed))
        {
            _blocks[position] = type;
        }

        RecalculateBounds();
        RebuildSurfaceCache();
        RebuildAllChunks();
        BuildDecorations();

        BlockCountChanged?.Invoke(RemainingBlockCount, TargetBlockCount);
        WorldGenerated?.Invoke();
    }

    public bool HasBlock(Vector3I coordinate)
        => _blocks.ContainsKey(coordinate);

    public bool TryGetBlock(Vector3I coordinate, out BlockType type)
        => _blocks.TryGetValue(coordinate, out type);

    public BlockDamageResult DamageBlock(Vector3I coordinate, float damage)
    {
        if (damage <= 0.0f || !_blocks.TryGetValue(coordinate, out BlockType type))
        {
            return default;
        }

        BlockDefinition definition = BlockPalette.Get(type);
        float accumulatedDamage = _damage.GetValueOrDefault(coordinate) + damage;
        float remaining = MathF.Max(0.0f, definition.Hardness - accumulatedDamage);

        if (remaining > 0.0f)
        {
            _damage[coordinate] = accumulatedDamage;
            return new BlockDamageResult(true, false, type, remaining, definition.Hardness, 0);
        }

        _damage.Remove(coordinate);
        _blocks.Remove(coordinate);
        UpdateSurfaceCacheAround(coordinate);
        MarkDirtyAround(coordinate);

        BlockCountChanged?.Invoke(RemainingBlockCount, TargetBlockCount);

        if (_blocks.Count == 0)
        {
            WorldCleared?.Invoke();
        }

        return new BlockDamageResult(true, true, type, 0.0f, definition.Hardness, definition.Reward);
    }

    public bool TryGetRandomSurfaceBlock(RandomNumberGenerator rng, out Vector3I coordinate)
    {
        coordinate = default;
        if (_surfaceBlocks.Count == 0)
        {
            return false;
        }

        int skip = rng.RandiRange(0, _surfaceBlocks.Count - 1);
        foreach (Vector3I candidate in _surfaceBlocks)
        {
            if (skip-- == 0)
            {
                coordinate = candidate;
                return true;
            }
        }

        return false;
    }

    private void RebuildAllChunks()
    {
        foreach (MeshInstance3D mesh in _chunkMeshes.Values)
        {
            mesh.QueueFree();
        }

        _chunkMeshes.Clear();

        var chunkKeys = new HashSet<Vector3I>();
        foreach (Vector3I coordinate in _blocks.Keys)
        {
            chunkKeys.Add(ToChunkKey(coordinate));
        }

        foreach (Vector3I chunkKey in chunkKeys)
        {
            RebuildChunk(chunkKey);
        }
    }

    private void RebuildChunk(Vector3I chunkKey)
    {
        if (_chunkMeshes.Remove(chunkKey, out MeshInstance3D? existing))
        {
            existing.QueueFree();
        }

        ArrayMesh? mesh = VoxelMesher.BuildChunk(_blocks, chunkKey, GameConfig.ChunkSize);
        if (mesh is null)
        {
            return;
        }

        var instance = new MeshInstance3D
        {
            Name = $"Chunk_{chunkKey.X}_{chunkKey.Y}_{chunkKey.Z}",
            Mesh = mesh,
            MaterialOverride = _voxelMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };

        AddChild(instance);
        _chunkMeshes[chunkKey] = instance;
    }

    private void MarkDirtyAround(Vector3I coordinate)
    {
        _dirtyChunks.Add(ToChunkKey(coordinate));

        foreach (Vector3I direction in VoxelDirections.All)
        {
            _dirtyChunks.Add(ToChunkKey(coordinate + direction));
        }
    }

    private static Vector3I ToChunkKey(Vector3I coordinate)
        => new(
            FloorDiv(coordinate.X, GameConfig.ChunkSize),
            FloorDiv(coordinate.Y, GameConfig.ChunkSize),
            FloorDiv(coordinate.Z, GameConfig.ChunkSize));

    private static int FloorDiv(int value, int divisor)
        => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    private void RebuildSurfaceCache()
    {
        _surfaceBlocks.Clear();
        foreach (Vector3I coordinate in _blocks.Keys)
        {
            if (IsSurface(coordinate))
            {
                _surfaceBlocks.Add(coordinate);
            }
        }
    }

    private void UpdateSurfaceCacheAround(Vector3I removedCoordinate)
    {
        _surfaceBlocks.Remove(removedCoordinate);

        foreach (Vector3I direction in VoxelDirections.All)
        {
            Vector3I neighbor = removedCoordinate + direction;
            if (_blocks.ContainsKey(neighbor) && IsSurface(neighbor))
            {
                _surfaceBlocks.Add(neighbor);
            }
        }
    }

    private bool IsSurface(Vector3I coordinate)
    {
        foreach (Vector3I direction in VoxelDirections.All)
        {
            if (!_blocks.ContainsKey(coordinate + direction))
            {
                return true;
            }
        }

        return false;
    }

    private void RecalculateBounds()
    {
        if (_blocks.Count == 0)
        {
            Bounds = new Aabb(Vector3.Zero, Vector3.One);
            return;
        }

        Vector3I min = _blocks.Keys.First();
        Vector3I max = min;

        foreach (Vector3I coordinate in _blocks.Keys)
        {
            min = new Vector3I(
                Math.Min(min.X, coordinate.X),
                Math.Min(min.Y, coordinate.Y),
                Math.Min(min.Z, coordinate.Z));
            max = new Vector3I(
                Math.Max(max.X, coordinate.X),
                Math.Max(max.Y, coordinate.Y),
                Math.Max(max.Z, coordinate.Z));
        }

        Bounds = new Aabb(
            (Vector3)min - Vector3.One * 0.5f,
            (Vector3)(max - min) + Vector3.One);
    }

    private void EnsureMaterial()
    {
        _voxelMaterial ??= new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.92f,
        };
    }

    private void BuildDecorations()
    {
        _decorationRoot?.QueueFree();
        _decorationRoot = new Node3D { Name = "Decorations" };
        AddChild(_decorationRoot);

        if (TargetBlockCount < 100)
        {
            return;
        }

        var topGrass = _blocks
            .Where(pair => pair.Value == BlockType.Grass && !_blocks.ContainsKey(pair.Key + Vector3I.Up))
            .Select(pair => pair.Key)
            .OrderByDescending(position => position.Y)
            .ToArray();

        BuildTrees(topGrass);
        BuildRuin(topGrass);
    }

    private void BuildTrees(Vector3I[] topGrass)
    {
        if (_decorationRoot is null || topGrass.Length == 0)
        {
            return;
        }

        var trunkTransforms = new List<Transform3D>();
        var leafTransforms = new List<Transform3D>();

        int desiredTrees = Math.Clamp((int)Math.Sqrt(TargetBlockCount) / 5, 2, 18);
        foreach (Vector3I ground in topGrass)
        {
            if (trunkTransforms.Count >= desiredTrees * 2)
            {
                break;
            }

            float hash = ProceduralWorldGenerator.Hash01(ground.X, ground.Y, ground.Z, Seed ^ 0x2A53);
            if (hash > 0.18f)
            {
                continue;
            }

            Vector3 basePosition = (Vector3)ground + Vector3.Up;
            trunkTransforms.Add(new Transform3D(Basis.Identity, basePosition + Vector3.Up * 0.15f));
            trunkTransforms.Add(new Transform3D(Basis.Identity, basePosition + Vector3.Up * 0.65f));

            Vector3 canopy = basePosition + Vector3.Up * 1.2f;
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy));
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy + Vector3.Left * 0.45f));
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy + Vector3.Right * 0.45f));
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy + Vector3.Forward * 0.45f));
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy + Vector3.Back * 0.45f));
            leafTransforms.Add(new Transform3D(Basis.Identity, canopy + Vector3.Up * 0.42f));
        }

        AddMultiMesh("TreeTrunks", trunkTransforms, new Vector3(0.28f, 0.48f, 0.28f), new Color(0.36f, 0.20f, 0.08f));
        AddMultiMesh("TreeLeaves", leafTransforms, new Vector3(0.55f, 0.55f, 0.55f), new Color(0.19f, 0.58f, 0.18f));
    }

    private void BuildRuin(Vector3I[] topGrass)
    {
        if (_decorationRoot is null || TargetBlockCount < 1_000 || topGrass.Length == 0)
        {
            return;
        }

        Vector3I anchor = topGrass[0];
        Vector3 origin = (Vector3)anchor + Vector3.Up;
        var transforms = new List<Transform3D>();

        for (int y = 0; y < 4; y++)
        {
            transforms.Add(new Transform3D(Basis.Identity, origin + Vector3.Up * y * 0.55f));
        }

        Vector3 cap = origin + Vector3.Up * 2.15f;
        transforms.Add(new Transform3D(Basis.Identity, cap + Vector3.Left * 0.45f));
        transforms.Add(new Transform3D(Basis.Identity, cap + Vector3.Right * 0.45f));
        transforms.Add(new Transform3D(Basis.Identity, cap + Vector3.Forward * 0.45f));
        transforms.Add(new Transform3D(Basis.Identity, cap + Vector3.Back * 0.45f));

        AddMultiMesh("Ruin", transforms, new Vector3(0.52f, 0.52f, 0.52f), new Color(0.68f, 0.71f, 0.72f));
    }

    private void AddMultiMesh(string name, List<Transform3D> transforms, Vector3 size, Color color)
    {
        if (_decorationRoot is null || transforms.Count == 0)
        {
            return;
        }

        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.95f,
        };

        var box = new BoxMesh
        {
            Size = size,
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = box,
            InstanceCount = transforms.Count,
        };

        for (int i = 0; i < transforms.Count; i++)
        {
            multiMesh.SetInstanceTransform(i, transforms[i]);
        }

        var instance = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };

        _decorationRoot.AddChild(instance);
    }
}
