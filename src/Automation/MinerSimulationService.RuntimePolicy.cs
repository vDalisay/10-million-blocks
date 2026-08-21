using System;
using System.Linq;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

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
            MinerStopReason.BlockedMaterial => DescribeBlockedMaterial(miner.BlockedBlockId),
            MinerStopReason.BlockedFeature when miner.BlockedBlockId == "tree" =>
                "blocked by a tree; clear it manually or with the Forest Cutter",
            MinerStopReason.NoReachableTarget when IsShovel(_catalog.Get(miner.DefinitionId)) =>
                "stopped: no reachable shovel terrain",
            MinerStopReason.NoTreeTarget => "stopped: no reachable tree target",
            MinerStopReason.RangeComplete => "finished its configured range",
            _ => "stopped",
        };

    public Vector3I AttentionFocusVoxel(MinerInstance miner)
    {
        if (miner.StopReason == MinerStopReason.BlockedMaterial)
        {
            // Deep drill blockers are easier to understand from their tunnel entrance.
            return miner.Origin;
        }
        if (miner.StopReason == MinerStopReason.BlockedFeature)
        {
            return miner.BlockedVoxel;
        }
        return miner.LastMinedVoxel;
    }

    private string DescribeBlockedMaterial(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return "blocked by unsupported material; clear it manually or upgrade the drill bit";
        }

        if (blockId == _world.Profile.DarkStoneBlock)
        {
            return $"blocked by {blockId}; clear it manually or buy Hardened Bit";
        }

        if (blockId == _world.Profile.CopperBlock
            || blockId == _world.Profile.SilverBlock
            || blockId == _world.Profile.GoldBlock)
        {
            return $"blocked by {blockId}; clear it manually or buy Ore-Cutting Bit";
        }

        BlockDefinition block = _mining.GetBlockDefinition(blockId);
        if (block.Tags.Contains("gem", StringComparer.Ordinal))
        {
            return $"blocked by {blockId}; clear the gem with Rock Breaker/manual mining";
        }
        if (block.Tags.Contains("bomb", StringComparer.Ordinal))
        {
            return $"blocked by {blockId}; handle the unstable block manually";
        }

        return $"blocked by {blockId}; clear it manually or unlock a compatible tool";
    }

    private static bool NeedsAttention(MinerInstance miner)
        => miner.Exhausted && miner.StopReason is
            MinerStopReason.BlockedMaterial or
            MinerStopReason.BlockedFeature or
            MinerStopReason.NoReachableTarget or
            MinerStopReason.NoTreeTarget;

    private void StopMiner(
        MinerInstance miner,
        MinerStopReason reason,
        Vector3I blockedVoxel = default,
        string blockedBlockId = "")
    {
        // A primitive Shovel previously reported only "no reachable target" when the next otherwise
        // valid soft surface was occupied by a tree. Preserve the generic stop when terrain truly ends,
        // but promote a nearby tree into an explicit feature blocker so tutorial/UI logic can explain
        // the actual obstruction and automatically resume after the feature is cleared.
        if (reason == MinerStopReason.NoReachableTarget
            && IsShovel(_catalog.Get(miner.DefinitionId))
            && TryFindTreeBlockedShovelSurface(miner, out Vector3I treeSupport))
        {
            reason = MinerStopReason.BlockedFeature;
            blockedVoxel = treeSupport;
            blockedBlockId = "tree";
        }

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

    private bool TryFindTreeBlockedShovelSurface(MinerInstance miner, out Vector3I blocked)
    {
        blocked = default;
        Vector3I start = miner.BlocksMined > 0 ? miner.LastMinedVoxel : miner.Origin;
        Vector3I outward = -LineMiningPattern.Cardinal(miner.Direction);
        (Vector3I tangentA, Vector3I tangentB) = LineMiningPattern.PerpendicularAxes(outward);
        int radius = Math.Clamp(Math.Max(1, _skills.Derived.ShovelSearchRadius), 1, 8);
        int heightTolerance = Math.Clamp(Math.Max(0, _skills.Derived.ShovelHeightTolerance), 0, 3);

        for (int ring = 1; ring <= radius; ring++)
        {
            for (int a = -ring; a <= ring; a++)
            for (int b = -ring; b <= ring; b++)
            {
                if (Math.Max(Math.Abs(a), Math.Abs(b)) != ring) continue;
                if (ring == 1 && Math.Abs(a) + Math.Abs(b) != 1) continue;

                for (int height = 0; height <= heightTolerance; height++)
                {
                    int attempts = height == 0 ? 1 : 2;
                    for (int sign = 0; sign < attempts; sign++)
                    {
                        int radialOffset = height == 0 ? 0 : sign == 0 ? height : -height;
                        Vector3I candidate = start + tangentA * a + tangentB * b + outward * radialOffset;
                        BlockSample sample = _world.SampleVoxel(candidate);
                        if (!sample.Present
                            || !_world.IsExposed(candidate)
                            || !IsShovelMaterial(sample)
                            || _world.Source.GetOutwardNormal(candidate) != outward)
                        {
                            continue;
                        }

                        if (_world.Source.TrySampleTree(candidate, out FeatureSample feature)
                            && feature.OutwardNormal == outward)
                        {
                            blocked = candidate;
                            return true;
                        }
                    }
                }
            }
        }

        return false;
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
        if (miner.StopReason == MinerStopReason.BlockedFeature)
        {
            if (miner.BlockedBlockId != "tree") return false;
            if (!_world.IsPresent(miner.BlockedVoxel)) return true;
            return !_world.Source.TrySampleTree(miner.BlockedVoxel, out _);
        }

        if (miner.StopReason != MinerStopReason.BlockedMaterial || !IsPrimaryDrill(_catalog.Get(miner.DefinitionId)))
        {
            return false;
        }

        BlockSample sample = _world.SampleVoxel(miner.BlockedVoxel);
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
        _view.RefreshViewDependentPresentation();
        _view.RefreshDeferredAutomationPresentation();

        bool resumed = false;
        foreach (MinerInstance miner in _miners)
        {
            if (miner.Exhausted
                && miner.StopReason is MinerStopReason.BlockedMaterial or MinerStopReason.BlockedFeature
                && BlockerIsNowSupported(miner))
            {
                ResumeMiner(miner);
                resumed = true;
            }
            RefreshVisualVisibility(miner);
        }

        if (resumed)
        {
            Changed?.Invoke();
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
