using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Content;

public sealed class BlockAssetRegistry
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, PackedScene> _scenes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material?> _materialOverrides = new(StringComparer.Ordinal);

    public BlockAssetRegistry(ContentDatabase content)
    {
        _content = content;
    }

    public void ValidateAndPreload()
    {
        var failures = new List<string>();

        foreach ((string id, BlockDefinition definition) in _content.Blocks)
        {
            if (!ResourceLoader.Exists(definition.AssetPath))
            {
                failures.Add($"{id}: asset does not exist at '{definition.AssetPath}'.");
                continue;
            }

            PackedScene? scene = GD.Load<PackedScene>(definition.AssetPath);
            if (scene is null)
            {
                failures.Add($"{id}: '{definition.AssetPath}' did not import as a PackedScene.");
                continue;
            }

            _scenes[id] = scene;

            Node instance = scene.Instantiate();
            MeshInstance3D? meshInstance = FindFirstMesh(instance);
            if (meshInstance?.Mesh is null)
            {
                failures.Add($"{id}: imported scene contains no MeshInstance3D mesh.");
            }
            else
            {
                _meshes[id] = meshInstance.Mesh;
            }

            instance.Free();
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Block asset validation failed:\n - " + string.Join("\n - ", failures));
        }

        GD.Print($"Validated {_meshes.Count} supplied block meshes.");
    }

    public Mesh GetMesh(string blockId)
    {
        if (!_meshes.TryGetValue(blockId, out Mesh? mesh))
        {
            throw new KeyNotFoundException($"Block mesh '{blockId}' is not loaded. Call ValidateAndPreload first.");
        }

        return mesh;
    }

    public BlockDefinition GetDefinition(string blockId) => _content.GetBlock(blockId);

    public Material? GetMaterialOverride(string blockId)
    {
        if (_materialOverrides.TryGetValue(blockId, out Material? cached))
        {
            return cached;
        }

        BlockDefinition definition = _content.GetBlock(blockId);
        if (definition.RenderTint.Count == 0)
        {
            _materialOverrides[blockId] = null;
            return null;
        }

        float r = definition.RenderTint.Count > 0 ? definition.RenderTint[0] : 1.0f;
        float g = definition.RenderTint.Count > 1 ? definition.RenderTint[1] : 1.0f;
        float b = definition.RenderTint.Count > 2 ? definition.RenderTint[2] : 1.0f;
        float a = definition.RenderTint.Count > 3 ? definition.RenderTint[3] : 1.0f;
        Color tint = new(r, g, b, a);

        Mesh mesh = GetMesh(blockId);
        Material? source = mesh.GetSurfaceCount() > 0 ? mesh.SurfaceGetMaterial(0) : null;
        Material material;
        if (source is StandardMaterial3D standard)
        {
            var duplicate = (StandardMaterial3D)standard.Duplicate(true);
            Color baseColor = duplicate.AlbedoColor;
            duplicate.AlbedoColor = new Color(
                baseColor.R * tint.R,
                baseColor.G * tint.G,
                baseColor.B * tint.B,
                baseColor.A * tint.A);
            material = duplicate;
        }
        else
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Roughness = 0.82f,
            };
        }

        _materialOverrides[blockId] = material;
        return material;
    }

    public Node3D Instantiate(string blockId)
    {
        if (!_scenes.TryGetValue(blockId, out PackedScene? scene))
        {
            throw new KeyNotFoundException($"Block scene '{blockId}' is not loaded. Call ValidateAndPreload first.");
        }

        Node instance = scene.Instantiate();
        if (instance is Node3D node3D)
        {
            return node3D;
        }

        instance.Free();
        throw new InvalidOperationException($"Block scene '{blockId}' does not have a Node3D root.");
    }

    private static MeshInstance3D? FindFirstMesh(Node node)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
        {
            return meshInstance;
        }

        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D? found = FindFirstMesh(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
