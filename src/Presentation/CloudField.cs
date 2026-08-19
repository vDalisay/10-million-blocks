using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class CloudField : Node3D
{
    private const int StarCount = 180;
    private readonly List<(Node3D Layer, float Speed, float BobPhase)> _cloudLayers = new();
    private double _elapsed;

    public override void _Ready()
    {
        AddCloudLayer("CloudLayerNear", 73021, 14.0f, 21.0f, 26, 0.030f, -0.08f);
        AddCloudLayer("CloudLayerMid", 73031, 20.0f, 29.0f, 28, -0.020f, 0.04f);
        AddCloudLayer("CloudLayerFar", 73051, 28.0f, 38.0f, 22, 0.012f, -0.02f);
        AddChild(BuildStars());
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        foreach ((Node3D layer, float speed, float phase) in _cloudLayers)
        {
            Vector3 rotation = layer.Rotation;
            rotation.Y += speed * (float)delta;
            rotation.Z = MathF.Sin((float)_elapsed * 0.11f + phase) * 0.025f;
            layer.Rotation = rotation;

            Vector3 position = layer.Position;
            position.Y = MathF.Sin((float)_elapsed * 0.18f + phase) * 0.45f;
            layer.Position = position;
        }
    }

    private void AddCloudLayer(
        string name,
        int seed,
        float minRadius,
        float maxRadius,
        int cubeCount,
        float angularSpeed,
        float tilt)
    {
        var layer = new Node3D
        {
            Name = name,
            Rotation = new Vector3(tilt, 0.0f, 0.0f),
        };
        layer.AddChild(BuildCloudBatch(seed, minRadius, maxRadius, cubeCount));
        AddChild(layer);
        _cloudLayers.Add((layer, angularSpeed, seed * 0.001f));
    }

    private static MultiMeshInstance3D BuildCloudBatch(int seed, float minRadius, float maxRadius, int cubeCount)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.96f, 1.0f, 1.0f),
            Roughness = 0.88f,
        };

        var mesh = new BoxMesh
        {
            Size = new Vector3(1.9f, 0.72f, 1.35f),
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = cubeCount,
            VisibleInstanceCount = cubeCount,
        };

        var random = new Random(seed);
        int clusterCount = Math.Max(1, cubeCount / 4);
        for (int i = 0; i < cubeCount; i++)
        {
            int cluster = i % clusterCount;
            float baseAngle = (cluster / (float)clusterCount) * Mathf.Tau;
            baseAngle += ((float)random.NextDouble() - 0.5f) * 0.24f;
            float radius = minRadius + (float)random.NextDouble() * (maxRadius - minRadius);
            float height = ((float)random.NextDouble() - 0.5f) * 19.0f;

            Vector3 center = new(
                MathF.Cos(baseAngle) * radius,
                height,
                MathF.Sin(baseAngle) * radius);

            float localIndex = i / (float)clusterCount;
            Vector3 local = new(
                (localIndex - 1.45f) * 1.45f,
                ((float)random.NextDouble() - 0.5f) * 0.55f,
                ((float)random.NextDouble() - 0.5f) * 1.1f);

            float scale = 0.72f + (float)random.NextDouble() * 0.58f;
            Basis basis = Basis.Identity.Scaled(new Vector3(scale * 1.35f, scale * 0.72f, scale));
            multiMesh.SetInstanceTransform(i, new Transform3D(basis, center + local));
        }

        return new MultiMeshInstance3D
        {
            Name = "MovingBlockClouds",
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
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
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
