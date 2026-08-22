using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    private readonly HashSet<Vector3I> _diagnosticSpawnVoxels = new();

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
                    out BlockSample sample)
                || !sample.Present
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

    public void ResetDiagnosticSpawnReservations()
        => _diagnosticSpawnVoxels.Clear();

    private static int NextDiagnosticInt(ref uint state, int exclusiveMax)
    {
        if (exclusiveMax <= 1) return 0;
        state = unchecked(state * 1664525u + 1013904223u);
        return (int)(state % (uint)exclusiveMax);
    }
}
