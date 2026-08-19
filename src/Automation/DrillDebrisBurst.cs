using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Automation;

public partial class DrillDebrisBurst : Node3D
{
    private sealed class DebrisPiece
    {
        public MeshInstance3D Node { get; init; } = null!;
        public Vector3 Velocity { get; set; }
        public Vector3 AngularVelocity { get; init; }
    }

    private readonly List<DebrisPiece> _pieces = new();
    private float _age;
    private Vector3 _gravityDirection;

    public void Initialize(Vector3 worldPosition, Vector3 outward, string blockId, float spacing, int seed)
    {
        GlobalPosition = worldPosition;
        _gravityDirection = -outward.Normalized();

        var random = new Random(seed);
        int count = blockId.Contains("water", StringComparison.Ordinal) ? 7 : 9;
        Vector3 tangentA = MathF.Abs(outward.Dot(Vector3.Up)) > 0.9f
            ? Vector3.Right
            : outward.Cross(Vector3.Up).Normalized();
        Vector3 tangentB = outward.Cross(tangentA).Normalized();

        for (int i = 0; i < count; i++)
        {
            Color color = ResolveColor(blockId, i);
            var material = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = 0.92f,
            };

            float size = spacing * (0.055f + (float)random.NextDouble() * 0.045f);
            var mesh = new BoxMesh
            {
                Size = Vector3.One * size,
                Material = material,
            };

            var node = new MeshInstance3D
            {
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(node);

            float sideA = ((float)random.NextDouble() - 0.5f) * spacing * 1.25f;
            float sideB = ((float)random.NextDouble() - 0.5f) * spacing * 1.25f;
            float outwardSpeed = spacing * (1.45f + (float)random.NextDouble() * 2.0f);
            Vector3 velocity = outward * outwardSpeed + tangentA * sideA + tangentB * sideB;

            _pieces.Add(new DebrisPiece
            {
                Node = node,
                Velocity = velocity,
                AngularVelocity = new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 7.0f,
                    ((float)random.NextDouble() - 0.5f) * 7.0f,
                    ((float)random.NextDouble() - 0.5f) * 7.0f),
            });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        foreach (DebrisPiece piece in _pieces)
        {
            piece.Velocity += _gravityDirection * 2.4f * dt;
            piece.Node.Position += piece.Velocity * dt;
            piece.Node.Rotation += piece.AngularVelocity * dt;
            piece.Node.Scale = Vector3.One * MathF.Max(0.08f, 1.0f - _age / 0.72f);
        }

        if (_age >= 0.72f)
        {
            QueueFree();
        }
    }

    private static Color ResolveColor(string blockId, int index)
    {
        if (blockId is "grass" or "dirt_grass")
        {
            // Mostly dirt with occasional green turf fragments, matching the block being drilled.
            return index % 4 == 0
                ? new Color(0.10f, 0.62f, 0.25f)
                : new Color(0.48f, 0.27f, 0.13f);
        }

        if (blockId == "dirt") return new Color(0.49f, 0.28f, 0.15f);
        if (blockId == "sand") return new Color(0.86f, 0.72f, 0.45f);
        if (blockId.Contains("water", StringComparison.Ordinal)) return new Color(0.20f, 0.58f, 0.96f);
        if (blockId == "copper") return new Color(0.64f, 0.38f, 0.24f);
        if (blockId == "silver") return new Color(0.73f, 0.78f, 0.84f);
        if (blockId == "gold") return new Color(0.92f, 0.69f, 0.16f);
        if (blockId == "stone_dark") return new Color(0.24f, 0.28f, 0.32f);
        return new Color(0.48f, 0.52f, 0.57f);
    }
}
