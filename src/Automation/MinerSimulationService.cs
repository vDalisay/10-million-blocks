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
    private const float ShovelScale = 0.22f;
    private const int MaxDrillSliceBlocks = 25;
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
    private int _lastShovelHeightTolerance;
    private string _lastDrillPatternId = "line";

    public event Action? Changed;
    public event Action<MinerInstance>? MinerPlaced;

    public IReadOnlyList<MinerInstance> Miners => _miners;
    public int MaxMiningOperationsPerFrame { get; set; } = 96;
    public bool IsMinerUnlocked(string minerId) => _skills.IsMinerUnlocked(minerId);

    public double BlocksPerSecond => _miners
        .Where(miner => !miner.Exhausted)
        .Sum(miner =>
        {
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            return definition.BaseRate
                * EffectiveRateMultiplier(definition)
                * EstimatedBlocksPerWorkUnit(definition);
        });

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
        _lastShovelHeightTolerance = Math.Max(0, skills.Derived.ShovelHeightTolerance);
        _lastDrillPatternId = skills.Derived.DrillPatternId;
        _lastDrillMaterialTier = Math.Max(0, skills.Derived.DrillMaterialTier);
        skills.Changed += OnSkillsChanged;
    }

    public override void _Process(double delta)
    {
        RefreshAutomationVisualVisibility(delta);

        float dt = (float)delta;
        foreach ((long id, Node3D rotor) in _rotors)
        {
            MinerInstance? miner = _miners.FirstOrDefault(candidate => candidate.InstanceId == id);
            if (miner is null || miner.Exhausted) continue;
            if (_visuals.TryGetValue(id, out Node3D? visual) && !visual.Visible) continue;
            rotor.RotateY(dt * 9.5f);
        }

        int budget = MaxMiningOperationsPerFrame;
        bool changed = false;

        foreach (MinerInstance miner in _miners)
        {
            if (budget <= 0 || miner.Exhausted) continue;

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * EffectiveRateMultiplier(definition) * delta;

            while (budget > 0 && miner.WorkAccumulator >= 1.0 && !miner.Exhausted)
            {
                miner.WorkAccumulator -= 1.0;
                budget--;
                if (Advance(miner, definition, emitPresentation: true) || miner.Exhausted)
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

        BlockSample placementSample = _world.SampleVoxel(surfaceVoxel);
        if (!placementSample.Present || !_world.IsExposed(surfaceVoxel)) return null;

        MinerDefinition definition = _catalog.Get(definitionId);
        string patternId = EffectivePatternId(definition);
        if (!_patterns.Contains(patternId))
        {
            throw new InvalidOperationException(
                $"Miner '{definition.Id}' references unknown effective pattern '{patternId}'.");
        }

        if (IsShovel(definition) && !IsShovelMaterial(placementSample))
        {
            return null;
        }

        if (IsAxe(definition) && !IsTreeAnchor(surfaceVoxel))
        {
            return null;
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
            StopReason = miner.StopReason,
            BlockedX = miner.BlockedVoxel.X,
            BlockedY = miner.BlockedVoxel.Y,
            BlockedZ = miner.BlockedVoxel.Z,
            BlockedBlockId = miner.BlockedBlockId,
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
                Exhausted = snapshot.Exhausted,
                StopReason = snapshot.StopReason,
                BlockedVoxel = new Vector3I(snapshot.BlockedX, snapshot.BlockedY, snapshot.BlockedZ),
                BlockedBlockId = snapshot.BlockedBlockId ?? string.Empty,
            };

            if (miner.Direction == Vector3I.Zero) continue;
            _miners.Add(miner);
            BuildVisual(miner, -miner.Direction);

            bool smarterShovel = IsShovel(definition)
                && miner.StopReason == MinerStopReason.NoReachableTarget
                && (_skills.Derived.ShovelSearchRadius > 1 || _skills.Derived.ShovelHeightTolerance > 0);
            if (smarterShovel || BlockerIsNowSupported(miner))
            {
                ResumeMiner(miner, grantImmediateWork: false);
            }
            else
            {
                UpdateVisual(miner);
            }

            maxId = Math.Max(maxId, miner.InstanceId);
        }

        _nextInstanceId = Math.Max(1L, maxId + 1L);
        Changed?.Invoke();
    }

    public long ApplyOfflineProgress(double elapsedSeconds, long operationCap = 50_000)
    {
        if (elapsedSeconds <= 0.0 || operationCap <= 0 || _miners.Count == 0) return 0L;

        double seconds = Math.Min(elapsedSeconds, 7.0 * 24.0 * 60.0 * 60.0);
        long operationsLeft = operationCap;
        long minedBefore = _mining.TotalMined;

        foreach (MinerInstance miner in _miners)
        {
            if (operationsLeft <= 0 || miner.Exhausted) break;
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            miner.WorkAccumulator += definition.BaseRate * EffectiveRateMultiplier(definition) * seconds;

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
        if (IsShovel(definition)) return AdvanceShovel(miner, definition, emitPresentation);
        if (IsAxe(definition)) return AdvanceAxe(miner, definition, emitPresentation);
        if (IsPrimaryDrill(definition)) return AdvancePrimaryDrill(miner, definition, emitPresentation);
        return AdvanceGenericPatternMiner(miner, definition, emitPresentation);
    }

    private bool AdvancePrimaryDrill(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        if (miner.CandidateIndex >= definition.Range)
        {
            StopMiner(miner, MinerStopReason.RangeComplete);
            return false;
        }

        int depth = miner.CandidateIndex;
        Vector3I inward = LineMiningPattern.Cardinal(miner.Direction);
        Vector3I center = miner.Origin + inward * depth;
        bool centerAlreadyMined = _world.State.IsMined(center);
        BlockSample centerSample = _world.SampleVoxel(center);

        if (!centerSample.Present && !centerAlreadyMined)
        {
            StopMiner(miner, MinerStopReason.RangeComplete, center);
            return false;
        }

        if (centerSample.Present && !CanPrimaryDrillMine(centerSample))
        {
            StopMiner(miner, MinerStopReason.BlockedMaterial, center, centerSample.BlockId);
            return false;
        }

        string patternId = EffectivePatternId(definition);
        int width = patternId == "wide_line" ? 3 : 1;
        (Vector3I axisA, Vector3I axisB) = LineMiningPattern.PerpendicularAxes(inward);

        // Wide drills preflight the whole cutter face. One unsupported block physically blocks the
        // machine instead of being silently skipped. Empty/already-mined cells are harmless cavities.
        if (patternId == "wide_line")
        {
            int radius = Math.Max(1, width / 2);
            for (int a = -radius; a <= radius; a++)
            for (int b = -radius; b <= radius; b++)
            {
                Vector3I candidate = center + axisA * a + axisB * b;
                if (_world.State.IsMined(candidate)) continue;
                BlockSample sample = _world.SampleVoxel(candidate);
                if (!sample.Present) continue;
                if (!CanPrimaryDrillMine(sample))
                {
                    StopMiner(miner, MinerStopReason.BlockedMaterial, candidate, sample.BlockId);
                    return false;
                }
            }
        }

        miner.CandidateIndex++;
        int minedThisStep = 0;

        if (patternId == "wide_line")
        {
            int radius = Math.Max(1, width / 2);
            for (int a = -radius; a <= radius && minedThisStep < MaxDrillSliceBlocks; a++)
            for (int b = -radius; b <= radius && minedThisStep < MaxDrillSliceBlocks; b++)
            {
                Vector3I candidate = center + axisA * a + axisB * b;
                if (TryMineAutomated(miner, definition, candidate, emitPresentation && minedThisStep < 3))
                {
                    minedThisStep++;
                }
            }
        }
        else if (!centerAlreadyMined && TryMineAutomated(miner, definition, center, emitPresentation))
        {
            minedThisStep = 1;
        }

        miner.LastMinedVoxel = center;
        if (miner.CandidateIndex >= definition.Range)
        {
            StopMiner(miner, MinerStopReason.RangeComplete);
        }
        else
        {
            UpdateVisual(miner);
        }
        return minedThisStep > 0 || centerAlreadyMined;
    }

    private bool AdvanceGenericPatternMiner(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        string patternId = definition.PatternId;
        IMiningPattern pattern = _patterns.Get(patternId);
        int width = PatternWidthFor(definition, patternId);
        int safety = Math.Max(16, definition.Range * Math.Max(1, width * width));

        while (safety-- > 0)
        {
            Vector3I? candidate = CandidateAt(pattern, miner, definition, width, miner.CandidateIndex++);
            if (candidate is null)
            {
                StopMiner(miner, MinerStopReason.RangeComplete);
                return false;
            }

            if (TryMineAutomated(miner, definition, candidate.Value, emitPresentation))
            {
                miner.LastMinedVoxel = candidate.Value;
                UpdateVisual(miner);
                return true;
            }
        }

        return false;
    }

    private bool TryMineAutomated(
        MinerInstance miner,
        MinerDefinition definition,
        Vector3I candidate,
        bool emitPresentation)
    {
        BlockSample sample = _world.SampleVoxel(candidate);
        if (!sample.Present) return false;

        BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
        if (!MatchesAllowedTags(definition, block)) return false;

        MiningResult result = _mining.TryMine(candidate, MiningSource.Automated, requireExposed: false);
        if (!result.Success) return false;

        miner.BlocksMined++;
        ApplyAffinityCredit(miner, definition, block);

        // State always changes, presentation only changes when this area can currently contribute
        // pixels. Off-screen/back-side/interior automation stays computational-only until revisited.
        _view.MarkAutomationDirty(result.Voxel);
        if (emitPresentation && ShouldEmitPresentation(miner, result.Voxel))
        {
            EmitDebris(miner, result);
        }
        return true;
    }

    private bool AdvanceShovel(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        Vector3I outward = -LineMiningPattern.Cardinal(miner.Direction);
        Vector3I? candidate;

        if (miner.BlocksMined == 0 && TryGetValidShovelBlock(miner.Origin, outward, enforceFace: false))
        {
            candidate = miner.Origin;
        }
        else
        {
            candidate = FindShovelSurfaceCandidate(
                miner,
                Math.Max(1, _skills.Derived.ShovelSearchRadius),
                Math.Max(0, _skills.Derived.ShovelHeightTolerance));
        }

        if (candidate is null)
        {
            StopMiner(miner, MinerStopReason.NoReachableTarget);
            return false;
        }

        miner.CandidateIndex++;
        if (!TryGetValidShovelBlock(candidate.Value, outward, enforceFace: miner.BlocksMined > 0)) return false;

        MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: true);
        if (!result.Success) return false;

        miner.BlocksMined++;
        miner.LastMinedVoxel = result.Voxel;
        _view.MarkAutomationDirty(result.Voxel);
        if (emitPresentation && ShouldEmitPresentation(miner, result.Voxel)) EmitDebris(miner, result);
        UpdateVisual(miner);
        return true;
    }

    private Vector3I? FindShovelSurfaceCandidate(MinerInstance miner, int maxRadius, int heightTolerance)
    {
        Vector3I start = miner.LastMinedVoxel;
        Vector3I outward = -LineMiningPattern.Cardinal(miner.Direction);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);
        maxRadius = Math.Clamp(maxRadius, 1, 8);
        heightTolerance = Math.Clamp(heightTolerance, 0, 3);

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Vector3I? best = null;
            float bestScore = float.PositiveInfinity;
            float bestTie = float.PositiveInfinity;

            for (int a = -radius; a <= radius; a++)
            for (int b = -radius; b <= radius; b++)
            {
                if (a == 0 && b == 0) continue;
                if (Math.Max(Math.Abs(a), Math.Abs(b)) != radius) continue;
                if (radius == 1 && Math.Abs(a) + Math.Abs(b) != 1) continue;

                for (int height = 0; height <= heightTolerance; height++)
                {
                    int attempts = height == 0 ? 1 : 2;
                    for (int sign = 0; sign < attempts; sign++)
                    {
                        int radialOffset = height == 0 ? 0 : (sign == 0 ? height : -height);
                        Vector3I candidate = start + tangentA * a + tangentB * b + outward * radialOffset;
                        if (!TryGetValidShovelBlock(candidate, outward, enforceFace: true)) continue;

                        float score = a * a + b * b + Math.Abs(radialOffset) * 0.35f;
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
                }
            }

            if (best is not null) return best;
        }

        return null;
    }

    private bool TryGetValidShovelBlock(Vector3I candidate, Vector3I outward, bool enforceFace)
    {
        BlockSample sample = _world.SampleVoxel(candidate);
        if (!sample.Present || !_world.IsExposed(candidate) || !IsShovelMaterial(sample)) return false;
        if (enforceFace && _world.Source.GetOutwardNormal(candidate) != outward) return false;
        return true;
    }

    private bool IsShovelMaterial(BlockSample sample)
    {
        if (sample.BlockId == _world.Profile.SandBlock || sample.BlockId == _world.Profile.SurfaceEdgeBlock) return true;
        BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
        return block.Tags.Contains("sand", StringComparer.Ordinal);
    }

    private bool AdvanceAxe(MinerInstance miner, MinerDefinition definition, bool emitPresentation)
    {
        Vector3I outward = -LineMiningPattern.Cardinal(miner.Direction);
        Vector3I? candidate = miner.BlocksMined == 0 && IsTreeAnchor(miner.Origin)
            ? miner.Origin
            : FindTreeCandidate(miner, outward, 12);

        if (candidate is null)
        {
            StopMiner(miner, MinerStopReason.NoTreeTarget);
            return false;
        }

        BlockSample sample = _world.SampleVoxel(candidate.Value);
        BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
        MiningResult result = _mining.TryMine(candidate.Value, MiningSource.Automated, requireExposed: true);
        if (!result.Success) return false;

        miner.CandidateIndex++;
        miner.BlocksMined++;
        miner.LastMinedVoxel = result.Voxel;
        ApplyAffinityCredit(miner, definition, block);
        _view.MarkAutomationDirty(result.Voxel);
        if (emitPresentation && ShouldEmitPresentation(miner, result.Voxel)) EmitDebris(miner, result);
        UpdateVisual(miner);
        return true;
    }

    private Vector3I? FindTreeCandidate(MinerInstance miner, Vector3I outward, int maxRadius)
    {
        Vector3I start = miner.LastMinedVoxel;
        (Vector3I axisA, Vector3I axisB) = LineMiningPattern.PerpendicularAxes(outward);

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            Vector3I? best = null;
            float bestTie = float.PositiveInfinity;
            for (int a = -radius; a <= radius; a++)
            for (int b = -radius; b <= radius; b++)
            {
                if (Math.Max(Math.Abs(a), Math.Abs(b)) != radius) continue;
                for (int height = -2; height <= 2; height++)
                {
                    Vector3I candidate = start + axisA * a + axisB * b + outward * height;
                    if (_world.Source.GetOutwardNormal(candidate) != outward || !IsTreeAnchor(candidate)) continue;
                    float tie = DeterministicNoise.Hash01(candidate.X, candidate.Y, candidate.Z, _world.Profile.Seed + 88001);
                    if (tie < bestTie)
                    {
                        best = candidate;
                        bestTie = tie;
                    }
                }
            }
            if (best is not null) return best;
        }

        return null;
    }

    private bool IsTreeAnchor(Vector3I voxel)
        => _world.IsPresent(voxel)
            && _world.IsExposed(voxel)
            && _world.Source.TrySampleTree(voxel, out _);

    private void OnSkillsChanged()
    {
        int searchRadius = Math.Max(1, _skills.Derived.ShovelSearchRadius);
        int heightTolerance = Math.Max(0, _skills.Derived.ShovelHeightTolerance);
        int materialTier = Math.Max(0, _skills.Derived.DrillMaterialTier);
        bool intelligenceIncreased = searchRadius > _lastShovelSearchRadius
            || heightTolerance > _lastShovelHeightTolerance;
        bool materialCapabilityIncreased = materialTier > _lastDrillMaterialTier;
        bool drillPatternChanged = !string.Equals(
            _skills.Derived.DrillPatternId,
            _lastDrillPatternId,
            StringComparison.Ordinal);

        bool changed = false;
        foreach (MinerInstance miner in _miners)
        {
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            if (IsShovel(definition))
            {
                if (intelligenceIncreased
                    && miner.Exhausted
                    && miner.StopReason == MinerStopReason.NoReachableTarget)
                {
                    ResumeMiner(miner);
                    changed = true;
                }
                continue;
            }

            if (IsPrimaryDrill(definition))
            {
                if (materialCapabilityIncreased && miner.Exhausted && BlockerIsNowSupported(miner))
                {
                    ResumeMiner(miner);
                    changed = true;
                }

                if (drillPatternChanged)
                {
                    miner.CandidateIndex = 0;
                    if (miner.StopReason != MinerStopReason.BlockedMaterial || BlockerIsNowSupported(miner))
                    {
                        ResumeMiner(miner);
                    }
                    UpdateVisual(miner);
                    changed = true;
                }
            }
        }

        _lastShovelSearchRadius = searchRadius;
        _lastShovelHeightTolerance = heightTolerance;
        _lastDrillPatternId = _skills.Derived.DrillPatternId;
        _lastDrillMaterialTier = materialTier;
        if (changed) Changed?.Invoke();
    }

    private double EffectiveRateMultiplier(MinerDefinition definition)
    {
        double multiplier = IsShovel(definition)
            ? _skills.Derived.ShovelRateMultiplier
            : IsPrimaryDrill(definition)
                ? 1.0
                : _skills.Derived.MinerRateMultiplier;

        return IsPrimaryDrill(definition) && _skills.Derived.DrillPatternId == "wide_line"
            ? multiplier * 0.75
            : multiplier;
    }

    private string EffectivePatternId(MinerDefinition definition)
        => IsPrimaryDrill(definition) ? _skills.Derived.DrillPatternId : definition.PatternId;

    private int PatternWidthFor(MinerDefinition definition, string patternId)
    {
        if (IsPrimaryDrill(definition) && patternId == "wide_line") return 3;
        if (patternId == "wide_line") return 3;
        return 1;
    }

    private double EstimatedBlocksPerWorkUnit(MinerDefinition definition)
    {
        string patternId = EffectivePatternId(definition);
        if (IsPrimaryDrill(definition) && patternId == "wide_line")
        {
            int width = PatternWidthFor(definition, patternId);
            return width * width;
        }
        return 1.0;
    }

    private static bool IsPrimaryDrill(MinerDefinition definition)
        => definition.Id.Equals("line_miner", StringComparison.Ordinal)
            && definition.ToolClass.Equals("drill", StringComparison.OrdinalIgnoreCase);

    private static bool IsShovel(MinerDefinition definition)
        => definition.ToolClass.Equals("shovel", StringComparison.OrdinalIgnoreCase);

    private static bool IsAxe(MinerDefinition definition)
        => definition.ToolClass.Equals("axe", StringComparison.OrdinalIgnoreCase);

    private static bool IsPickaxe(MinerDefinition definition)
        => definition.ToolClass.Equals("pickaxe", StringComparison.OrdinalIgnoreCase);

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
        if (IsShovel(definition)) return;
        double affinity = definition.RateMultiplierForTags(block.Tags);
        if (affinity <= 1.0) return;
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
            Transform = new Transform3D(BasisForNormal(outward), MinerPosition(miner, definition, outward, spacing)),
        };
        AddChild(root);

        if (IsShovel(definition)) BuildShovelVisual(root, spacing);
        else if (IsPickaxe(definition)) BuildPickaxeVisual(root, spacing);
        else if (IsAxe(definition)) BuildAxeVisual(root, spacing);
        else BuildDrillVisual(root, miner.InstanceId, spacing);

        _visuals[miner.InstanceId] = root;
        UpdateVisual(miner);
    }

    private static void BuildShovelVisual(Node3D root, float spacing)
    {
        float scale = ShovelScale * spacing;
        var model = ShovelScene.Instantiate<Node3D>();
        model.Transform = new Transform3D(
            new Basis(Vector3.Back, Mathf.DegToRad(14.0f)).Scaled(Vector3.One * scale),
            ShovelRecentre * scale);
        root.AddChild(model);
    }

    private static StandardMaterial3D ToolMaterial(Color color, float metallic = 0.0f)
        => new()
        {
            AlbedoColor = color,
            Roughness = 0.78f,
            Metallic = metallic,
        };

    private static void BuildPickaxeVisual(Node3D root, float spacing)
    {
        Material wood = ToolMaterial(new Color(0.42f, 0.24f, 0.10f));
        Material steel = ToolMaterial(new Color(0.62f, 0.67f, 0.72f), 0.25f);
        var pivot = new Node3D { Rotation = new Vector3(0, 0, Mathf.DegToRad(-18.0f)) };
        root.AddChild(pivot);
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(spacing * 0.12f, spacing * 0.95f, spacing * 0.12f), Material = wood },
            Position = Vector3.Up * spacing * 0.42f,
        });
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(spacing * 0.95f, spacing * 0.14f, spacing * 0.18f), Material = steel },
            Position = Vector3.Up * spacing * 0.90f,
        });
    }

    private static void BuildAxeVisual(Node3D root, float spacing)
    {
        Material wood = ToolMaterial(new Color(0.46f, 0.27f, 0.11f));
        Material steel = ToolMaterial(new Color(0.70f, 0.73f, 0.75f), 0.18f);
        var pivot = new Node3D { Rotation = new Vector3(0, 0, Mathf.DegToRad(16.0f)) };
        root.AddChild(pivot);
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(spacing * 0.12f, spacing * 0.92f, spacing * 0.12f), Material = wood },
            Position = Vector3.Up * spacing * 0.40f,
        });
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(spacing * 0.56f, spacing * 0.36f, spacing * 0.18f), Material = steel },
            Position = new Vector3(spacing * 0.18f, spacing * 0.82f, 0),
        });
    }

    private void BuildDrillVisual(Node3D root, long instanceId, float spacing)
    {
        var housingMaterial = ToolMaterial(new Color(0.34f, 0.38f, 0.43f), 0.18f);
        var steelMaterial = ToolMaterial(new Color(0.62f, 0.67f, 0.72f), 0.34f);
        var accentMaterial = ToolMaterial(new Color(0.92f, 0.58f, 0.12f));
        accentMaterial.EmissionEnabled = true;
        accentMaterial.Emission = new Color(0.62f, 0.24f, 0.04f);
        accentMaterial.EmissionEnergyMultiplier = 0.55f;

        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.43f,
                BottomRadius = spacing * 0.43f,
                Height = spacing * 0.56f,
                RadialSegments = 16,
                Material = housingMaterial,
            },
            Position = Vector3.Up * spacing * 0.31f,
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.34f,
                BottomRadius = spacing * 0.39f,
                Height = spacing * 0.14f,
                RadialSegments = 16,
                Material = accentMaterial,
            },
            Position = Vector3.Up * spacing * 0.66f,
        });

        var rotor = new Node3D { Name = "Rotor" };
        root.AddChild(rotor);
        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = spacing * 0.11f,
                BottomRadius = spacing * 0.11f,
                Height = spacing * 0.50f,
                RadialSegments = 12,
                Material = steelMaterial,
            },
            Position = Vector3.Down * spacing * 0.18f,
        });
        rotor.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.0f,
                BottomRadius = spacing * 0.24f,
                Height = spacing * 0.42f,
                RadialSegments = 14,
                Material = steelMaterial,
            },
            Position = Vector3.Down * spacing * 0.62f,
        });
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4.0f;
            rotor.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(spacing * 0.09f, spacing * 0.28f, spacing * 0.42f),
                    Material = steelMaterial,
                },
                Position = new Vector3(
                    MathF.Cos(angle) * spacing * 0.22f,
                    -spacing * 0.43f,
                    MathF.Sin(angle) * spacing * 0.22f),
                Rotation = new Vector3(0, -angle, 0),
            });
        }
        _rotors[instanceId] = rotor;
    }

    private void UpdateVisual(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        Vector3I outward = -miner.Direction;
        float spacing = _world.Profile.BlockSpacing;
        root.Position = MinerPosition(miner, definition, outward, spacing);

        float footprint = DrillFootprint(definition);
        Vector3 scale = IsShovel(definition) || IsAxe(definition) || IsPickaxe(definition)
            ? Vector3.One
            : new Vector3(footprint, 1.0f, footprint);
        if (miner.Exhausted) scale *= 0.82f;
        root.Scale = scale;
        RefreshVisualVisibility(miner);
    }

    private float DrillFootprint(MinerDefinition definition)
    {
        if (!definition.ToolClass.Equals("drill", StringComparison.OrdinalIgnoreCase)) return 1.0f;
        string patternId = EffectivePatternId(definition);
        return patternId switch
        {
            "wide_line" => 3.0f,
            _ => 1.0f,
        };
    }

    private void EmitDebris(MinerInstance miner, MiningResult result)
    {
        ulong now = Time.GetTicksMsec();
        ulong last = _lastDebrisAtMs.GetValueOrDefault(miner.InstanceId);
        if (now - last < 60UL) return;

        _lastDebrisAtMs[miner.InstanceId] = now;
        Vector3 outward = (Vector3)(-miner.Direction);
        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = _view.VoxelToWorld(result.Voxel) + outward * spacing * 0.48f;
        int seed = unchecked((int)(miner.InstanceId * 73856093L)
            ^ result.Voxel.X * 19349663
            ^ result.Voxel.Y * 83492791
            ^ result.Voxel.Z * 265443576);
        var burst = new DrillDebrisBurst { Name = $"MiningDebris_{miner.InstanceId}" };
        AddChild(burst);
        burst.Initialize(position, outward, result.BlockId, spacing, seed);
    }

    private static Vector3I MinerAnchorVoxel(MinerInstance miner, MinerDefinition definition)
    {
        if (IsShovel(definition) || IsAxe(definition))
        {
            return miner.LastMinedVoxel;
        }

        if (IsPrimaryDrill(definition))
        {
            int processedDepth = Math.Max(0, miner.CandidateIndex - 1);
            return miner.Origin + miner.Direction * processedDepth;
        }

        return miner.LastMinedVoxel;
    }

    private static Vector3 MinerPosition(MinerInstance miner, MinerDefinition definition, Vector3I outward, float spacing)
        => ((Vector3)MinerAnchorVoxel(miner, definition) + (Vector3)outward * 0.78f) * spacing;

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
