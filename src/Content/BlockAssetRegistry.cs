using System;
using System.Collections.Generic;
using Godot;

namespace TenMillionBlocks.Content;

public sealed class BlockAssetRegistry
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, PackedScene> _scenes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.Ordinal);

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
