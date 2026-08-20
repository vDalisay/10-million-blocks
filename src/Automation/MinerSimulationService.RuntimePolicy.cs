using System;
using System.Linq;
using Godot;
using TenMillionBlocks.Content;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    private int _lastDrillMaterialTier;
    private double _visualVisibilityRefreshTimer;

    public event Action<MinerInstance>? MinerStopped;

    public int AttentionMinerCount => _miners.Count(NeedsAttention);
    public int PresentedMinerCount => _visuals.Values.Count(root => root.Visible);

    public MinerInstance? GetAttentionMiner(int index)
    {
        int count = AttentionMinerCount;
        if (count <= 0) return null;

        int wanted = ((index % count) + count) % count;
        int current = 0;
        foreach (MinerInstance miner in _miners)
        {
            if (!NeedsAttention(miner)) continue;
            if (current++ == wanted) return miner;
        }
        return null;
    }

    public string DescribeStop(MinerInstance miner)
        => miner.StopReason switch
        {
            MinerStopReason.BlockedMaterial => string.IsNullOrWhiteSpace(miner.BlockedBlockId)
                ? "blocked by an unsupported material"
                : $"blocked by {miner.BlockedBlockId}",
            MinerStopReason.NoReachableTarget when IsShovel(_catalog.Get(miner.DefinitionId)) =>
                "stopped: no reachable shovel terrain",
            MinerStopReason.NoTreeTarget => "stopped: no reachable tree target",
            MinerStopReason.RangeComplete => "finished its configured range",
            _ => "stopped",
        };

    public Vector3I AttentionFocusVoxel(MinerInstance miner)
    {
        // A material blocker can be deep inside the cube. Focusing the surface entry keeps the user on
        // a visible, useful location; the alert text still reports the exact material that blocked it.
        if (miner.StopReason == MinerStopReason.BlockedMaterial)
        {
            return miner.Origin;
        }
        return miner.LastMinedVoxel;
    }

    private static bool NeedsAttention(MinerInstance miner)
        => miner.Exhausted && miner.StopReason is
            MinerStopReason.BlockedMaterial or
            MinerStopReason.NoReachableTarget or
            MinerStopReason.NoTreeTarget;

    private void StopMiner(
        MinerInstance miner,
        MinerStopReason reason,
        Vector3I blockedVoxel = default,
        string blockedBlockId = "")
    {
        bool wasAttention = NeedsAttention(miner);
        bool stateChanged = !miner.Exhausted
            || miner.StopReason != reason
            || miner.BlockedVoxel != blockedVoxel
            || !string.Equals(miner.BlockedBlockId, blockedBlockId, StringComparison.Ordinal);

        miner.Exhausted = true;
        miner.StopReason = reason;
        miner.BlockedVoxel = blockedVoxel;
        miner.BlockedBlockId = blockedBlockId;
        UpdateVisual(miner);

        if (stateChanged && NeedsAttention(miner) && !wasAttention)
        {
            MinerStopped?.Invoke(miner);
        }
    }

    private void ResumeMiner(MinerInstance miner, bool grantImmediateWork = true)
    {
        miner.Exhausted = false;
        miner.StopReason = MinerStopReason.None;
        miner.BlockedVoxel = Vector3I.Zero;
        miner.BlockedBlockId = string.Empty;
        if (grantImmediateWork)
        {
            miner.WorkAccumulator = Math.Max(miner.WorkAccumulator, 1.0);
        }
        UpdateVisual(miner);
    }

    private bool CanPrimaryDrillMine(BlockSample sample)
    {
        if (!sample.Present) return false;
        BlockDefinition block = _mining.GetBlockDefinition(sample.BlockId);
        return CanPrimaryDrillMine(sample.BlockId, block);
    }

    private bool CanPrimaryDrillMine(string blockId, BlockDefinition block)
    {
        // Unstable blocks intentionally remain blockers: a normal drill should never detonate one as a
        // side effect. Gems remain a later capability; the current ore bit only covers normal ores.
        if (block.Tags.Contains("bomb", StringComparer.Ordinal)) return false;
        if (blockId == _world.Profile.StoneBlock) return true;
        if (_skills.Derived.DrillMaterialTier >= 1 && blockId == _world.Profile.DarkStoneBlock) return true;
        if (_skills.Derived.DrillMaterialTier >= 2
            && block.Tags.Contains("ore", StringComparer.Ordinal)
            && !block.Tags.Contains("gem", StringComparer.Ordinal))
        {
            return true;
        }
        return false;
    }

    private bool BlockerIsNowSupported(MinerInstance miner)
    {
        if (miner.StopReason != MinerStopReason.BlockedMaterial || !IsPrimaryDrill(_catalog.Get(miner.DefinitionId)))
        {
            return false;
        }

        BlockSample sample = _world.SampleVoxel(miner.BlockedVoxel);
        // If another tool already removed the blocker, the drill may also resume.
        return !sample.Present || CanPrimaryDrillMine(sample);
    }

    private void RefreshAutomationVisualVisibility(double delta)
    {
        _visualVisibilityRefreshTimer += delta;
        if (_visualVisibilityRefreshTimer < 0.12)
        {
            return;
        }

        _visualVisibilityRefreshTimer = 0.0;
        foreach (MinerInstance miner in _miners)
        {
            RefreshVisualVisibility(miner);
        }
    }

    private void RefreshVisualVisibility(MinerInstance miner)
    {
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        Vector3I outward = -miner.Direction;
        Vector3I anchor = MinerAnchorVoxel(miner, definition);
        root.Visible = _view.ShouldPresentAutomation(anchor, outward);
    }

    private bool ShouldEmitPresentation(MinerInstance miner, Vector3I voxel)
        => _view.ShouldPresentAutomation(voxel, -miner.Direction);
}
