using Godot;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    /// <summary>
    /// Short-lived copy of the mined block used for manual click feedback. The authoritative chunk is
    /// still rebuilt normally; this copy exists only long enough to create the small "pop" scale-up
    /// before disappearing, so repeated clicking feels tactile without adding persistent block nodes.
    /// </summary>
    public void SpawnManualMinePop(Vector3I voxel, string blockId)
    {
        if (!_assets.Definitions.ContainsKey(blockId)) return;

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
}
