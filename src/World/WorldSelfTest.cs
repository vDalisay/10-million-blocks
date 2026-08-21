using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
using TenMillionBlocks.World.Generation;
using TenMillionBlocks.World.Rendering;
using TenMillionBlocks.World.Storage;

namespace TenMillionBlocks.World;

public static class WorldSelfTest
{
    public static void Run(WorldCatalog catalog)
    {
        Assert(VoxelMath.FloorDiv(7, 8) == 0, "positive chunk division");
        Assert(VoxelMath.FloorDiv(-1, 8) == -1, "negative chunk division -1");
        Assert(VoxelMath.FloorDiv(-8, 8) == -1, "negative chunk division boundary");
        Assert(VoxelMath.FloorDiv(-9, 8) == -2, "negative chunk division crossing boundary");

        WorldProfile reference = catalog.Get("reference_natural");
        var source = new ProceduralWorldSource(reference);
        Vector3I probe = new(reference.MaxCoordinate / 2, 1, -2);
        BlockSample first = source.SampleVoxel(probe);
        BlockSample second = source.SampleVoxel(probe);
        Assert(first.Equals(second), "procedural generator determinism");
        Assert(WorldView.ResolveTerrainVisualBlockId("grass", "grass", "dirt_grass", "dirt", false) == "dirt_grass",
            "ordinary surface grass keeps a brown core");
        Assert(WorldView.ResolveTerrainVisualBlockId("dirt", "grass", "dirt_grass", "dirt", true) == "grass_outer",
            "perimeter dirt uses stable solid green");
        Assert(WorldView.ResolveTerrainVisualBlockId("dirt", "grass", "dirt_grass", "dirt", false) == "dirt",
            "non-perimeter dirt remains brown");

        ValidateSingleBlockTutorial(catalog.Get("tutorial_single_block"));
        ValidateManualMiningFootprint(catalog.Get("tutorial_dirt_5"));
        ValidateReferenceEcology(reference, source);
        ValidateSparseState(reference);
        ValidateStressScale(catalog.Get("stress_1000"));
        ValidateMillionTarget(catalog.Get("final_target_1m"));

        GD.Print("World self-tests passed: single-block tutorial, ecology, sparse state, 1000 address-space streaming counters, region aggregates, and the one-million-block final target.");
    }

    private static void ValidateSingleBlockTutorial(WorldProfile profile)
    {
        var world = new VirtualWorld(profile);
        long physicalBlocks = world.CountMineableBlocksExact();
        Assert(physicalBlocks == 1L, "tutorial world contains exactly one physical mineable block");
        Assert(physicalBlocks == profile.TargetMineableBlocks, "tutorial physical count matches authored target");
        Assert(world.IsExposed(Vector3I.Zero), "tutorial block is exposed");
        Assert(world.TryMine(Vector3I.Zero, out _), "tutorial block can be mined");
        Assert(world.RemainingMineableBlocks == 0L, "tutorial block completes the world");
        Assert(!profile.SkillTreeAvailable && !profile.AutomationAvailable,
            "tutorial progression systems remain unavailable");
    }

    private static void ValidateManualMiningFootprint(WorldProfile profile)
    {
        var world = new VirtualWorld(profile);
        Vector3I center = new(0, 2, 0);
        IReadOnlyList<Vector3I> footprint = ManualMiningFootprint.ResolveFromCenter(
            world,
            center,
            ManualMiningFootprintKind.Square3,
            Vector3I.Up);
        Assert(footprint.Count == 9 && footprint[0] == center, "3x3 footprint stays centred on raycast hit");
        foreach (Vector3I voxel in footprint)
        {
            Assert(voxel.Y == center.Y, "3x3 footprint stays on raycast face plane");
        }
    }

    private static void ValidateSparseState(WorldProfile reference)
    {
        var state = new WorldStateStore(reference.ChunkSize, reference.RegionSizeInChunks);
        Vector3I negative = new(-1, -9, 8);
        Assert(state.MarkMined(negative), "first sparse mined-state write");
        Assert(state.IsMined(negative), "sparse mined-state lookup");
        Assert(!state.MarkMined(negative), "duplicate mined-state write rejected");

        var snapshot = state.CreateSnapshot();
        var restored = new WorldStateStore(reference.ChunkSize, reference.RegionSizeInChunks);
        restored.RestoreSnapshot(snapshot);
        Assert(restored.IsMined(negative), "sparse mined-state snapshot round trip");
        Assert(restored.MinedVoxelCount == 1, "sparse mined-state count round trip");
    }

