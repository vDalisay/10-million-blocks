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
        AnimateDrills(dt);

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
        Vector3 position = DrillPosition(miner, outward, spacing);

        var root = new Node3D
        {
            Name = $"Miner_{miner.InstanceId}",
            Transform = new Transform3D(BasisForNormal(outward), position),
        };
        AddChild(root);

        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.55f, 0.68f),
            Metallic = 0.45f,
            Roughness = 0.38f,
        };
        var bitMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.77f, 0.82f),
            Metallic = 0.82f,
            Roughness = 0.24f,
        };
        var accentMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.86f, 0.96f),
            EmissionEnabled = true,
            Emission = new Color(0.025f, 0.18f, 0.22f),
            Roughness = 0.42f,
        };

        root.AddChild(new MeshInstance3D
        {
            Name = "MotorHousing",
            Position = new Vector3(0.0f, spacing * 0.18f, 0.0f),
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.25f,
                BottomRadius = spacing * 0.25f,
                Height = spacing * 0.34f,
                RadialSegments = 10,
                Material = bodyMaterial,
            },
        });

        var drillBit = new Node3D
        {
            Name = "DrillBit",
            Position = new Vector3(0.0f, -spacing * 0.18f, 0.0f),
        };
        root.AddChild(drillBit);

        drillBit.AddChild(new MeshInstance3D
        {
            Name = "Shaft",
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.11f,
                BottomRadius = spacing * 0.11f,
                Height = spacing * 0.34f,
                RadialSegments = 8,
                Material = bitMaterial,
            },
        });

        drillBit.AddChild(new MeshInstance3D
        {
            Name = "Cone",
            Position = new Vector3(0.0f, -spacing * 0.31f, 0.0f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.0f,
                BottomRadius = spacing * 0.23f,
                Height = spacing * 0.46f,
                RadialSegments = 8,
                Material = bitMaterial,
            },
        });

        for (int fin = 0; fin < 3; fin++)
        {
            float angle = fin * Mathf.Tau / 3.0f;
            drillBit.AddChild(new MeshInstance3D
            {
                Name = $"CuttingFin_{fin}",
                Position = new Vector3(
                    MathF.Cos(angle) * spacing * 0.12f,
                    -spacing * 0.16f,
                    MathF.Sin(angle) * spacing * 0.12f),
                Rotation = new Vector3(0.0f, -angle, Mathf.DegToRad(28.0f)),
                Mesh = new BoxMesh
                {
                    Size = new Vector3(spacing * 0.06f, spacing * 0.38f, spacing * 0.14f),
                    Material = bitMaterial,
                },
            });
        }

        root.AddChild(new MeshInstance3D
        {
            Name = "StatusLight",
            Position = new Vector3(0.0f, spacing * 0.40f, 0.0f),
            Mesh = new BoxMesh
            {
                Size = Vector3.One * spacing * 0.11f,
                Material = accentMaterial,
            },
        });

        _visuals[miner.InstanceId] = root;
    }

    private void UpdateVisual(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        Vector3I outward = -miner.Direction;
        root.Position = DrillPosition(miner, outward, _world.Profile.BlockSpacing);
        root.Scale = miner.Exhausted ? Vector3.One * 0.82f : Vector3.One;
    }

    private void AnimateDrills(float delta)
    {
        foreach (Node3D root in _visuals.Values)
        {
            Node3D? bit = root.GetNodeOrNull<Node3D>("DrillBit");
            if (bit is not null) bit.RotateY(delta * 9.0f);
        }
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
