using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Interaction;

/// <summary>
/// Displays the exact set of voxels that the next manual/hover mining tick will affect. The complete
/// footprint is one MultiMesh draw primitive instead of one MeshInstance3D/draw call per highlighted
/// block, which keeps large overmining previews cheap while preserving the same breathing/pulse effect.
/// </summary>
public partial class SelectionHighlight : Node3D
{
    private readonly List<Vector3> _positions = new();
    private MultiMesh? _multiMesh;
    private MultiMeshInstance3D? _instance;
    private float _spacing = 2.0f;
    private float _time;
    private float _hitPulse;
    private int _activeCount;

    public void Initialize(float spacing)
    {
        _spacing = spacing;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.94f, 0.42f, 0.23f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };

        var mesh = new BoxMesh
        {
            Size = Vector3.One * spacing * 1.055f,
            Material = material,
        };

        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = 1,
            VisibleInstanceCount = 0,
        };
        _instance = new MultiMeshInstance3D
        {
            Name = "SelectionBatch",
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_instance);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _activeCount <= 0 || _multiMesh is null) return;

        float dt = Math.Max(0.0f, (float)delta);
        _time += dt;
        _hitPulse = MathF.Max(0.0f, _hitPulse - dt * 7.0f);
        float breathing = 1.0f + MathF.Sin(_time * 4.0f) * 0.010f;
        float hit = 1.0f + _hitPulse * 0.09f;
        Basis basis = Basis.Identity.Scaled(Vector3.One * breathing * hit);
        for (int i = 0; i < _activeCount; i++)
        {
            _multiMesh.SetInstanceTransform(i, new Transform3D(basis, _positions[i]));
        }
    }

    public void ShowVoxel(Vector3I voxel)
    {
        Span<Vector3I> one = stackalloc Vector3I[1];
        one[0] = voxel;
        ShowVoxels(one.ToArray());
    }

    public void ShowVoxels(IReadOnlyList<Vector3I> voxels)
    {
        if (voxels.Count == 0)
        {
            HideVoxel();
            return;
        }
        if (_multiMesh is null)
        {
            throw new InvalidOperationException("SelectionHighlight must be initialized before use.");
        }

        EnsureCapacity(voxels.Count);
        _positions.Clear();
        for (int i = 0; i < voxels.Count; i++)
        {
            Vector3 position = (Vector3)voxels[i] * _spacing;
            _positions.Add(position);
            _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, position));
        }

        _activeCount = voxels.Count;
        _multiMesh.VisibleInstanceCount = _activeCount;
        Visible = true;
    }

    public void PulseMine()
    {
        _hitPulse = 1.0f;
    }

    public void HideVoxel()
    {
        Visible = false;
        _activeCount = 0;
        _positions.Clear();
        if (_multiMesh is not null) _multiMesh.VisibleInstanceCount = 0;
    }

    private void EnsureCapacity(int count)
    {
        if (_multiMesh is null) return;
        if (_multiMesh.InstanceCount >= count) return;

        int capacity = 1;
        while (capacity < count) capacity <<= 1;
        _multiMesh.InstanceCount = capacity;
        _multiMesh.VisibleInstanceCount = 0;
        _positions.EnsureCapacity(capacity);
    }
}
