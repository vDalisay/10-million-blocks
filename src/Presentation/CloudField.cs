using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class CloudField : Node3D
{
    private const int StarCount = 180;
    private const int CloudCount = 16;

    private readonly List<CloudOrbiter> _orbiters = new();

    // Clouds must never clip the cube. The cube's own distance metric is the Chebyshev norm, so a
    // single standoff radius in that metric keeps them clear of faces, edges and corners alike.
    private float _minStandoff = 30.0f;

    public void SetWorldExtent(float halfExtent)
    {
        _minStandoff = halfExtent + 4.0f;
    }

    private sealed class CloudOrbiter
    {
        public Node3D Pivot { get; init; } = null!;
        public Node3D Carrier { get; init; } = null!;
        public Vector3 LocalOffset { get; init; }
        public float AngularSpeed { get; init; }
    }

    public override void _Ready()
    {
        BuildOrbitingClouds();
        AddChild(BuildStars());
        OrientCloudsTowardWorld();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (CloudOrbiter orbiter in _orbiters)
        {
            Vector3 rotation = orbiter.Pivot.Rotation;
            rotation.Y = Mathf.Wrap(rotation.Y + orbiter.AngularSpeed * dt, -Mathf.Pi, Mathf.Pi);
            orbiter.Pivot.Rotation = rotation;
        }

        OrientCloudsTowardWorld();
    }

    private void BuildOrbitingClouds()
    {
        var random = new Random(73021);

        for (int cloudIndex = 0; cloudIndex < CloudCount; cloudIndex++)
        {
            float normalized = cloudIndex / (float)CloudCount;
            float radius = 20.0f + (float)random.NextDouble() * 14.0f;
            float height = -7.0f + (float)random.NextDouble() * 14.0f;
            float inclination = Mathf.DegToRad(-18.0f + (float)random.NextDouble() * 36.0f);
            float bank = Mathf.DegToRad(-7.0f + (float)random.NextDouble() * 14.0f);
            float phase = normalized * Mathf.Tau + ((float)random.NextDouble() - 0.5f) * 0.48f;

            var pivot = new Node3D
            {
                Name = $"CloudOrbit_{cloudIndex:00}",
                Rotation = new Vector3(inclination, phase, bank),
            };
            AddChild(pivot);

            Vector3 localOffset = new(radius, height, 0.0f);
            var carrier = new Node3D
            {
                Name = "Carrier",
                TopLevel = true,
            };
            pivot.AddChild(carrier);

            int pieces = random.Next(5, 10);
            carrier.AddChild(BuildClump(random, pieces));

            float direction = cloudIndex % 5 == 0 ? -1.0f : 1.0f;
            float angularSpeed = direction * (0.033f + (34.0f - radius) * 0.0012f);
            _orbiters.Add(new CloudOrbiter
            {
                Pivot = pivot,
                Carrier = carrier,
                LocalOffset = localOffset,
                AngularSpeed = angularSpeed,
            });
        }
    }

    private void OrientCloudsTowardWorld()
    {
        foreach (CloudOrbiter orbiter in _orbiters)
        {
            Vector3 orbitalPosition = orbiter.Pivot.ToGlobal(orbiter.LocalOffset);

            float chebyshev = MathF.Max(
                MathF.Abs(orbitalPosition.X),
                MathF.Max(MathF.Abs(orbitalPosition.Y), MathF.Abs(orbitalPosition.Z)));
            if (chebyshev > 0.0001f && chebyshev < _minStandoff)
            {
                orbitalPosition *= _minStandoff / chebyshev;
            }

            orbiter.Carrier.GlobalPosition = orbitalPosition;

            Vector3 outward = orbitalPosition.Normalized();
            if (outward.LengthSquared() < 0.0001f)
            {
                continue;
            }

            Vector3 reference = MathF.Abs(outward.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
            Vector3 xAxis = reference.Cross(outward).Normalized();
            Vector3 zAxis = xAxis.Cross(outward).Normalized();
            orbiter.Carrier.GlobalBasis = new Basis(xAxis, outward, zAxis).Orthonormalized();
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

        for (int i = 0; i < pieces; i++)
        {
            float lane = i - (pieces - 1) * 0.5f;
            Vector3 local = new(
                lane * (0.70f + (float)random.NextDouble() * 0.26f),
                ((float)random.NextDouble() - 0.5f) * 0.34f,
                ((float)random.NextDouble() - 0.5f) * 1.55f);

            if (i > 1 && i < pieces - 1 && random.NextDouble() > 0.62)
            {
                local.Y += 0.62f;
            }

            float size = 1.18f + (float)random.NextDouble() * 0.76f;
            Vector3 scale = new(
                size * (1.12f + (float)random.NextDouble() * 0.32f),
                size * (0.32f + (float)random.NextDouble() * 0.14f),
                size * (0.72f + (float)random.NextDouble() * 0.26f));

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
