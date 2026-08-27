using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private readonly record struct IntroWaveInstance(
        MultiMesh MultiMesh,
        int Index,
        Transform3D BaseTransform,
        float NormalizedScreenX);

    private readonly List<IntroWaveInstance> _introWaveInstances = new();
    private bool _introWavePrepared;

    public int IntroWaveInstanceCount => _introWaveInstances.Count;

    public void PrepareIntroWave(Camera3D camera)
    {
        _introWaveInstances.Clear();
        _introWavePrepared = false;
        if (camera is null || _world is null) return;

        var pending = new List<(MultiMesh Mesh, int Index, Transform3D Transform, float ScreenX)>();
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;

        foreach (Node3D root in _chunkRoots.Values)
        {
            CollectIntroWaveInstances(root, camera, pending, ref minX, ref maxX);
        }

        if (pending.Count == 0) return;
        float width = Math.Max(1.0f, maxX - minX);
        foreach ((MultiMesh mesh, int index, Transform3D transform, float screenX) in pending)
        {
            _introWaveInstances.Add(new IntroWaveInstance(
                mesh,
                index,
                transform,
                Mathf.Clamp((screenX - minX) / width, 0.0f, 1.0f)));
        }
        _introWavePrepared = true;
    }

    public void UpdateIntroWave(double elapsedSeconds)
    {
        if (!_introWavePrepared || _introWaveInstances.Count == 0) return;
        float t = Math.Max(0.0f, (float)elapsedSeconds);
        float spacing = _world.Profile.BlockSpacing;
        const float firstDelay = 0.25f;
        const float delaySpan = 1.90f;
        const float pulseDuration = 0.72f;

        foreach (IntroWaveInstance item in _introWaveInstances)
        {
            float local = (t - (firstDelay + item.NormalizedScreenX * delaySpan)) / pulseDuration;
            float lift = 0.0f;
            if (local > 0.0f && local < 1.0f)
            {
                float wave = MathF.Sin(local * Mathf.Pi);
                lift = MathF.Pow(Math.Max(0.0f, wave), 1.12f) * spacing * 0.56f;
            }

            Transform3D moved = item.BaseTransform;
            moved.Origin += Vector3.Up * lift;
            item.MultiMesh.SetInstanceTransform(item.Index, moved);
        }
    }

    public void ResetIntroWave()
    {
        foreach (IntroWaveInstance item in _introWaveInstances)
            item.MultiMesh.SetInstanceTransform(item.Index, item.BaseTransform);
        _introWaveInstances.Clear();
        _introWavePrepared = false;
    }

    private void CollectIntroWaveInstances(
        Node node,
        Camera3D camera,
        List<(MultiMesh Mesh, int Index, Transform3D Transform, float ScreenX)> pending,
        ref float minX,
        ref float maxX)
    {
        if (node is MultiMeshInstance3D batch && batch.Multimesh is MultiMesh multiMesh)
        {
            int visible = multiMesh.VisibleInstanceCount < 0 ? multiMesh.InstanceCount : multiMesh.VisibleInstanceCount;
            bool treeBatch = batch.Name.ToString().Contains("tree_", StringComparison.OrdinalIgnoreCase);
            for (int index = 0; index < visible; index++)
            {
                Transform3D transform = multiMesh.GetInstanceTransform(index);
                bool include;
                if (treeBatch)
                {
                    Vector3 localUp = transform.Basis.Y;
                    include = localUp.LengthSquared() > 0.0001f && localUp.Normalized().Dot(Vector3.Up) > 0.72f;
                }
                else if (_world.Profile.UsesSingleBlockGenerator)
                {
                    include = true;
                }
                else
                {
                    float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
                    Vector3 origin = transform.Origin;
                    var voxel = new Vector3I(
                        Mathf.RoundToInt(origin.X / spacing),
                        Mathf.RoundToInt(origin.Y / spacing),
                        Mathf.RoundToInt(origin.Z / spacing));
                    BlockSample sample = _world.SampleVoxel(voxel);
                    include = sample.Present && _world.Source.GetOutwardNormal(voxel) == Vector3I.Up;
                }

                if (!include) continue;
                Vector3 global = batch.ToGlobal(transform.Origin);
                if (camera.IsPositionBehind(global)) continue;
                float screenX = camera.UnprojectPosition(global).X;
                minX = Math.Min(minX, screenX);
                maxX = Math.Max(maxX, screenX);
                pending.Add((multiMesh, index, transform, screenX));
            }
        }

        foreach (Node child in node.GetChildren())
            CollectIntroWaveInstances(child, camera, pending, ref minX, ref maxX);
    }
}
