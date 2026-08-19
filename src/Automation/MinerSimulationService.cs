using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Generation;
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
    private readonly Dictionary<long, Node3D> _rotors = new();
    private readonly Dictionary<long, ulong> _lastDebrisAtMs = new();

    private VirtualWorld _world = null!;
    private MiningService _mining = null!;
    private WorldView _view = null!;
    private MinerCatalog _catalog = null!;
    private MiningPatternRegistry _patterns = null!;
    private SkillTreeService _skills = null!;
    private long _nextInstanceId = 1;
    private int _lastShovelSearchRadius = 1;

    public event Action? Changed;
    public event Action<MinerInstance>? MinerPlaced;

    public IReadOnlyList<MinerInstance> Miners => _miners;
    public int MaxMiningOperationsPerFrame { get; set; } = 96;

    // This is the nominal un-affinitized rate. A shovel/pickaxe/axe can exceed it while working on
    // blocks carrying matching tags; the scheduler applies that bonus without spawning more nodes.
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
        _lastShovelSearchRadius = Math.Max(1, skills.Derived.ShovelSearchRadius);
        skills.Changed += OnSkillsChanged;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach ((long id, Node3D rotor) in _rotors)
        {
            MinerInstance? miner = _miners.FirstOrDefault(candidate => candidate.InstanceId == id);
            if (miner is null || miner.Exhausted) continue;
            rotor.RotateY(dt * 9.5f);
        }

        int budget = MaxMiningOperationsPerFrame;
        bool changed = false;
        double rateMultiplier = _skills.Derived.MinerRateMultiplier;

        foreach (MinerInstance miner in _miners)
        {
            if (budget <= 0 || miner.Exhausted) continue;

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * rateMultiplier * delta;

            // Consume work dynamically rather than precomputing a request count. Block affinity can
            // refund a fraction of one work unit after a successful mine, allowing the extra work to
            // be used in the same frame while the global operation budget still caps worst-case cost.
            while (budget > 0 && miner.WorkAccumulator >= 1.0 && !miner.Exhausted)
            {
                miner.WorkAccumulator -= 1.0;
                budget--;
                if (Advance(miner, definition, emitPresentation: true))
                {
                    changed = true;
                }
                else if (miner.Exhausted)
                {
                    changed = true;
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

        if (IsShovel(definition))
        {
            BlockSample placementSample = _world.SampleVoxel(surfaceVoxel);
            if (!placementSample.Present || !MatchesAllowedTags(definition, _mining.GetBlockDefinition(placementSample.BlockId)))
            {
                return null;
            }
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
            MinerDefinition definition = _catalog.Get(snapshot.DefinitionId);

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
                // A newly purchased Terrain Scout upgrade should also be able to revive a shovel that
                // was saved while stuck with the old adjacent-only search.
                Exhausted = snapshot.Exhausted && !(IsShovel(definition) && _skills.Derived.ShovelSearchRadius > 1),
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

        // Small worlds replay exact logical work but still use the same affinity scheduler as live
        // mining. Large worlds use region aggregation elsewhere and never replay unbounded ticks.
        double seconds = Math.Min(elapsedSeconds, 7.0 * 24.0 * 60.0 * 60.0);
        long operationsLeft = operationCap;
        long minedBefore = _mining.TotalMined;
        double rateMultiplier = _skills.Derived.MinerRateMultiplier;

        foreach (MinerInstance miner in _miners)
        {
            if (operationsLeft <= 0 || miner.Exhausted) break;

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * rateMultiplier * seconds;

            while (operationsLeft > 0 && miner.WorkAccumulator >= 1.0 && !miner.Exhausted)
            {
                miner.WorkAccumulator -= 1.0;
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
        _rotors.Clear();
        _miners.Clear();
        _lastDebrisAtMs.Clear();
        _nextInstanceId = 1;
    }

    private bool Advance(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        if (IsShovel(definition))
        {
            return AdvanceShovel(miner, definition, emitPresentation);
        }

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

            BlockSample sample = _world.SampleVoxel(candidate.Value);
            if (!sample.Present) continue;

            BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
            if (!MatchesAllowedTags(definition, block))
            {
                continue;
            }

            MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: false);
            if (!result.Success) continue;

            CompleteMine(miner, definition, block, result, emitPresentation);
            return true;
        }

        return false;
    }

    private bool AdvanceShovel(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        Vector3I? candidate;
        if (miner.BlocksMined == 0)
        {
            candidate = miner.Origin;
        }
        else
        {
            candidate = FindShovelSurfaceCandidate(miner, definition, Math.Max(1, _skills.Derived.ShovelSearchRadius));
        }

        if (candidate is null)
        {
            miner.Exhausted = true;
            UpdateVisual(miner);
            return false;
        }

        miner.CandidateIndex++;
        BlockSample sample = _world.SampleVoxel(candidate.Value);
        if (!sample.Present)
        {
            // The chosen tile may have disappeared earlier in the same frame because another miner
            // reached it first. Retry on the next work unit instead of killing the shovel.
            return false;
        }

        BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
        if (!MatchesAllowedTags(definition, block) || !_world.IsExposed(candidate.Value))
        {
            return false;
        }

        MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: true);
        if (!result.Success)
        {
            return false;
        }

        CompleteMine(miner, definition, block, result, emitPresentation);
        return true;
    }

    private Vector3I? FindShovelSurfaceCandidate(MinerInstance miner, MinerDefinition definition, int maxRadius)
    {
        Vector3I start = miner.LastMinedVoxel;
        Vector3I outward = -LineMiningPattern.Cardinal(miner.Direction);
        maxRadius = Math.Clamp(maxRadius, 1, 8);

        // Search expanding Chebyshev shells. Radius 1 means genuinely neighbouring surface tiles,
        // including a one-block height change. The Terrain Scout skill extends the same deterministic
        // search to radius 5 only after the shovel would otherwise be stuck.
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Vector3I? best = null;
            float bestScore = float.PositiveInfinity;
            float bestTie = float.PositiveInfinity;

            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != radius)
                {
                    continue;
                }

                var offset = new Vector3I(x, y, z);
                int radialOffset = offset.Dot(outward);
                Vector3I tangentOffset = offset - outward * radialOffset;

                // Never advance straight inward through the hole the shovel just created. There must
                // be tangential motion to a different surface column; radial movement is allowed only
                // so the crawler can follow one-block cliffs/relief.
                if (tangentOffset == Vector3I.Zero)
                {
                    continue;
                }

                Vector3I candidate = start + offset;
                if (_world.Source.GetOutwardNormal(candidate) != outward)
                {
                    continue;
                }

                BlockSample sample = _world.SampleVoxel(candidate);
                if (!sample.Present || !_world.IsExposed(candidate))
                {
                    continue;
                }

                BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
                if (!MatchesAllowedTags(definition, block))
                {
                    continue;
                }

                float tangentDistance = tangentOffset.LengthSquared();
                float radialPenalty = Math.Abs(radialOffset) * 0.35f;
                float score = tangentDistance + radialPenalty;
                float tie = DeterministicNoise.Hash01(
                    candidate.X,
                    candidate.Y,
                    candidate.Z,
                    unchecked(_world.Profile.Seed + (int)(miner.InstanceId * 7919L)));

                if (score < bestScore - 0.0001f || (MathF.Abs(score - bestScore) <= 0.0001f && tie < bestTie))
                {
                    best = candidate;
                    bestScore = score;
                    bestTie = tie;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private void CompleteMine(
        MinerInstance miner,
        MinerDefinition definition,
        BlockDefinition block,
        MiningResult result,
        bool emitPresentation)
    {
        miner.BlocksMined++;
        miner.LastMinedVoxel = result.Voxel;
        ApplyAffinityCredit(miner, definition, block);
        _view.MarkDirtyAround(result.Voxel);
        if (emitPresentation) EmitDebris(miner, result);
        UpdateVisual(miner);
    }

    private void OnSkillsChanged()
    {
        int searchRadius = Math.Max(1, _skills.Derived.ShovelSearchRadius);
        if (searchRadius <= _lastShovelSearchRadius)
        {
            _lastShovelSearchRadius = searchRadius;
            return;
        }

        bool revived = false;
        foreach (MinerInstance miner in _miners)
        {
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            if (!miner.Exhausted || !IsShovel(definition)) continue;
            miner.Exhausted = false;
            miner.WorkAccumulator = Math.Max(miner.WorkAccumulator, 1.0);
            UpdateVisual(miner);
            revived = true;
        }

        _lastShovelSearchRadius = searchRadius;
        if (revived) Changed?.Invoke();
    }

    private static bool IsShovel(MinerDefinition definition)
        => definition.ToolClass.Equals("shovel", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAllowedTags(MinerDefinition definition, BlockDefinition block)
    {
        if (definition.AllowedBlockTags.Count == 0) return true;
        foreach (string tag in block.Tags)
        {
            if (definition.AllowedBlockTags.Contains(tag, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    private static void ApplyAffinityCredit(MinerInstance miner, MinerDefinition definition, BlockDefinition block)
    {
        double affinity = definition.RateMultiplierForTags(block.Tags);
        if (affinity <= 1.0) return;

        // A normal block costs one accumulated work unit. At 2.5x affinity it costs 0.4 units, so
        // refund 0.6. This makes specialisation compose cleanly with the global skill speed multiplier.
        miner.WorkAccumulator += 1.0 - 1.0 / affinity;
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
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);

        var root = new Node3D
        {
            Name = $"Miner_{miner.InstanceId}",
            Transform = new Transform3D(BasisForNormal(outward), MinerPosition(miner, outward, spacing)),
        };
        AddChild(root);

        if (IsShovel(definition))
        {
            BuildShovelVisual(root, spacing);
        }
        else
        {
            BuildDrillVisual(root, miner.InstanceId, spacing);
        }

        _visuals[miner.InstanceId] = root;
    }

    private static void BuildShovelVisual(Node3D root, float spacing)
    {
        // Root local +Y is the outward face normal and the shovel model is handle-up, so it plants
        // blade-first into the working block with a slight lean.
        float scale = ShovelScale * spacing;
        var model = ShovelScene.Instantiate<Node3D>();
        model.Transform = new Transform3D(
            new Basis(Vector3.Back, Mathf.DegToRad(14.0f)).Scaled(Vector3.One * scale),
            ShovelRecentre * scale);
        root.AddChild(model);
    }

    private void BuildDrillVisual(Node3D root, long instanceId, float spacing)
    {
        var housingMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.34f, 0.38f, 0.43f),
            Roughness = 0.82f,
            Metallic = 0.18f,
        };
        var steelMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.67f, 0.72f),
            Roughness = 0.64f,
            Metallic = 0.34f,
        };
        var accentMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.58f, 0.12f),
            EmissionEnabled = true,
            Emission = new Color(0.62f, 0.24f, 0.04f),
            EmissionEnergyMultiplier = 0.55f,
            Roughness = 0.7f,
        };

        var housing = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.23f,
                BottomRadius = spacing * 0.23f,
                Height = spacing * 0.50f,
                RadialSegments = 12,
                Material = housingMaterial,
            },
            Position = Vector3.Up * spacing * 0.28f,
        };
        root.AddChild(housing);

        var cap = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.17f,
                BottomRadius = spacing * 0.21f,
                Height = spacing * 0.12f,
                RadialSegments = 12,
                Material = accentMaterial,
            },
            Position = Vector3.Up * spacing * 0.59f,
        };
        root.AddChild(cap);

        var rotor = new Node3D { Name = "Rotor" };
        root.AddChild(rotor);

        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.075f,
                BottomRadius = spacing * 0.075f,
                Height = spacing * 0.48f,
                RadialSegments = 10,
                Material = steelMaterial,
            },
            Position = Vector3.Down * spacing * 0.17f,
        });

        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.0f,
                BottomRadius = spacing * 0.18f,
                Height = spacing * 0.38f,
                RadialSegments = 12,
                Material = steelMaterial,
            },
            Position = Vector3.Down * spacing * 0.58f,
        });

        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.Tau / 3.0f;
            var fin = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(spacing * 0.06f, spacing * 0.26f, spacing * 0.28f),
                    Material = steelMaterial,
                },
                Position = new Vector3(
                    MathF.Cos(angle) * spacing * 0.12f,
                    -spacing * 0.40f,
                    MathF.Sin(angle) * spacing * 0.12f),
                Rotation = new Vector3(0, -angle, 0),
            };
            rotor.AddChild(fin);
        }

        _rotors[instanceId] = rotor;
    }

    private void UpdateVisual(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        Vector3I outward = -miner.Direction;
        root.Position = MinerPosition(miner, outward, _world.Profile.BlockSpacing);
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

    private static Vector3 MinerPosition(MinerInstance miner, Vector3I outward, float spacing)
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
