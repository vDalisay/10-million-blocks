using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Presentation;

public partial class CloudField : Node3D
{
    private const int StarCount = 180;
    private const int CloudCount = 16;

    // Screen-space cloud visibility zones, expressed as distance from screen centre divided by half
    // the viewport width. These correspond to the user's red / orange / green guide bands.
    private const float RedZoneHalfWidth = 0.30f;
    private const float OrangeZoneCentre = 0.46f;
    private const float GreenZoneStart = 0.58f;
    private const float RedZoneOpacity = 0.40f;
    private const float OrangeZoneOpacity = 0.80f;
    private const float GreenZoneOpacity = 1.00f;
    private const float OpacityResponse = 10.0f;

    private readonly List<CloudOrbiter> _orbiters = new();
    private float _minStandoff = 30.0f;
    private MultiMeshInstance3D? _stars;

    public void SetWorldExtent(float halfExtent)
    {
        _minStandoff = halfExtent + 4.0f;

        // The star field used to have a fixed 82..117 radius. Once the real-block one-million world
        // became physically much larger, some stars could end up inside the cube. Re-seed the same
        // deterministic field outside the active world's extent instead.
        if (_stars?.Multimesh is MultiMesh starMesh)
        {
            PopulateStars(starMesh, StarMinimumRadius(halfExtent));
        }
    }

    private sealed class CloudOrbiter
    {
        public Node3D Pivot { get; init; } = null!;
        public Node3D Carrier { get; init; } = null!;
        public StandardMaterial3D Material { get; init; } = null!;
        public Vector3 LocalOffset { get; init; }
        public float AngularSpeed { get; init; }
        public float StandoffOffset { get; init; }
        public float Opacity { get; set; } = 1.0f;
    }

    public override void _Ready()
    {
        BuildOrbitingClouds();
        _stars = BuildStars(StarMinimumRadius(MathF.Max(0.0f, _minStandoff - 4.0f)));
        AddChild(_stars);
        OrientCloudsTowardWorld();
        UpdateCloudOpacity(0.0f);
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
        UpdateCloudOpacity(dt);
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
            MultiMeshInstance3D clump = BuildClump(random, pieces, out StandardMaterial3D material);
            carrier.AddChild(clump);

            float direction = cloudIndex % 5 == 0 ? -1.0f : 1.0f;
            float angularSpeed = direction * (0.026f + (34.0f - radius) * 0.0009f);
            _orbiters.Add(new CloudOrbiter
            {
                Pivot = pivot,
                Carrier = carrier,
                Material = material,
                LocalOffset = localOffset,
                AngularSpeed = angularSpeed,
                // Preserve multiple orbital layers instead of clamping every cloud to exactly the
                // same giant-world shell radius.
                StandoffOffset = (float)random.NextDouble() * 16.0f,
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
            float desiredStandoff = _minStandoff + orbiter.StandoffOffset;
            if (chebyshev > 0.0001f && chebyshev < desiredStandoff)
            {
                orbitalPosition *= desiredStandoff / chebyshev;
            }

            orbiter.Carrier.GlobalPosition = orbitalPosition;

            // Local +Y points away from the cube, therefore the clump's -Y/underside faces the world.
            // This keeps the layered underside readable instead of presenting the cloud top inward.
            Vector3 outward = orbitalPosition.Normalized();
            if (outward.LengthSquared() < 0.0001f) continue;

            Vector3 reference = MathF.Abs(outward.Dot(Vector3.Up)) > 0.92f ? Vector3.Right : Vector3.Up;
            Vector3 xAxis = reference.Cross(outward).Normalized();
            Vector3 zAxis = xAxis.Cross(outward).Normalized();
            orbiter.Carrier.GlobalBasis = new Basis(xAxis, outward, zAxis).Orthonormalized();
        }
    }

    /// <summary>
    /// Clouds are decorative foreground objects, so they should not obscure the thing the player is
    /// actively looking at. Opacity is evaluated in camera/screen space rather than world space:
    /// centre/red is ~40%, the surrounding orange band tends toward ~80%, and the outer green area
    /// returns to 100%. Smooth interpolation and a short response time avoid visible alpha steps.
    /// </summary>
    private void UpdateCloudOpacity(float delta)
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        if (camera is null || viewportSize.X <= 1.0f)
        {
            SetAllCloudOpacity(1.0f);
            return;
        }

        Transform3D inverseCamera = camera.GlobalTransform.AffineInverse();
        float blend = delta <= 0.0f
            ? 1.0f
            : 1.0f - MathF.Exp(-OpacityResponse * delta);

        foreach (CloudOrbiter orbiter in _orbiters)
        {
            Vector3 localToCamera = inverseCamera * orbiter.Carrier.GlobalPosition;
            float targetOpacity = 1.0f;

            // Godot cameras look down local -Z. Clouds behind the camera should stay fully opaque so
            // they do not pre-fade before entering the visible screen on the next part of their orbit.
            if (localToCamera.Z < -0.01f)
            {
                Vector2 screen = camera.UnprojectPosition(orbiter.Carrier.GlobalPosition);
                targetOpacity = OpacityForScreenX(screen.X, viewportSize.X);
            }

            orbiter.Opacity = Mathf.Lerp(orbiter.Opacity, targetOpacity, blend);
            SetMaterialOpacity(orbiter.Material, orbiter.Opacity);
        }
    }

