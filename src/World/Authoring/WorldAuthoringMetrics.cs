using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Authoring;

public sealed class WorldAuthoringMetrics
{
    public long PresentBlocks { get; init; }
    public long MineableBlocks { get; init; }
    public long ExposedBlocks { get; init; }
    public long ExposedWaterBlocks { get; init; }
    public long ExposedSoftTerrainBlocks { get; init; }
    public long ExposedStoneBlocks { get; init; }
    public long TreeCount { get; init; }
    public long GemCount { get; init; }
    public IReadOnlyDictionary<string, long> MaterialCounts { get; init; }
        = new Dictionary<string, long>(StringComparer.Ordinal);

    public double WaterCoverage => Ratio(ExposedWaterBlocks, ExposedBlocks);
    public double SoftTerrainCoverage => Ratio(ExposedSoftTerrainBlocks, ExposedBlocks);
    public double ExposedStoneCoverage => Ratio(ExposedStoneBlocks, ExposedBlocks);

    private static double Ratio(long value, long total)
        => total <= 0 ? 0.0 : value / (double)total;
}

/// <summary>
/// CPU-only analyzer used by the authoring tool and candidate browser. It uses the same
/// VirtualWorld/ProceduralWorldSource path as gameplay and never instantiates render nodes.
/// </summary>
public static class WorldAuthoringAnalyzer
{
    public const int MaximumExactAuthoringCoordinate = 64;

    public static WorldAuthoringMetrics Analyze(WorldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.MaxCoordinate > MaximumExactAuthoringCoordinate)
        {
            throw new InvalidOperationException(
                $"Authoring exact scan for '{profile.Id}' would use bound {profile.MaxCoordinate}; " +
                $"the interactive tool is capped at {MaximumExactAuthoringCoordinate}. Use aggregate diagnostics for larger worlds.");
        }

        var world = new VirtualWorld(profile);
        int max = world.MaxCoordinate;
        long present = 0L;
        long mineable = 0L;
        long exposed = 0L;
        long exposedWater = 0L;
        long exposedSoft = 0L;
        long exposedStone = 0L;
        long trees = 0L;
        long gems = 0L;
        var materials = new Dictionary<string, long>(StringComparer.Ordinal);

        for (int z = -max; z <= max; z++)
        for (int y = -max; y <= max; y++)
        for (int x = -max; x <= max; x++)
        {
            Vector3I voxel = new(x, y, z);
            BlockSample sample = world.SampleVoxel(voxel);
            if (!sample.Present) continue;

            present++;
            if (sample.Mineable) mineable++;
            materials[sample.BlockId] = checked(materials.GetValueOrDefault(sample.BlockId) + 1L);
            if (sample.BlockId.StartsWith("gem_", StringComparison.Ordinal)) gems++;

            if (!world.IsExposed(voxel)) continue;
            exposed++;
            if (IsWater(profile, sample.BlockId)) exposedWater++;
            if (IsSoftTerrain(profile, sample.BlockId)) exposedSoft++;
            if (IsStone(profile, sample.BlockId)) exposedStone++;
            if (world.Source.TrySampleTree(voxel, out _)) trees++;
        }

        return new WorldAuthoringMetrics
        {
            PresentBlocks = present,
            MineableBlocks = mineable,
            ExposedBlocks = exposed,
            ExposedWaterBlocks = exposedWater,
            ExposedSoftTerrainBlocks = exposedSoft,
            ExposedStoneBlocks = exposedStone,
            TreeCount = trees,
            GemCount = gems,
            MaterialCounts = materials,
        };
    }

    public static double ScoreVerdantCandidate(WorldAuthoringMetrics metrics)
    {
        // Candidate browsing uses broad, explainable preferences rather than pretending a numeric
        // score can replace visual review. The score only pushes useful mixed-terrain seeds upward.
        double water = Bell(metrics.WaterCoverage, target: 0.14, tolerance: 0.14);
        double soft = Bell(metrics.SoftTerrainCoverage, target: 0.56, tolerance: 0.42);
        double stone = Bell(metrics.ExposedStoneCoverage, target: 0.18, tolerance: 0.22);
        double trees = Math.Clamp(metrics.TreeCount / 24.0, 0.0, 1.0);
        return water * 0.32 + soft * 0.28 + stone * 0.20 + trees * 0.20;
    }

    private static bool IsWater(WorldProfile profile, string blockId)
        => blockId == profile.WaterBlock
            || blockId == profile.ShallowWaterBlock
            || blockId == profile.DeepWaterBlock;

    private static bool IsSoftTerrain(WorldProfile profile, string blockId)
        => blockId == profile.SurfaceBlock
            || blockId == profile.SurfaceEdgeBlock
            || blockId == profile.SoilBlock
            || blockId == profile.SandBlock;

    private static bool IsStone(WorldProfile profile, string blockId)
        => blockId == profile.StoneBlock || blockId == profile.DarkStoneBlock;

    private static double Bell(double value, double target, double tolerance)
    {
        double distance = Math.Abs(value - target) / Math.Max(0.0001, tolerance);
        return Math.Clamp(1.0 - distance, 0.0, 1.0);
    }
}
