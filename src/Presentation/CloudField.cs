using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Presentation;

public sealed partial class CloudField : Node3D
{
    private readonly RandomNumberGenerator _rng = new();

    public void Build(int seed)
    {
        _rng.Seed = (ulong)(uint)seed;

        var transforms = new List<Transform3D>();
        const int cloudGroups = 24;

        for (int cloud = 0; cloud < cloudGroups; cloud++)
        {
            Vector3 direction = new Vector3(
                _rng.RandfRange(-1.0f, 1.0f),
                _rng.RandfRange(-0.75f, 0.75f),
                _rng.RandfRange(-1.0f, 1.0f)).Normalized();

            float radius = _rng.RandfRange(12.0f, 28.0f);
            Vector3 center = direction * radius;
            int pieces = _rng.RandiRange(2, 5);

            for (int piece = 0; piece < pieces; piece++)
            {
                Vector3 offset = new(
                    piece * 0.65f - pieces * 0.3f,
                    _rng.RandfRange(-0.25f, 0.25f),
                    _rng.RandfRange(-0.35f, 0.35f));

                Vector3 scale = new(
                    _rng.RandfRange(0.8f, 1.8f),
                    _rng.RandfRange(0.32f, 0.62f),
                    _rng.RandfRange(0.7f, 1.4f));

                transforms.Add(new Transform3D(Basis.Identity.Scaled(scale), center + offset));
            }
        }

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.88f, 0.93f, 1.0f),
            Roughness = 1.0f,
            EmissionEnabled = true,
            Emission = new Color(0.06f, 0.07f, 0.10f),
        };

        var mesh = new BoxMesh
        {
            Size = Vector3.One,
            Material = material,
        };

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count,
        };

        for (int i = 0; i < transforms.Count; i++)
        {
            multimesh.SetInstanceTransform(i, transforms[i]);
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "VoxelClouds",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