    private void SetAllCloudOpacity(float opacity)
    {
        foreach (CloudOrbiter orbiter in _orbiters)
        {
            orbiter.Opacity = opacity;
            SetMaterialOpacity(orbiter.Material, opacity);
        }
    }

    private static float OpacityForScreenX(float screenX, float viewportWidth)
    {
        float halfWidth = MathF.Max(1.0f, viewportWidth * 0.5f);
        float normalizedFromCentre = MathF.Abs(screenX - halfWidth) / halfWidth;

        if (normalizedFromCentre <= RedZoneHalfWidth)
        {
            return RedZoneOpacity;
        }

        if (normalizedFromCentre < OrangeZoneCentre)
        {
            float t = Smooth01((normalizedFromCentre - RedZoneHalfWidth)
                / (OrangeZoneCentre - RedZoneHalfWidth));
            return Mathf.Lerp(RedZoneOpacity, OrangeZoneOpacity, t);
        }

        if (normalizedFromCentre < GreenZoneStart)
        {
            float t = Smooth01((normalizedFromCentre - OrangeZoneCentre)
                / (GreenZoneStart - OrangeZoneCentre));
            return Mathf.Lerp(OrangeZoneOpacity, GreenZoneOpacity, t);
        }

        return GreenZoneOpacity;
    }

    private static void SetMaterialOpacity(StandardMaterial3D material, float opacity)
    {
        Color color = material.AlbedoColor;
        material.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(opacity, 0.0f, 1.0f));
    }

    private static float Smooth01(float value)
    {
        float t = Mathf.Clamp(value, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static MultiMeshInstance3D BuildClump(
        Random random,
        int pieces,
        out StandardMaterial3D material)
    {
        material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.93f, 0.965f, 1.0f, 1.0f),
            Roughness = 0.92f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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

        // Build one horizontally connected voxel clump rather than independent white rectangles.
        // Each piece overlaps its neighbour slightly; occasional raised cells create the stepped cloud
        // tops visible in the reference without turning the whole clump into a rigid slab.
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

    private static MultiMeshInstance3D BuildStars(float minimumRadius)
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
        PopulateStars(multiMesh, minimumRadius);

        return new MultiMeshInstance3D
        {
            Name = "SubtleStarField",
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static void PopulateStars(MultiMesh multiMesh, float minimumRadius)
    {
        var random = new Random(8128);
        for (int i = 0; i < StarCount; i++)
        {
            float yaw = (float)random.NextDouble() * Mathf.Tau;
            float pitch = ((float)random.NextDouble() - 0.5f) * Mathf.Pi;
            float radius = minimumRadius + (float)random.NextDouble() * MathF.Max(35.0f, minimumRadius * 0.35f);
            Vector3 direction = new(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw));
            multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, direction * radius));
        }
    }

    private static float StarMinimumRadius(float halfExtent)
        => MathF.Max(82.0f, halfExtent * 1.55f + 18.0f);
}
