using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.World.Interaction;

/// <summary>
/// Displays the exact set of voxels that the next manual/hover mining tick will affect. Instances share
/// one mesh/material so large footprint upgrades can preview their removal area without creating a new
/// material for every highlighted block.
/// </summary>
public partial class SelectionHighlight : Node3D
{
    private readonly List<MeshInstance3D> _instances = new();
    private BoxMesh? _mesh;
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

        _mesh = new BoxMesh
        {
            Size = Vector3.One * spacing * 1.055f,
            Material = material,
        };

        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _activeCount <= 0) return;

        float dt = (float)delta;
        _time += dt;
        _hitPulse = MathF.Max(0.0f, _hitPulse - dt * 7.0f);
        float breathing = 1.0f + MathF.Sin(_time * 4.0f) * 0.010f;
        float hit = 1.0f + _hitPulse * 0.09f;
        Vector3 scale = Vector3.One * breathing * hit;
        for (int i = 0; i < _activeCount; i++)
        {
            _instances[i].Scale = scale;
        }
    }

    public void ShowVoxel(Vector3I voxel)
        => ShowVoxels(new[] { voxel });

    public void ShowVoxels(IReadOnlyList<Vector3I> voxels)
    {
        if (voxels.Count == 0)
        {
            HideVoxel();
            return;
        }

        EnsureInstances(voxels.Count);
        _activeCount = voxels.Count;
        for (int i = 0; i < _instances.Count; i++)
        {
            MeshInstance3D instance = _instances[i];
            bool active = i < _activeCount;
            instance.Visible = active;
            if (!active) continue;
            instance.Position = (Vector3)voxels[i] * _spacing;
            instance.Scale = Vector3.One;
        }
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
        foreach (MeshInstance3D instance in _instances)
        {
            instance.Visible = false;
            instance.Scale = Vector3.One;
        }
    }

    private void EnsureInstances(int count)
    {
        if (_mesh is null)
        {
            throw new InvalidOperationException("SelectionHighlight must be initialized before use.");
        }

        while (_instances.Count < count)
        {
            var instance = new MeshInstance3D
            {
                Name = $"Selection_{_instances.Count}",
                Mesh = _mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(instance);
            _instances.Add(instance);
        }
    }
}
