using System;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class CloudField : Node3D
{
    private const int CloudCubeCount = 56;
    private const int StarCount = 180;

    public override void _Ready()
    {
        AddChild(BuildClouds());
        AddChild(BuildStars());
    }

    private static MultiMeshInstance3D BuildClouds()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.96f, 1.0f, 1.0f),
            Roughness = 0.82f,
        };

        var mesh = new BoxMesh
        {
            Size = new Vector3(1.9f, 1.25f, 1.4f),
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = CloudCubeCount,
            VisibleInstanceCount = CloudCubeCount,
        };

        var random = new Random(73021);
        for (int i = 0; i < CloudCubeCount; i++)
        {
            int cluster = i / 7;
            float angle = cluster * 0.82f + 0.25f;
            float radius = 19.0f + (cluster % 3) * 3.8f;
            Vector3 center = new(
                MathF.Cos(angle) * radius,
                ((cluster % 5) - 2) * 5.5f,
                MathF.Sin(angle) * radius);

            Vector3 jitter = new(
                ((float)random.NextDouble() - 0.5f) * 5.5f,
                ((float)random.NextDouble() - 0.5f) * 2.2f,
                ((float)random.NextDouble() - 0.5f) * 4.0f);

            float scale = 0.65f + (float)random.NextDouble() * 0.55f;
            Basis basis = Basis.Identity.Scaled(new Vector3(scale * 1.25f, scale * 0.75f, scale));
            multiMesh.SetInstanceTransform(i, new Transform3D(basis, center + jitter));
        }

        return new MultiMeshInstance3D
        {
            Name = "LayeredBlockClouds",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static MultiMeshInstance3D BuildStars()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.82f, 0.96f, 1.0f),
            Roughness = 1.0f,
        };

        var mesh = new BoxMesh
        {
            Size = new Vector3(0.10f, 0.10f, 0.10f),
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = StarCount,
            VisibleInstanceCount = StarCount,
        };

        var random = new Random(8128);
        for (int i = 0; i < StarCount; i++)
        {
            float yaw = (float)random.NextDouble() * Mathf.Tau;
            float pitch = ((float)random.NextDouble() - 0.5f) * Mathf.Pi;
            float radius = 82.0f + (float)random.NextDouble() * 35.0f;
            Vector3 direction = new(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw));
            multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, direction * radius));
        }

        return new MultiMeshInstance3D
        {
            Name = "SubtleStarField",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
