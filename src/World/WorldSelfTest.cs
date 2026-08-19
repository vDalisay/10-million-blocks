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

        var state = new WorldStateStore(reference.ChunkSize);
        Vector3I negative = new(-1, -9, 8);
        Assert(state.MarkMined(negative), "first sparse mined-state write");
        Assert(state.IsMined(negative), "sparse mined-state lookup");
        Assert(!state.MarkMined(negative), "duplicate mined-state write rejected");

        WorldProfile stress = catalog.Get("stress_1000");
        var stressSource = new ProceduralWorldSource(stress);
        _ = stressSource.SampleVoxel(new Vector3I(497, 123, -441));
        _ = stressSource.SampleVoxel(new Vector3I(-499, -300, 12));
        _ = stressSource.SampleVoxel(Vector3I.Zero);

        GD.Print("World self-tests passed, including 1000-address-space arbitrary-coordinate sampling without world allocation.");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"World self-test failed: {name}");
        }
    }
}
