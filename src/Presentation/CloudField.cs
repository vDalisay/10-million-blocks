using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class CloudField : Node3D
{
    private const int StarCount = 180;
    private const int CloudCount = 16;

    private readonly List<(Node3D Pivot, float AngularSpeed)> _orbiters = new();

    public override void _Ready()
    {
        BuildOrbitingClouds();
        AddChild(BuildStars());
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach ((Node3D pivot, float angularSpeed) in _orbiters)
        {
            Vector3 rotation = pivot.Rotation;
            rotation.Y = Mathf.Wrap(rotation.Y + angularSpeed * dt, -Mathf.Pi, Mathf.Pi);
            pivot.Rotation = rotation;
        }
    }

    private void BuildOrbitingClouds()
    {
        var random = new Random(73021);

        for (int cloudIndex = 0; cloudIndex < CloudCount; cloudIndex++)
        {
            float normalized = cloudIndex / (float)CloudCount;
            float radius = 20.0f + (float)random.NextDouble() * 14.0f;
            float height = -9.0f + (float)random.NextDouble() * 18.0f;
            float inclination = Mathf.DegToRad(-16.0f + (float)random.NextDouble() * 32.0f);
            float bank = Mathf.DegToRad(-8.0f + (float)random.NextDouble() * 16.0f);
            float phase = normalized * Mathf.Tau + ((float)random.NextDouble() - 0.5f) * 0.48f;

            var pivot = new Node3D
            {
                Name = $"CloudOrbit_{cloudIndex:00}",
                Rotation = new Vector3(inclination, phase, bank),
            };
            AddChild(pivot);

            var carrier = new Node3D
            {
                Name = "Carrier",
                Position = new Vector3(radius, height, 0.0f),
            };
            pivot.AddChild(carrier);

            int pieces = random.Next(4, 9);
            carrier.AddChild(BuildClump(random, pieces));

            // A complete revolution takes roughly 95-190 seconds. Nearby clouds are a little faster,
            // but every clump keeps its shape instead of independently wiggling in place.
            float direction = cloudIndex % 5 == 0 ? -1.0f : 1.0f;
            float angularSpeed = direction * (0.033f + (34.0f - radius) * 0.0012f);
            _orbiters.Add((pivot, angularSpeed));
        }
    }

    private static MultiMeshInstance3D BuildClump(Random random, int pieces)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.93f, 0.965f, 1.0f, 1.0f),
            Roughness = 0.92f,
        };

        var mesh = new BoxMesh
        {
            Size = Vector3.One,
            Material = material,
        };

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = pieces,
            VisibleInstanceCount = pieces,
        };

        // Build one coherent flattened voxel cloud around local origin. Pieces overlap enough to read
        // as one cloud at a distance, matching the reference instead of a ring of unrelated cubes.
        for (int i = 0; i < pieces; i++)
        {
            float lane = i - (pieces - 1) * 0.5f;
            Vector3 local = new(
                lane * (0.72f + (float)random.NextDouble() * 0.30f),
                ((float)random.NextDouble() - 0.5f) * 0.75f,
                ((float)random.NextDouble() - 0.5f) * 1.65f);

            if (i > 1 && i < pieces - 1 && random.NextDouble() > 0.55)
            {
                local.Y += 0.55f;
            }

            float size = 1.25f + (float)random.NextDouble() * 0.85f;
            Vector3 scale = new(
                size * (1.15f + (float)random.NextDouble() * 0.35f),
                size * (0.38f + (float)random.NextDouble() * 0.16f),
                size * (0.72f + (float)random.NextDouble() * 0.28f));

            multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(scale), local));
        }

        return new MultiMeshInstance3D
        {
            Name = "VoxelCloudClump",
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
