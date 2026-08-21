using Godot;
using TenMillionBlocks.Automation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Short-lived copy of the mined block used for manual/replay feedback. The authoritative chunk is
    /// still rebuilt normally; this copy exists only long enough to create the small "pop" scale-up
    /// before disappearing, so mining feels tactile without adding persistent block nodes.
    /// </summary>
    public void SpawnManualMinePop(Vector3I voxel, string blockId)
    {
        Vector3I outward = _world.Source.GetOutwardNormal(voxel);
        Basis basis = ShouldOrientToCubeFace(blockId)
            ? BasisForNormal(outward)
            : Basis.Identity;

        var pop = new MeshInstance3D
        {
            Name = $"MinePop_{voxel.X}_{voxel.Y}_{voxel.Z}",
            Mesh = _assets.GetMesh(blockId),
            MaterialOverride = _assets.GetMaterialOverride(blockId),
            Transform = new Transform3D(basis, VoxelToWorld(voxel)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = Vector3.One * 0.985f,
        };
        AddChild(pop);

        Tween tween = pop.CreateTween();
        tween.SetEase(Tween.EaseType.Out);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(pop, "scale", Vector3.One * 1.12f, 0.075);
        tween.SetEase(Tween.EaseType.In);
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(pop, "scale", Vector3.One * 0.92f, 0.055);
        tween.TweenCallback(Callable.From(pop.QueueFree));
    }

    /// <summary>
    /// Shared mining-only debris used by live mining and replay. This deliberately does not emit any
    /// resource pickup presentation, so replay can recreate the physical mining feedback from the
    /// recorded voxel alone without granting or serializing rewards.
    /// </summary>
    public void SpawnMiningDebris(Vector3I voxel, string blockId, int seed, string name = "MiningDebris")
    {
        Vector3I outwardI = _world.Source.GetOutwardNormal(voxel);
        Vector3 outward = (Vector3)outwardI;
        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = VoxelToWorld(voxel) + outward * spacing * 0.48f;

        var burst = new DrillDebrisBurst { Name = name };
        AddChild(burst);
        burst.Initialize(position, outward, blockId, spacing, seed);
    }
}
