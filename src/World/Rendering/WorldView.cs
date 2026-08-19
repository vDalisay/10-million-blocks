using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
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
    private readonly HashSet<ChunkCoord> _desiredChunks = new();
    private readonly Queue<ChunkCoord> _loadQueue = new();
    private readonly HashSet<ChunkCoord> _queuedLoads = new();
    private readonly Dictionary<string, float> _treeScales = new(StringComparer.Ordinal);

    private BlockAssetRegistry _assets = null!;
    private VirtualWorld _world = null!;
    private OrbitCameraController? _camera;
    private MacroWorldProxy? _macroProxy;
    private ChunkCoord? _lastStreamingFocus;
    private double _streamRefreshTimer;
    private double _chunkBuildTotalMilliseconds;

    public int VisibleChunkCount => _chunkRoots.Count;
    public int PendingChunkRebuilds => _dirtyChunks.Count;
    public int PendingChunkLoads => _loadQueue.Count;
    public bool StreamingEnabled => _world is not null && _world.Profile.UsesStreamingRenderer;
    public int MacroInstanceCount => _macroProxy?.InstanceCount ?? 0;
    public double MacroBuildMilliseconds => _macroProxy?.BuildMilliseconds ?? 0.0;
    public long TotalChunkBuilds { get; private set; }
    public long TotalVoxelCandidatesScanned { get; private set; }
    public long StreamedChunkLoads { get; private set; }
    public long StreamedChunkUnloads { get; private set; }
    public double LastChunkBuildMilliseconds { get; private set; }
    public double AverageChunkBuildMilliseconds => TotalChunkBuilds == 0 ? 0.0 : _chunkBuildTotalMilliseconds / TotalChunkBuilds;

    public void Initialize(BlockAssetRegistry assets, VirtualWorld world, OrbitCameraController? camera = null)
    {
        _assets = assets;
        _world = world;
        _camera = camera;

        if (StreamingEnabled)
        {
            _macroProxy = new MacroWorldProxy { Name = "MacroWorldProxy" };
            AddChild(_macroProxy);
            _macroProxy.Build(world);
            RefreshStreamingSet(force: true);
        }
        else
        {
            BuildInitialChunks();
        }
    }

    public override void _Process(double delta)
    {
        if (StreamingEnabled)
        {
            _streamRefreshTimer += delta;
            if (_streamRefreshTimer >= 0.18)
            {
                _streamRefreshTimer = 0.0;
                RefreshStreamingSet(force: false);
            }
        }

        int buildBudget = StreamingEnabled ? 1 : 2;
        while (buildBudget > 0 && TryPopDirtyChunk(out ChunkCoord dirty))
        {
            if (!StreamingEnabled || _desiredChunks.Contains(dirty))
            {
                RebuildChunk(dirty);
                buildBudget--;
            }
        }

        while (buildBudget > 0 && _loadQueue.Count > 0)
        {
            ChunkCoord chunk = _loadQueue.Dequeue();
            _queuedLoads.Remove(chunk);
            if (!_desiredChunks.Contains(chunk) || _chunkRoots.ContainsKey(chunk))
            {
                continue;
            }

            RebuildChunk(chunk);
            StreamedChunkLoads++;
            buildBudget--;
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

    public void MarkRegionDirty(RegionCoord region)
    {
        foreach (ChunkCoord chunk in _chunkRoots.Keys)
        {
            if (RegionCoord.FromChunk(chunk, _world.Profile.RegionSizeInChunks) == region)
            {
                _dirtyChunks.Add(chunk);
            }
        }
    }

    public Vector3 VoxelToWorld(Vector3I voxel) => (Vector3)voxel * _world.Profile.BlockSpacing;

    private void BuildInitialChunks()
    {
        int minChunk = _world.MinChunkCoordinate;
        int maxChunk = _world.MaxChunkCoordinate;

        for (int z = minChunk; z <= maxChunk; z++)
        for (int y = minChunk; y <= maxChunk; y++)
        for (int x = minChunk; x <= maxChunk; x++)
        {
            RebuildChunk(new ChunkCoord(x, y, z));
        }
    }

    private void RefreshStreamingSet(bool force)
    {
        if (_camera?.Camera is null)
        {
            return;
        }

        Vector3 cameraPosition = _camera.Camera.GlobalPosition;
        Vector3 direction = cameraPosition.LengthSquared() > 0.001f
            ? cameraPosition.Normalized()
            : new Vector3(0.4f, 0.4f, 1.0f).Normalized();
        float maxAbs = MathF.Max(MathF.Abs(direction.X), MathF.Max(MathF.Abs(direction.Y), MathF.Abs(direction.Z)));
        float radius = MathF.Max(1.0f, _world.Profile.BaseRadius);
        Vector3 surfacePoint = direction * (radius / MathF.Max(0.0001f, maxAbs));
        var focusVoxel = new Vector3I(
            (int)MathF.Round(surfacePoint.X),
            (int)MathF.Round(surfacePoint.Y),
            (int)MathF.Round(surfacePoint.Z));
        ChunkCoord focus = ChunkCoord.FromVoxel(focusVoxel, _world.Profile.ChunkSize);

        if (!force && _lastStreamingFocus == focus)
        {
            return;
        }
        _lastStreamingFocus = focus;

        _desiredChunks.Clear();
        int radiusChunks = Math.Max(0, _world.Profile.StreamingChunkRadius);
        int depthChunks = Math.Max(1, _world.Profile.DetailedSurfaceDepthChunks);
        Vector3I faceNormal = DominantNormal(direction);
        ChunkCoord normalStep = new(faceNormal.X, faceNormal.Y, faceNormal.Z);
        (ChunkCoord tangentA, ChunkCoord tangentB) = TangentChunkAxes(faceNormal);

        for (int depth = 0; depth < depthChunks; depth++)
        for (int a = -radiusChunks; a <= radiusChunks; a++)
        for (int b = -radiusChunks; b <= radiusChunks; b++)
        {
            ChunkCoord candidate = Add(focus, Scale(normalStep, -depth));
            candidate = Add(candidate, Scale(tangentA, a));
            candidate = Add(candidate, Scale(tangentB, b));
            if (ChunkInWorldBounds(candidate))
            {
                _desiredChunks.Add(candidate);
            }
        }

        var unload = new List<ChunkCoord>();
        foreach (ChunkCoord loaded in _chunkRoots.Keys)
        {
            if (!_desiredChunks.Contains(loaded))
            {
                unload.Add(loaded);
            }
        }

        foreach (ChunkCoord chunk in unload)
        {
            Node3D root = _chunkRoots[chunk];
            _chunkRoots.Remove(chunk);
            root.QueueFree();
            StreamedChunkUnloads++;
        }

        foreach (ChunkCoord desired in _desiredChunks)
        {
            if (!_chunkRoots.ContainsKey(desired) && _queuedLoads.Add(desired))
            {
                _loadQueue.Enqueue(desired);
            }
        }
    }

    private bool ChunkInWorldBounds(ChunkCoord chunk)
        => chunk.X >= _world.MinChunkCoordinate && chunk.X <= _world.MaxChunkCoordinate
            && chunk.Y >= _world.MinChunkCoordinate && chunk.Y <= _world.MaxChunkCoordinate
            && chunk.Z >= _world.MinChunkCoordinate && chunk.Z <= _world.MaxChunkCoordinate;

    private void RebuildChunk(ChunkCoord chunk)
    {
        ulong started = Time.GetTicksUsec();
        if (_chunkRoots.Remove(chunk, out Node3D? oldRoot))
        {
            oldRoot.QueueFree();
        }

        RegionCoord region = RegionCoord.FromChunk(chunk, _world.Profile.RegionSizeInChunks);
        if (_world.State.IsRegionExhausted(region))
        {
            FinishChunkBuild(started, 0L);
            return;
        }

        int chunkSize = _world.Profile.ChunkSize;
        int max = _world.MaxCoordinate;
        Vector3I min = chunk.MinVoxel(chunkSize);
        var batches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        var treeBatches = new Dictionary<string, List<Transform3D>>(StringComparer.Ordinal);
        long scanned = 0L;

        float innerSurfaceBand = MathF.Max(0.0f,
            _world.Profile.BaseRadius
            - _world.Profile.TerrainAmplitude
            - _world.Profile.DetailAmplitude
            - MathF.Abs(_world.Profile.SeaLevelOffset)
            - 5.0f);

        for (int z = 0; z < chunkSize; z++)
        for (int y = 0; y < chunkSize; y++)
        for (int x = 0; x < chunkSize; x++)
        {
            Vector3I voxel = min + new Vector3I(x, y, z);
            if (Math.Abs(voxel.X) > max || Math.Abs(voxel.Y) > max || Math.Abs(voxel.Z) > max)
            {
                continue;
            }

            if (StreamingEnabled && MaxAbs(voxel) < innerSurfaceBand)
            {
                continue;
            }

            scanned++;
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
                && _world.Source.TrySampleTree(voxel, out FeatureSample feature)
                && !_world.IsPresent(voxel + feature.OutwardNormal))
            {
                AddTransform(treeBatches, PickTree(voxel), TreeTransform(voxel, feature.OutwardNormal));
            }
        }

        if (batches.Count == 0 && treeBatches.Count == 0)
        {
            FinishChunkBuild(started, scanned);
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

        _chunkRoots[chunk] = chunkRoot;
        FinishChunkBuild(started, scanned);
    }

    private void FinishChunkBuild(ulong startedUsec, long scanned)
    {
        LastChunkBuildMilliseconds = (Time.GetTicksUsec() - startedUsec) / 1000.0;
        _chunkBuildTotalMilliseconds += LastChunkBuildMilliseconds;
        TotalChunkBuilds++;
        TotalVoxelCandidatesScanned = checked(TotalVoxelCandidatesScanned + scanned);
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

        Vector3 position = VoxelToWorld(voxel) + (Vector3)outward * spacing * 0.5f;
        return new Transform3D(basis, position);
    }

    private float TreeScale(string variant)
    {
        if (_treeScales.TryGetValue(variant, out float cached))
        {
            return cached;
        }

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

    private static float MaxAbs(Vector3I voxel)
        => Math.Max(Math.Abs(voxel.X), Math.Max(Math.Abs(voxel.Y), Math.Abs(voxel.Z)));

    private static Vector3I DominantNormal(Vector3 direction)
    {
        float ax = MathF.Abs(direction.X);
        float ay = MathF.Abs(direction.Y);
        float az = MathF.Abs(direction.Z);
        if (ax >= ay && ax >= az) return direction.X >= 0.0f ? Vector3I.Right : Vector3I.Left;
        if (ay >= ax && ay >= az) return direction.Y >= 0.0f ? Vector3I.Up : Vector3I.Down;
        return direction.Z >= 0.0f ? Vector3I.Back : Vector3I.Forward;
    }

    private static (ChunkCoord A, ChunkCoord B) TangentChunkAxes(Vector3I normal)
    {
        if (Math.Abs(normal.X) == 1) return (new ChunkCoord(0, 1, 0), new ChunkCoord(0, 0, 1));
        if (Math.Abs(normal.Y) == 1) return (new ChunkCoord(1, 0, 0), new ChunkCoord(0, 0, 1));
        return (new ChunkCoord(1, 0, 0), new ChunkCoord(0, 1, 0));
    }

    private static ChunkCoord Add(ChunkCoord a, ChunkCoord b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static ChunkCoord Scale(ChunkCoord value, int scale) => new(value.X * scale, value.Y * scale, value.Z * scale);

    private static Basis BasisForNormal(Vector3I normal)
    {
        if (normal == Vector3I.Up) return Basis.Identity;
        if (normal == Vector3I.Down) return new Basis(Vector3.Right, Mathf.Pi);
        if (normal == Vector3I.Right) return new Basis(Vector3.Back, -Mathf.Pi * 0.5f);
        if (normal == Vector3I.Left) return new Basis(Vector3.Back, Mathf.Pi * 0.5f);
        if (normal == Vector3I.Back) return new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        return new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
    }

    private bool TryPopDirtyChunk(out ChunkCoord chunk)
    {
        using HashSet<ChunkCoord>.Enumerator enumerator = _dirtyChunks.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            chunk = default;
            return false;
        }

        chunk = enumerator.Current;
        _dirtyChunks.Remove(chunk);
        return true;
    }
}
