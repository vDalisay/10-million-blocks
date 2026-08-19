using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;
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

        ValidateReferenceEcology(reference, source);

        var state = new WorldStateStore(reference.ChunkSize);
        Vector3I negative = new(-1, -9, 8);
        Assert(state.MarkMined(negative), "first sparse mined-state write");
        Assert(state.IsMined(negative), "sparse mined-state lookup");
        Assert(!state.MarkMined(negative), "duplicate mined-state write rejected");

        var snapshot = state.CreateSnapshot();
        var restored = new WorldStateStore(reference.ChunkSize);
        restored.RestoreSnapshot(snapshot);
        Assert(restored.IsMined(negative), "sparse mined-state snapshot round trip");
        Assert(restored.MinedVoxelCount == 1, "sparse mined-state count round trip");

        WorldProfile stress = catalog.Get("stress_1000");
        var stressSource = new ProceduralWorldSource(stress);
        _ = stressSource.SampleVoxel(new Vector3I(497, 123, -441));
        _ = stressSource.SampleVoxel(new Vector3I(-499, -300, 12));
        _ = stressSource.SampleVoxel(Vector3I.Zero);

        GD.Print("World self-tests passed, including hydrology/forest guards and 1000-address-space arbitrary-coordinate sampling without world allocation.");
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