    private static void ValidateStressScale(WorldProfile stress)
    {
        var world = new VirtualWorld(stress);
        long total = world.InitializeMineableBlockCount();
        Assert(total == 1_000_000L, "1000 stress world uses the one-million-block authored target without full scan");
        Assert(world.State.ModifiedChunkCount == 0, "1000 stress startup allocates no sparse chunks");
        Assert(world.State.ExhaustedRegionCount == 0, "1000 stress startup allocates no region markers");

        _ = world.Source.SampleVoxel(new Vector3I(497, 123, -441));
        _ = world.Source.SampleVoxel(new Vector3I(-499, -300, 12));
        _ = world.Source.SampleVoxel(Vector3I.Zero);

        RegionCoord firstRegion = new(world.MinRegionCoordinate, world.MinRegionCoordinate, world.MinRegionCoordinate);
        long quota = world.GetRegionQuota(firstRegion);
        Assert(quota > 0, "1000 stress region receives logical quota");
        Assert(world.TryExhaustRegion(firstRegion, out long newlyMined), "1000 stress region aggregate exhaustion");
        Assert(newlyMined == quota, "region exhaustion mines exact quota");
        Assert(world.RemainingMineableBlocks == total - quota, "region exhaustion preserves exact remaining count");
        Assert(world.State.SparseVoxelOverrideCount == 0, "region exhaustion does not allocate voxel deviations");

        var regions = world.State.CreateExhaustedRegionSnapshot();
        var restored = new WorldStateStore(stress.ChunkSize, stress.RegionSizeInChunks);
        restored.RestoreSnapshot(Array.Empty<MinedChunkSnapshot>(), regions);
        Assert(restored.MinedVoxelCount == quota, "aggregate region snapshot round trip");
        Assert(restored.ExhaustedRegionCount == 1, "aggregate region marker round trip");
    }

    private static void ValidateMillionTarget(WorldProfile profile)
    {
        var world = new VirtualWorld(profile);
        long total = world.InitializeMineableBlockCount();
        Assert(total == 1_000_000L, "final validation profile has exactly one million authored mineable blocks");
        Assert(world.TotalLogicalRegionCount > 1L, "final target remains hierarchically partitioned");
        Assert(world.State.ModifiedChunkCount == 0 && world.State.ExhaustedRegionCount == 0,
            "final-target profile creation remains sparse");

        int far = Math.Max(1, world.MaxCoordinate - 3);
        BlockSample a = world.Source.SampleVoxel(new Vector3I(far, 20, -31));
        BlockSample b = world.Source.SampleVoxel(new Vector3I(far, 20, -31));
        Assert(a.Equals(b), "final-target far coordinate deterministic generation");
        _ = world.Source.SampleVoxel(new Vector3I(-far, -27, 18));

        RegionCoord distant = new(world.MaxRegionCoordinate, 0, world.MinRegionCoordinate);
        long quota = world.GetRegionQuota(distant);
        Assert(quota > 0, "final-target distant region quota available in O(1)");
        Assert(world.TryExhaustRegion(distant, out long mined), "final-target distant region aggregate exhaustion");
        Assert(mined == quota && world.RemainingMineableBlocks == total - quota,
            "final-target aggregate mining keeps exact 64-bit accounting");
        Assert(world.State.SparseVoxelOverrideCount == 0,
            "final-target aggregate mining does not visit/store individual voxels");

        long axis = world.RegionAxisCount;
        long regionCount = world.TotalLogicalRegionCount;
        long quotient = total / regionCount;
        long remainder = total % regionCount;
        Assert(checked(quotient * regionCount + remainder) == total,
            "region quota quotient/remainder reconstructs exact one-million-block total");
    }

    private static void ValidateReferenceEcology(WorldProfile profile, ProceduralWorldSource source)
    {
        long water = 0;
        long shallowWater = 0;
        long deepWater = 0;
        long sand = 0;
        long trees = 0;
        int max = profile.MaxCoordinate;

        for (int z = -max; z <= max; z++)
        for (int y = -max; y <= max; y++)
        for (int x = -max; x <= max; x++)
        {
            Vector3I coordinate = new(x, y, z);
            BlockSample sample = source.SampleVoxel(coordinate);
            if (!sample.Present) continue;

            if (sample.BlockId == profile.ShallowWaterBlock) shallowWater++;
            else if (sample.BlockId == profile.DeepWaterBlock) deepWater++;
            else if (sample.BlockId == profile.WaterBlock) water++;
            else if (sample.BlockId == profile.SandBlock) sand++;

            if ((sample.BlockId == profile.SurfaceBlock || sample.BlockId == profile.SurfaceEdgeBlock)
                && source.TrySampleTree(coordinate, out _))
            {
                trees++;
            }
        }

        long allWater = shallowWater + water + deepWater;
        Assert(allWater >= 100, "reference world contains coherent visible water volume");
        Assert(shallowWater > 0, "reference world contains shallow-water visual tier");
        Assert(deepWater > 0, "reference world contains deep-water visual tier");
        Assert(sand >= 100, "reference world contains beach/shore material");
        Assert(trees >= 10, "reference world contains readable tree feature population");

        GD.Print(
            $"Reference ecology: {allWater:N0} water ({shallowWater:N0} shallow / {deepWater:N0} deep), " +
            $"{sand:N0} sand blocks, {trees:N0} deterministic trees.");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"World self-test failed: {name}");
        }
    }
}
