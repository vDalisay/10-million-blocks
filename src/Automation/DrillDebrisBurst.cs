using System;
using Godot;

namespace TenMillionBlocks.Automation;

/// <summary>
/// Short-lived mining debris rendered as one MultiMesh instead of one MeshInstance3D/material/mesh per
/// fragment. WorldView keeps these burst nodes pooled, so rapid hover mining and dense automation avoid
/// repeated SceneTree allocation/free churn and keep the effect to a single draw primitive per burst.
/// </summary>
public partial class DrillDebrisBurst : Node3D
{
    private const int MaxPieces = 9;
    private const float LifetimeSeconds = 0.72f;

    private struct DebrisPiece
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Rotation;
        public Vector3 AngularVelocity;
        public float Size;
    }

    private static BoxMesh? _sharedMesh;
    private static StandardMaterial3D? _sharedMaterial;

    private readonly DebrisPiece[] _pieces = new DebrisPiece[MaxPieces];
    private MultiMeshInstance3D _instance = null!;
    private MultiMesh _multiMesh = null!;
    private int _pieceCount;
    private float _age;
    private Vector3 _gravityDirection;

    public event Action<DrillDebrisBurst>? Finished;

    public override void _Ready()
    {
        EnsureSharedResources();

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _sharedMesh,
            InstanceCount = MaxPieces,
            VisibleInstanceCount = 0,
        };

        _instance = new MultiMeshInstance3D
        {
            Name = "DebrisMultiMesh",
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_instance);

        Visible = false;
        SetProcess(false);
    }

    public void Play(
        Vector3 worldPosition,
        Vector3 outward,
        string blockId,
        float spacing,
        int seed,
        string name = "MiningDebris")
    {
        Name = name;
        GlobalPosition = worldPosition;
        _gravityDirection = -outward.Normalized();
        _age = 0.0f;

        var random = new Random(seed);
        _pieceCount = blockId.Contains("water", StringComparison.Ordinal) ? 7 : MaxPieces;
        Vector3 tangentA = MathF.Abs(outward.Dot(Vector3.Up)) > 0.9f
            ? Vector3.Right
            : outward.Cross(Vector3.Up).Normalized();
        Vector3 tangentB = outward.Cross(tangentA).Normalized();

        for (int i = 0; i < _pieceCount; i++)
        {
            float sideA = ((float)random.NextDouble() - 0.5f) * spacing * 1.25f;
            float sideB = ((float)random.NextDouble() - 0.5f) * spacing * 1.25f;
            float outwardSpeed = spacing * (1.45f + (float)random.NextDouble() * 2.0f);
            float size = spacing * (0.055f + (float)random.NextDouble() * 0.045f);

            _pieces[i] = new DebrisPiece
            {
                Position = Vector3.Zero,
                Velocity = outward * outwardSpeed + tangentA * sideA + tangentB * sideB,
                Rotation = Vector3.Zero,
                AngularVelocity = new Vector3(
                    ((float)random.NextDouble() - 0.5f) * 7.0f,
                    ((float)random.NextDouble() - 0.5f) * 7.0f,
                    ((float)random.NextDouble() - 0.5f) * 7.0f),
                Size = size,
            };

            _multiMesh.SetInstanceColor(i, ResolveColor(blockId, i));
            WriteTransform(i, _pieces[i], 1.0f);
        }

        _multiMesh.VisibleInstanceCount = _pieceCount;
        Visible = true;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        float dt = Math.Max(0.0f, (float)delta);
        _age += dt;
        float lifeScale = MathF.Max(0.08f, 1.0f - _age / LifetimeSeconds);

        for (int i = 0; i < _pieceCount; i++)
        {
            DebrisPiece piece = _pieces[i];
            piece.Velocity += _gravityDirection * 2.4f * dt;
            piece.Position += piece.Velocity * dt;
            piece.Rotation += piece.AngularVelocity * dt;
            _pieces[i] = piece;
            WriteTransform(i, piece, lifeScale);
        }

        if (_age < LifetimeSeconds) return;

        _multiMesh.VisibleInstanceCount = 0;
        Visible = false;
        SetProcess(false);
        Finished?.Invoke(this);
    }

    private void WriteTransform(int index, DebrisPiece piece, float lifeScale)
    {
        Basis basis = Basis.Identity
            .Rotated(Vector3.Right, piece.Rotation.X)
            .Rotated(Vector3.Up, piece.Rotation.Y)
            .Rotated(Vector3.Back, piece.Rotation.Z)
            .Scaled(Vector3.One * piece.Size * lifeScale);
        _multiMesh.SetInstanceTransform(index, new Transform3D(basis, piece.Position));
    }

    private static void EnsureSharedResources()
    {
        if (_sharedMaterial is null)
        {
            _sharedMaterial = new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                Roughness = 0.92f,
                VertexColorUseAsAlbedo = true,
            };
        }

        _sharedMesh ??= new BoxMesh
        {
            Size = Vector3.One,
            Material = _sharedMaterial,
        };
    }

    private static Color ResolveColor(string blockId, int index)
    {
        if (blockId is "grass" or "dirt_grass")
        {
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
