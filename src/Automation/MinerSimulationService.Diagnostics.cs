using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation.MiningPatterns;
using TenMillionBlocks.Content;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Automation;

public readonly record struct DiagnosticRelocationBatch(
    int Attempted,
    int Relocated,
    int LocalRelocations,
    int FallbackRelocations,
    int Failed,
    int CooldownSkipped,
    long CandidateChecks,
    int MaximumSearchRadius,
    double SearchMilliseconds);

public partial class MinerSimulationService
{
    // Failed nearest-neighbour searches used to be retried five times per second. The supplied F11
    // trace spent 15.4 seconds of CPU time on 1.65 million relocation candidate checks, with one pass
    // reaching 392 ms. A short retry backoff keeps recovery responsive while preventing a known-bad
    // neighbourhood from being rescanned every 200 ms before the terrain around it has meaningfully
    // changed.
    private const ulong DiagnosticRelocationFailureCooldownMs = 1_000UL;

    private readonly HashSet<Vector3I> _diagnosticSpawnVoxels = new();
    private readonly HashSet<Vector3I> _diagnosticOccupiedAnchors = new();
    private readonly Dictionary<long, ulong> _diagnosticRelocationRetryAfterMs = new();
    private int _diagnosticRelocationCursor;

    public long DiagnosticTotalMined => _mining.TotalMined;
    public long DiagnosticCurrency => _mining.Currency;

