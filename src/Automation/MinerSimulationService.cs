using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService : Node3D
{
    // KayKit "Go Deeper" shovel. The source mesh is ~6.7 units along its handle and is modelled off
    // to one side of its origin, so it needs a scale and a recentring offset to stand on a block.
    private const float ShovelScale = 0.22f;
    private static readonly Vector3 ShovelRecentre = new(2.30f, 0.0f, -0.55f);

    private static readonly PackedScene ShovelScene = GD.Load<PackedScene>("res://Assets/godeeper/shovel.gltf");

    private readonly List<MinerInstance> _miners = new();
    private readonly Dictionary<long, Node3D> _visuals = new();
    private readonly Dictionary<long, ulong> _lastDebrisAtMs = new();

    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private WorldView _view = null!;
    private MinerCatalog _catalog = null!;
    private MiningPatternRegistry _patterns = null!;
    private SkillTreeService _skills = null!;
    private long _nextInstanceId = 1;

    public event Action? Changed;
    public event Action<MinerInstance>? MinerPlaced;

    public IReadOnlyList<MinerInstance> Miners => _miners;
    public int MaxMiningOperationsPerFrame { get; set; } = 96;

    public double BlocksPerSecond => _miners
        .Where(miner => !miner.Exhausted)
        .Sum(miner => _catalog.Get(miner.DefinitionId).BaseRate * _skills.Derived.MinerRateMultiplier);

    public void Initialize(
        VirtualWorld world,
        MiningService mining,
        WorldView view,
        MinerCatalog catalog,
        MiningPatternRegistry patterns,
        SkillTreeService skills)
    {
        _world = world;
        _mining = mining;
        _view = view;
        _catalog = catalog;
        _patterns = patterns;
        _skills = skills;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        int budget = MaxMiningOperationsPerFrame;
        bool changed = false;
        double rateMultiplier = _skills.Derived.MinerRateMultiplier;

        foreach (MinerInstance miner in _miners)
        {
            if (budget <= 0 || miner.Exhausted) continue;

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * rateMultiplier * delta;

            int requested = Math.Min((int)Math.Floor(miner.WorkAccumulator), budget);
            if (requested <= 0) continue;

            miner.WorkAccumulator -= requested;
            for (int i = 0; i < requested && budget > 0; i++)
            {
                budget--;
                if (Advance(miner, definition, emitPresentation: true))
                {
                    changed = true;
                }
                else if (miner.Exhausted)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed) Changed?.Invoke();
    }

    public MinerInstance? PlaceLineMiner(Vector3I surfaceVoxel)
        => PlaceMiner("line_miner", surfaceVoxel);

    public MinerInstance? PlaceMiner(string definitionId, Vector3I surfaceVoxel)
    {
        if (!_skills.IsMinerUnlocked(definitionId)) return null;
        if (!_world.IsPresent(surfaceVoxel) || !_world.IsExposed(surfaceVoxel)) return null;

        MinerDefinition definition = _catalog.Get(definitionId);
        if (!_patterns.Contains(definition.PatternId))
        {
            throw new InvalidOperationException(
                $"Miner '{definition.Id}' references unknown pattern '{definition.PatternId}'.");
        }

        Vector3I outward = _world.Source.GetOutwardNormal(surfaceVoxel);
        var instance = new MinerInstance
        {
            InstanceId = _nextInstanceId++,
            DefinitionId = definitionId,
            Origin = surfaceVoxel,
            Direction = -outward,
            LastMinedVoxel = surfaceVoxel,
        };

        _miners.Add(instance);
        BuildVisual(instance, outward);
        MinerPlaced?.Invoke(instance);
        Changed?.Invoke();
        return instance;
    }

    public List<MinerSnapshot> CreateSnapshot()
        => _miners.Select(miner => new MinerSnapshot
        {
            InstanceId = miner.InstanceId,
            DefinitionId = miner.DefinitionId,
            OriginX = miner.Origin.X,
            OriginY = miner.Origin.Y,
            OriginZ = miner.Origin.Z,
            DirectionX = miner.Direction.X,
            DirectionY = miner.Direction.Y,
            DirectionZ = miner.Direction.Z,
            LastX = miner.LastMinedVoxel.X,
            LastY = miner.LastMinedVoxel.Y,
            LastZ = miner.LastMinedVoxel.Z,
            CandidateIndex = miner.CandidateIndex,
            BlocksMined = miner.BlocksMined,
            WorkAccumulator = miner.WorkAccumulator,
            Exhausted = miner.Exhausted,
        }).ToList();

    public void RestoreSnapshot(IEnumerable<MinerSnapshot> snapshots)
    {
        ClearMiners();
        long maxId = 0;

        foreach (MinerSnapshot snapshot in snapshots)
        {
            if (!_catalog.Miners.ContainsKey(snapshot.DefinitionId)) continue;

            var miner = new MinerInstance
            {
                InstanceId = Math.Max(1, snapshot.InstanceId),
                DefinitionId = snapshot.DefinitionId,
                Origin = new Vector3I(snapshot.OriginX, snapshot.OriginY, snapshot.OriginZ),
                Direction = new Vector3I(snapshot.DirectionX, snapshot.DirectionY, snapshot.DirectionZ),
                LastMinedVoxel = new Vector3I(snapshot.LastX, snapshot.LastY, snapshot.LastZ),
                CandidateIndex = Math.Max(0, snapshot.CandidateIndex),
                BlocksMined = Math.Max(0L, snapshot.BlocksMined),
                WorkAccumulator = Math.Max(0.0, snapshot.WorkAccumulator),
                Exhausted = snapshot.Exhausted,
            };

            if (miner.Direction == Vector3I.Zero) continue;
            _miners.Add(miner);
            BuildVisual(miner, -miner.Direction);
            UpdateVisual(miner);
            maxId = Math.Max(maxId, miner.InstanceId);
        }

        _nextInstanceId = Math.Max(1L, maxId + 1L);
        Changed?.Invoke();
    }

    public long ApplyOfflineProgress(double elapsedSeconds, long operationCap = 50_000)
    {
        if (elapsedSeconds <= 0.0 || operationCap <= 0 || _miners.Count == 0) return 0L;

        // Current small-world implementation performs exact logical mining while suppressing visual
        // debris. It is intentionally capped. The region-aggregate path in the scale milestone will
        // replace this loop for million-scale worlds rather than ever replaying unbounded ticks.
        double seconds = Math.Min(elapsedSeconds, 7.0 * 24.0 * 60.0 * 60.0);
        long operationsLeft = operationCap;
        long minedBefore = _mining.TotalMined;
        double rateMultiplier = _skills.Derived.MinerRateMultiplier;

        foreach (MinerInstance miner in _miners)
        {
            if (operationsLeft <= 0 || miner.Exhausted) break;

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            double accumulated = miner.WorkAccumulator + definition.BaseRate * rateMultiplier * seconds;
            long requested = Math.Min((long)Math.Floor(accumulated), operationsLeft);
            miner.WorkAccumulator = accumulated - requested;

            for (long i = 0; i < requested && operationsLeft > 0 && !miner.Exhausted; i++)
            {
                operationsLeft--;
                _ = Advance(miner, definition, emitPresentation: false);
            }
        }

        long mined = _mining.TotalMined - minedBefore;
        if (mined > 0) Changed?.Invoke();
        return mined;
    }

    public void ClearMiners()
    {
        foreach (Node3D visual in _visuals.Values) visual.QueueFree();
        _visuals.Clear();
        _miners.Clear();
        _lastDebrisAtMs.Clear();
        _nextInstanceId = 1;
    }

    private bool Advance(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        IMiningPattern pattern = _patterns.Get(definition.PatternId);
        int width = definition.PatternId == "line" ? 1 : Math.Max(1, _skills.Derived.MinerPatternWidth);

        int safety = Math.Max(16, definition.Range * Math.Max(1, width * width));
        while (safety-- > 0)
        {
            Vector3I? candidate = CandidateAt(pattern, miner, definition, width, miner.CandidateIndex++);
            if (candidate is null)
            {
                miner.Exhausted = true;
                UpdateVisual(miner);
                return false;
            }

            if (!_world.IsPresent(candidate.Value)) continue;

            MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: false);
            if (!result.Success) continue;

            miner.BlocksMined++;
            miner.LastMinedVoxel = candidate.Value;
            _view.MarkDirtyAround(candidate.Value);
            if (emitPresentation) EmitDebris(miner, result);
            UpdateVisual(miner);
            return true;
        }

        return false;
    }

    private static Vector3I? CandidateAt(
        IMiningPattern pattern,
        MinerInstance miner,
        MinerDefinition definition,
        int width,
        int index)
    {
        int current = 0;
        foreach (Vector3I candidate in pattern.Enumerate(miner.Origin, miner.Direction, definition.Range, width))
        {
            if (current++ == index) return candidate;
        }
        return null;
    }

    private void BuildVisual(MinerInstance miner, Vector3I outward)
    {
        float spacing = _world.Profile.BlockSpacing;

        var root = new Node3D
        {
            Name = $"Miner_{miner.InstanceId}",
            Transform = new Transform3D(BasisForNormal(outward), DrillPosition(miner, outward, spacing)),
        };
        AddChild(root);

        // Root local +Y is the outward face normal and the shovel models handle-up, so it plants
        // blade-first into the block it is working with no extra rotation beyond a slight lean.
        float scale = ShovelScale * spacing;
        var model = ShovelScene.Instantiate<Node3D>();
        model.Transform = new Transform3D(
            new Basis(Vector3.Back, Mathf.DegToRad(14.0f)).Scaled(Vector3.One * scale),
            ShovelRecentre * scale);
        root.AddChild(model);

        _visuals[miner.InstanceId] = root;
    }

    private void UpdateVisual(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        Vector3I outward = -miner.Direction;
        root.Position = DrillPosition(miner, outward, _world.Profile.BlockSpacing);
        root.Scale = miner.Exhausted ? Vector3.One * 0.82f : Vector3.One;
    }

    private void EmitDebris(MinerInstance miner, MiningResult result)
    {
        ulong now = Time.GetTicksMsec();
        ulong last = _lastDebrisAtMs.GetValueOrDefault(miner.InstanceId);
        if (now - last < 80UL) return;

        _lastDebrisAtMs[miner.InstanceId] = now;
        Vector3 outward = (Vector3)(-miner.Direction);
        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = _view.VoxelToWorld(result.Voxel) + outward * spacing * 0.48f;
        int seed = unchecked((int)(miner.InstanceId * 73856093L)
            ^ result.Voxel.X * 19349663
            ^ result.Voxel.Y * 83492791
            ^ result.Voxel.Z * 265443576);

        var burst = new DrillDebrisBurst { Name = $"DrillDebris_{miner.InstanceId}" };
        AddChild(burst);
        burst.Initialize(position, outward, result.BlockId, spacing, seed);
    }

    private static Vector3 DrillPosition(MinerInstance miner, Vector3I outward, float spacing)
        => ((Vector3)miner.LastMinedVoxel + (Vector3)outward * 0.78f) * spacing;

    private static Basis BasisForNormal(Vector3I normal)
    {
        if (normal == Vector3I.Up) return Basis.Identity;
        if (normal == Vector3I.Down) return new Basis(Vector3.Right, Mathf.Pi);
        if (normal == Vector3I.Right) return new Basis(Vector3.Back, -Mathf.Pi * 0.5f);
        if (normal == Vector3I.Left) return new Basis(Vector3.Back, Mathf.Pi * 0.5f);
        if (normal == Vector3I.Back) return new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        return new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
    }
}
