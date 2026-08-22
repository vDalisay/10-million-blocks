using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Presentation;

namespace TenMillionBlocks.World.Rendering;

public partial class WorldView
{
    private const int MaxActiveMinePops = 32;
    private const int MaxActiveDebrisBursts = 24;
    private const float MinePopGrowSeconds = 0.075f;
    private const float MinePopShrinkSeconds = 0.055f;

    private sealed class MinePopVisual
    {
        public MeshInstance3D Node { get; init; } = null!;
        public float Age;
        public float PeakScale;
    }

    private readonly List<MinePopVisual> _activeMinePops = new();
    private readonly Stack<MinePopVisual> _minePopPool = new();
    private readonly Stack<DrillDebrisBurst> _debrisPool = new();
    private WorldMiningFeedbackTicker? _miningFeedbackTicker;
    private int _activeDebrisBursts;

    public int ActiveMinePopCount => _activeMinePops.Count;
    public int PooledMinePopCount => _minePopPool.Count;
    public int ActiveDebrisBurstCount => _activeDebrisBursts;
    public int PooledDebrisBurstCount => _debrisPool.Count;
    public long DroppedMinePopCount { get; private set; }
    public long DroppedDebrisBurstCount { get; private set; }

    /// <summary>
    /// Short-lived copy of the mined block used for manual/replay feedback. Nodes are pooled and the
    /// animation is advanced centrally, avoiding a Tween + QueueFree allocation for every mined block
    /// during high-rate hover mining/replay. Off-screen events are culled before acquiring a pooled node.
    /// Reduced-motion mode suppresses this cosmetic animation entirely; authoritative mining and HUD
    /// counters are unaffected.
    /// </summary>
    public void SpawnManualMinePop(Vector3I voxel, string blockId, float peakScale = 1.12f)
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            return;
        }

        Vector3 worldPosition = VoxelToWorld(voxel);
        if (_activeMinePops.Count >= MaxActiveMinePops || !ShouldSpawnMiningFx(worldPosition, 72.0f))
        {
            DroppedMinePopCount++;
            return;
        }

        EnsureMiningFeedbackTicker();
        string visualBlockId = ResolveSurfaceVisualBlockId(voxel, blockId);
        Vector3I outward = _world.Source.GetOutwardNormal(voxel);
        Basis basis = ShouldOrientToCubeFace(visualBlockId)
            ? BasisForNormal(outward)
            : Basis.Identity;

        MinePopVisual pop = _minePopPool.Count > 0 ? _minePopPool.Pop() : CreateMinePopVisual();
        pop.Age = 0.0f;
        pop.PeakScale = MathF.Max(1.0f, peakScale);
        pop.Node.Name = $"MinePop_{voxel.X}_{voxel.Y}_{voxel.Z}";
        pop.Node.Mesh = _assets.GetMesh(visualBlockId);
        pop.Node.MaterialOverride = _assets.GetMaterialOverride(visualBlockId);
        pop.Node.Transform = new Transform3D(basis, worldPosition);
        pop.Node.Scale = Vector3.One * 0.985f;
        pop.Node.Visible = true;
        _activeMinePops.Add(pop);
    }

    /// <summary>
    /// Shared mining-only debris used by live mining, automation and replay. Bursts are pooled and each
    /// burst uses one MultiMesh for all fragments, so dense mining does not create/free effect containers,
    /// MeshInstance3D nodes, meshes or materials per action. Off-screen events stay simulation-only.
    /// Reduced-motion mode omits the burst rather than changing any mining result.
    /// </summary>
    public void SpawnMiningDebris(Vector3I voxel, string blockId, int seed, string name = "MiningDebris")
    {
        Vector3 outward = (Vector3)_world.Source.GetOutwardNormal(voxel);
        SpawnMiningDebris(voxel, blockId, seed, outward, name);
    }

    /// <summary>
    /// Explicit-direction overload for automation. A miner already knows the face it entered from, so
    /// reusing that direction avoids another procedural outward-normal query and keeps debris travelling
    /// out of the machine even at cube seams.
    /// </summary>
    public void SpawnMiningDebris(
        Vector3I voxel,
        string blockId,
        int seed,
        Vector3 outward,
        string name = "MiningDebris")
    {
        if (GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true)
        {
            return;
        }

        if (outward.LengthSquared() < 0.0001f)
        {
            outward = (Vector3)_world.Source.GetOutwardNormal(voxel);
        }
        else
        {
            outward = outward.Normalized();
        }

        float spacing = _world.Profile.BlockSpacing;
        Vector3 position = VoxelToWorld(voxel) + outward * spacing * 0.48f;
        if (_activeDebrisBursts >= MaxActiveDebrisBursts || !ShouldSpawnMiningFx(position, 96.0f))
        {
            DroppedDebrisBurstCount++;
            return;
        }

        string visualBlockId = ResolveSurfaceVisualBlockId(voxel, blockId);
        DrillDebrisBurst burst;
        if (_debrisPool.Count > 0)
        {
            burst = _debrisPool.Pop();
        }
        else
        {
            burst = new DrillDebrisBurst { Name = name };
            burst.Finished += ReturnDebrisBurst;
            AddChild(burst);
        }

        _activeDebrisBursts++;
        burst.Play(position, outward, visualBlockId, spacing, seed, name);
    }

    private bool ShouldSpawnMiningFx(Vector3 worldPosition, float screenMargin)
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return true;
        if (camera.IsPositionBehind(worldPosition)) return false;

        Vector2 screen = camera.UnprojectPosition(worldPosition);
        Rect2 visible = GetViewport().GetVisibleRect().Grow(screenMargin);
        return visible.HasPoint(screen);
    }

    private void EnsureMiningFeedbackTicker()
    {
        if (_miningFeedbackTicker is not null && IsInstanceValid(_miningFeedbackTicker)) return;
        _miningFeedbackTicker = new WorldMiningFeedbackTicker { Name = "MiningFeedbackTicker" };
        _miningFeedbackTicker.Tick = AdvanceMiningFeedback;
        AddChild(_miningFeedbackTicker);
    }

    private void AdvanceMiningFeedback(double delta)
    {
        if (_activeMinePops.Count == 0) return;

        float dt = Math.Max(0.0f, (float)delta);
        float total = MinePopGrowSeconds + MinePopShrinkSeconds;
        for (int i = _activeMinePops.Count - 1; i >= 0; i--)
        {
            MinePopVisual pop = _activeMinePops[i];
            pop.Age += dt;

            float scale;
            if (pop.Age < MinePopGrowSeconds)
            {
                float t = Math.Clamp(pop.Age / MinePopGrowSeconds, 0.0f, 1.0f);
                float eased = EaseOutBack(t);
                scale = Mathf.Lerp(0.985f, pop.PeakScale, eased);
            }
            else
            {
                float t = Math.Clamp((pop.Age - MinePopGrowSeconds) / MinePopShrinkSeconds, 0.0f, 1.0f);
                scale = Mathf.Lerp(pop.PeakScale, 0.92f, t * t);
            }

            pop.Node.Scale = Vector3.One * scale;
            if (pop.Age < total) continue;

            pop.Node.Visible = false;
            pop.Node.Mesh = null;
            pop.Node.MaterialOverride = null;
            _activeMinePops.RemoveAt(i);
            _minePopPool.Push(pop);
        }
    }

    private MinePopVisual CreateMinePopVisual()
    {
        var node = new MeshInstance3D
        {
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return new MinePopVisual { Node = node };
    }

    private void ReturnDebrisBurst(DrillDebrisBurst burst)
    {
        _activeDebrisBursts = Math.Max(0, _activeDebrisBursts - 1);
        if (!IsInstanceValid(burst) || burst.GetParent() != this) return;
        _debrisPool.Push(burst);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1.0f;
        float x = t - 1.0f;
        return 1.0f + c3 * x * x * x + c1 * x * x;
    }
}