    /// <summary>
    /// Debug-only stress helper. The stress_1000 session is non-persistent, so applying every authored
    /// skill at max rank here cannot alter the player's progression save. This lets the automation ramp
    /// benchmark exercise the genuinely worst-case shovel/drill rates and patterns without requiring
    /// the tester to farm resources first.
    /// </summary>
    public void ApplyDiagnosticMaximumSkills()
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string id, SkillNodeDefinition node) in _skills.Catalog.Nodes)
        {
            ranks[id] = node.MaxRank;
        }
        _skills.RestoreRanks(ranks);
    }

    /// <summary>
    /// Places stress-test miners directly onto deterministic exposed surface cells. Normal placement
    /// validity still applies (including shovel-compatible terrain), but currency/UI purchase flow is
    /// intentionally bypassed. The caller should apply diagnostic maximum skills first so unlocks and
    /// upgraded behavior match a late-game worst case.
    /// </summary>
    public int SpawnDiagnosticSurfaceMiners(string definitionId, int requestedCount, ref uint randomState)
    {
        if (requestedCount <= 0) return 0;

        int placed = 0;
        int max = _world.MaxCoordinate;
        int attemptBudget = Math.Max(512, requestedCount * 384);
        for (int attempt = 0; attempt < attemptBudget && placed < requestedCount; attempt++)
        {
            int face = NextDiagnosticInt(ref randomState, 6);
            int tangentA = NextDiagnosticInt(ref randomState, max * 2 + 1) - max;
            int tangentB = NextDiagnosticInt(ref randomState, max * 2 + 1) - max;
            Vector3I normal = face switch
            {
                0 => Vector3I.Right,
                1 => Vector3I.Left,
                2 => Vector3I.Up,
                3 => Vector3I.Down,
                4 => Vector3I.Back,
                _ => Vector3I.Forward,
            };

            if (!_world.Source.TrySampleOutermostSurfaceVoxel(
                    normal,
                    tangentA,
                    tangentB,
                    out Vector3I voxel,
                    out _))
            {
                continue;
            }

            // The raw column helper is a fast locator. Validate the actual structural/runtime sample so
            // stress placement sees the same inset-water/shoreline result as gameplay and rendering.
            BlockSample sample = _world.SampleVoxel(voxel);
            if (!sample.Present)
            {
                // Raw water may have been carved one cell inward by the generation-v3 structural pass.
                for (int inward = 1; inward <= 4 && !sample.Present; inward++)
                {
                    Vector3I candidate = voxel - normal * inward;
                    sample = _world.SampleVoxel(candidate);
                    if (sample.Present) voxel = candidate;
                }
            }

            if (!sample.Present
                || _world.State.IsMined(voxel)
                || !_diagnosticSpawnVoxels.Add(voxel))
            {
                continue;
            }

            MinerInstance? miner = PlaceMiner(definitionId, voxel);
            if (miner is null)
            {
                _diagnosticSpawnVoxels.Remove(voxel);
                continue;
            }

            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Stress-suite-only recovery policy. A stopped machine is moved to the nearest compatible exposed
    /// block around its current stop position. Search order is increasing Manhattan distance in the
    /// local cube-face coordinate system, so the first accepted block is the nearest candidate under
    /// that metric rather than a random teleport.
    ///
    /// Shovels deliberately recover onto the authored dirt-backed surface-edge material, not merely any
    /// generic shovel-compatible tile. This keeps the stress harness from bouncing a stuck shovel among
    /// sand patches and matches the requested "put it back on dirt" recovery behavior.
    ///
    /// Drills require the anchor block itself to be mineable by the fully-upgraded drill. If the local
    /// area contains no valid anchor, the same nearest search is repeated around the active manual-mining
    /// front supplied by the F11 suite.
    ///
    /// Candidate validation is allocation-free and uses a per-pass O(1) occupied-anchor set. The previous
    /// diagnostic path called CanPlaceMiner for every candidate, which rescanned all miners and repeated
    /// voxel/exposure sampling; with 120 machines that made relocation itself the benchmark's largest CPU
    /// spike. Normal gameplay placement semantics are unchanged.
    /// </summary>
    public DiagnosticRelocationBatch RelocateStoppedDiagnosticMiners(
        int relocationBudget,
        int localSearchRadius,
        Vector3I? activeMiningFront)
    {
        if (relocationBudget <= 0 || _miners.Count == 0)
        {
            return default;
        }

        ulong started = Time.GetTicksUsec();
        ulong nowMs = Time.GetTicksMsec();
        int minerCount = _miners.Count;
        int visited = 0;
        int attempted = 0;
        int relocated = 0;
        int localRelocations = 0;
        int fallbackRelocations = 0;
        int failed = 0;
        int cooldownSkipped = 0;
        long candidateChecks = 0;
        int maximumRadius = 0;
        int radius = Math.Clamp(localSearchRadius, 2, 12);
        int fallbackRadius = Math.Clamp(radius + 2, 4, 14);

        BuildDiagnosticOccupiedAnchorSet();

        while (visited < minerCount && attempted < relocationBudget)
        {
            if (_diagnosticRelocationCursor >= minerCount) _diagnosticRelocationCursor = 0;
            MinerInstance miner = _miners[_diagnosticRelocationCursor++];
            visited++;
            if (!NeedsAttention(miner)) continue;

            if (_diagnosticRelocationRetryAfterMs.TryGetValue(miner.InstanceId, out ulong retryAt)
                && nowMs < retryAt)
            {
                cooldownSkipped++;
                continue;
            }

            attempted++;
            Vector3I localAnchor = DiagnosticRelocationOrigin(miner);
            bool found = TryFindNearestDiagnosticCompatibleAnchor(
                miner,
                localAnchor,
                radius,
                out Vector3I destination,
                out int foundRadius,
                ref candidateChecks);

            bool usedFallback = false;
            if (!found
                && activeMiningFront is Vector3I fallback
                && fallback != localAnchor)
            {
                usedFallback = TryFindNearestDiagnosticCompatibleAnchor(
                    miner,
                    fallback,
                    fallbackRadius,
                    out destination,
                    out foundRadius,
                    ref candidateChecks);
                found = usedFallback;
            }

            maximumRadius = Math.Max(maximumRadius, foundRadius);
            if (!found)
            {
                failed++;
                _diagnosticRelocationRetryAfterMs[miner.InstanceId] = nowMs + DiagnosticRelocationFailureCooldownMs;
                continue;
            }

            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            Vector3I oldOrigin = miner.Origin;
            Vector3I oldAnchor = MinerAnchorVoxel(miner, definition);
            if (!TryMoveStoppedMiner(miner, destination))
            {
                failed++;
                _diagnosticRelocationRetryAfterMs[miner.InstanceId] = nowMs + DiagnosticRelocationFailureCooldownMs;
                continue;
            }

            _diagnosticRelocationRetryAfterMs.Remove(miner.InstanceId);
            _diagnosticOccupiedAnchors.Remove(oldOrigin);
            _diagnosticOccupiedAnchors.Remove(oldAnchor);
            _diagnosticOccupiedAnchors.Add(miner.Origin);
            _diagnosticOccupiedAnchors.Add(MinerAnchorVoxel(miner, definition));

            relocated++;
            if (usedFallback) fallbackRelocations++;
            else localRelocations++;
        }

        double elapsedMs = (Time.GetTicksUsec() - started) / 1000.0;
        return new DiagnosticRelocationBatch(
            attempted,
            relocated,
            localRelocations,
            fallbackRelocations,
            failed,
            cooldownSkipped,
            candidateChecks,
            maximumRadius,
            elapsedMs);
    }

    public void ResetDiagnosticSpawnReservations()
    {
        _diagnosticSpawnVoxels.Clear();
        _diagnosticOccupiedAnchors.Clear();
        _diagnosticRelocationRetryAfterMs.Clear();
        _diagnosticRelocationCursor = 0;
    }

    private void BuildDiagnosticOccupiedAnchorSet()
    {
        _diagnosticOccupiedAnchors.Clear();
        foreach (MinerInstance miner in _miners)
        {
            MinerDefinition definition = _catalog.Get(miner.DefinitionId);
            _diagnosticOccupiedAnchors.Add(miner.Origin);
            _diagnosticOccupiedAnchors.Add(MinerAnchorVoxel(miner, definition));
        }
    }

    private Vector3I DiagnosticRelocationOrigin(MinerInstance miner)
        => miner.StopReason is MinerStopReason.BlockedMaterial
            or MinerStopReason.BlockedFeature
            or MinerStopReason.BlockedTerrain
            ? miner.BlockedVoxel
            : miner.BlocksMined > 0 ? miner.LastMinedVoxel : miner.Origin;

    private bool TryFindNearestDiagnosticCompatibleAnchor(
        MinerInstance miner,
        Vector3I origin,
        int maxRadius,
        out Vector3I destination,
        out int foundRadius,
        ref long candidateChecks)
    {
        destination = default;
        foundRadius = 0;
        Vector3I outward = _world.Source.GetOutwardNormal(origin);
        if (outward == Vector3I.Zero) return false;

        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);

        // Enumerate |a| + |b| + inwardDepth == radius. This visits candidates once in deterministic
        // nearest-first order instead of rescanning an expanding cube at every radius.
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int inwardDepth = 0; inwardDepth <= radius; inwardDepth++)
            {
                int tangentDistance = radius - inwardDepth;
                for (int a = -tangentDistance; a <= tangentDistance; a++)
                {
                    int absB = tangentDistance - Math.Abs(a);
                    Vector3I candidate = origin
                        + tangentA * a
                        + tangentB * absB
                        - outward * inwardDepth;
                    if (IsDiagnosticCompatibleAnchor(miner, candidate, ref candidateChecks))
                    {
                        destination = candidate;
                        foundRadius = radius;
                        return true;
                    }

                    if (absB == 0) continue;
                    candidate = origin
                        + tangentA * a
                        - tangentB * absB
                        - outward * inwardDepth;
                    if (IsDiagnosticCompatibleAnchor(miner, candidate, ref candidateChecks))
                    {
                        destination = candidate;
                        foundRadius = radius;
                        return true;
                    }
                }
            }
        }

        foundRadius = maxRadius;
        return false;
    }

    private bool IsDiagnosticCompatibleAnchor(
        MinerInstance miner,
        Vector3I candidate,
        ref long candidateChecks)
    {
        candidateChecks++;
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        Vector3I ownAnchor = MinerAnchorVoxel(miner, definition);
        if (_diagnosticOccupiedAnchors.Contains(candidate)
            && candidate != miner.Origin
            && candidate != ownAnchor)
        {
            return false;
        }

        BlockSample sample = _world.SampleVoxel(candidate);
        if (!sample.Present || !_world.IsExposed(candidate, sample)) return false;

        if (IsPrimaryDrill(definition))
        {
            return CanPrimaryDrillMine(sample);
        }

        if (IsShovel(definition))
        {
            // Stress relocation intentionally prefers/forces the dirt-backed authored shovel surface.
            // Normal player placement still accepts the full production shovel material vocabulary.
            if (sample.BlockId != _world.Profile.SurfaceEdgeBlock) return false;
            Vector3I outward = _world.Source.GetOutwardNormal(candidate);
            return IsShovelMaterial(sample) && !HasBlockingShovelSurfaceFeature(candidate, outward);
        }

        if (IsAxe(definition)) return IsTreeAnchor(candidate);
        if (!MatchesAllowedTags(definition, _mining.GetBlockDefinition(sample.BlockId))) return false;
        return _patterns.Contains(EffectivePatternId(definition));
    }

    private static int NextDiagnosticInt(ref uint state, int exclusiveMax)
    {
        if (exclusiveMax <= 1) return 0;
        state = unchecked(state * 1664525u + 1013904223u);
        return (int)(state % (uint)exclusiveMax);
    }
}
