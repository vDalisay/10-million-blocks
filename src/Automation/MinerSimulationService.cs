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
        int budget = MaxMiningOperationsPerFrame;
        bool changed = false;
        double rateMultiplier = _skills.Derived.MinerRateMultiplier;

        foreach (MinerInstance miner in _miners)
        {
            if (budget <= 0 || miner.Exhausted)
            {
                continue;
            }

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * rateMultiplier * delta;

            int requested = Math.Min((int)Math.Floor(miner.WorkAccumulator), budget);
            if (requested <= 0)
            {
                continue;
            }

            miner.WorkAccumulator -= requested;
            for (int i = 0; i < requested && budget > 0; i++)
            {
                budget--;
                if (Advance(miner, definition))
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

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public MinerInstance? PlaceLineMiner(Vector3I surfaceVoxel)
        => PlaceMiner("line_miner", surfaceVoxel);

    public MinerInstance? PlaceMiner(string definitionId, Vector3I surfaceVoxel)
    {
        if (!_skills.IsMinerUnlocked(definitionId))
        {
            return null;
        }

        if (!_world.IsPresent(surfaceVoxel) || !_world.IsExposed(surfaceVoxel))
        {
            return null;
        }

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
        };

        _miners.Add(instance);
        BuildVisual(instance, outward);
        MinerPlaced?.Invoke(instance);
        Changed?.Invoke();
        return instance;
    }

    private bool Advance(MinerInstance miner, MinerDefinition definition)
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

            if (!_world.IsPresent(candidate.Value))
            {
                continue;
            }

            MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: false);
            if (!result.Success)
            {
                continue;
            }

            miner.BlocksMined++;
            _view.MarkDirtyAround(candidate.Value);
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
            if (current++ == index)
            {
                return candidate;
            }
        }

        return null;
    }

    private void BuildVisual(MinerInstance miner, Vector3I outward)
    {
        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = ((Vector3)miner.Origin + (Vector3)outward * 0.72f) * spacing;

        var root = new Node3D
        {
            Name = $"Miner_{miner.InstanceId}",
            Transform = new Transform3D(BasisForNormal(outward), position),
        };
        AddChild(root);

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.76f, 0.92f),
            EmissionEnabled = true,
            Emission = new Color(0.03f, 0.14f, 0.18f),
            Roughness = 0.55f,
        };

        root.AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh
            {
                Size = new Vector3(spacing * 0.48f, spacing * 0.30f, spacing * 0.48f),
                Material = material,
            },
        });

        root.AddChild(new MeshInstance3D
        {
            Name = "BoreDirection",
            Position = new Vector3(0.0f, -spacing * 0.34f, 0.0f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(spacing * 0.14f, spacing * 0.45f, spacing * 0.14f),
                Material = material,
            },
        });

        _visuals[miner.InstanceId] = root;
    }

    private void UpdateVisual(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root))
        {
            return;
        }

        float pulse = miner.Exhausted ? 0.72f : 1.0f + 0.04f * MathF.Sin((float)Time.GetTicksMsec() * 0.008f);
        root.Scale = Vector3.One * pulse;
    }

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
