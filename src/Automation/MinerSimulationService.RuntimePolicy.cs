using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.World.Generation;

namespace TenMillionBlocks.Automation;

public partial class MinerSimulationService
{
    private const int MaxVisualPolicyChecksPerRefresh = 256;

    private int _lastDrillMaterialTier;
    private double _visualVisibilityRefreshTimer;
    private int _visualPolicyCursor;
    private readonly HashSet<long> _attentionMinerIds = new();

    public event Action<MinerInstance>? MinerStopped;

    public int AttentionMinerCount
    {
        get
        {
            int count = 0;
            foreach (long id in _attentionMinerIds)
            {
                if (_minersById.TryGetValue(id, out MinerInstance? miner) && NeedsAttention(miner)) count++;
            }
            return count;
        }
    }

    public int PresentedMinerCount
    {
        get
        {
            int count = 0;
            foreach (Node3D root in _visuals.Values)
            {
                if (root.Visible) count++;
            }
            return count;
        }
    }

    public MinerInstance? GetAttentionMiner(int index)
    {
        int count = AttentionMinerCount;
        if (count <= 0) return null;

        int wanted = ((index % count) + count) % count;
        int current = 0;
        foreach (long id in _attentionMinerIds)
        {
            if (!_minersById.TryGetValue(id, out MinerInstance? miner) || !NeedsAttention(miner)) continue;
            if (current++ == wanted) return miner;
        }
        return null;
    }

    public string DescribeStop(MinerInstance miner)
        => miner.StopReason switch
        {
            MinerStopReason.BlockedMaterial => DescribeBlockedMaterial(miner.BlockedBlockId),
            MinerStopReason.BlockedFeature when miner.BlockedBlockId == "tree" =>
                "blocked by a tree; clear it manually or use a compatible tree-clearing machine once unlocked",
            MinerStopReason.BlockedTerrain => DescribeShovelTerrainBlocker(miner.BlockedBlockId),
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
            return miner.Origin;
        }
        if (miner.StopReason is MinerStopReason.BlockedFeature or MinerStopReason.BlockedTerrain)
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

    private string DescribeShovelTerrainBlocker(string blockId)
    {
        if (IsWaterId(blockId)) return "blocked by water; clear it manually or route the Shovel around the lake";
        if (IsStoneId(blockId)) return "blocked by stone; clear it manually or with a stone-capable tool";
        return string.IsNullOrWhiteSpace(blockId)
            ? "blocked by a physical surface obstruction"
            : $"blocked by {blockId}; clear the obstruction to resume the Shovel";
    }

    private bool IsWaterId(string blockId)
        => blockId == _world.Profile.WaterBlock
            || blockId == _world.Profile.ShallowWaterBlock
            || blockId == _world.Profile.DeepWaterBlock;

    private bool IsStoneId(string blockId)
    {
        if (blockId == _world.Profile.StoneBlock || blockId == _world.Profile.DarkStoneBlock) return true;
        if (string.IsNullOrWhiteSpace(blockId)) return false;
        return _mining.GetBlockDefinition(blockId).Tags.Contains("stone", StringComparer.Ordinal);
    }

    private static bool NeedsAttention(MinerInstance miner)
        => miner.Exhausted && miner.StopReason is
            MinerStopReason.BlockedMaterial or
            MinerStopReason.BlockedFeature or
            MinerStopReason.BlockedTerrain or
            MinerStopReason.NoReachableTarget or
            MinerStopReason.NoTreeTarget;

    private void TrackAttentionState(MinerInstance miner)
    {
        if (NeedsAttention(miner)) _attentionMinerIds.Add(miner.InstanceId);
        else _attentionMinerIds.Remove(miner.InstanceId);
    }

    private void StopMiner(
        MinerInstance miner,
        MinerStopReason reason,
        Vector3I blockedVoxel = default,
        string blockedBlockId = "")
    {
        if (reason == MinerStopReason.NoReachableTarget
            && IsShovel(_catalog.Get(miner.DefinitionId))
            && TryFindShovelSurfaceBlocker(
                miner,
                out MinerStopReason classifiedReason,
                out Vector3I classifiedVoxel,
                out string classifiedBlocker))
        {
            reason = classifiedReason;
            blockedVoxel = classifiedVoxel;
            blockedBlockId = classifiedBlocker;
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
        TrackAttentionState(miner);
        UpdateVisual(miner);

        if (stateChanged && NeedsAttention(miner) && !wasAttention)
        {
            MinerStopped?.Invoke(miner);
        }
    }

    private bool TryFindShovelSurfaceBlocker(
        MinerInstance miner,
        out MinerStopReason reason,
        out Vector3I blocked,
        out string blockId)
    {
        reason = MinerStopReason.None;
        blocked = default;
        blockId = string.Empty;

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
                            || !_world.IsExposed(candidate, sample)
                            || _world.Source.GetOutwardNormal(candidate) != outward)
                        {
                            continue;
                        }

                        if (IsShovelMaterial(sample))
                        {
                            if (_world.Source.TrySampleTree(candidate, out FeatureSample feature)
                                && feature.OutwardNormal == outward)
                            {
                                reason = MinerStopReason.BlockedFeature;
                                blocked = candidate;
                                blockId = "tree";
                                return true;
                            }

                            BlockSample outwardObstruction = _world.SampleVoxel(candidate + outward);
                            if (outwardObstruction.Present)
                            {
                                reason = MinerStopReason.BlockedTerrain;
                                blocked = candidate + outward;
                                blockId = outwardObstruction.BlockId;
                                return true;
                            }
                            continue;
                        }

                        if (IsWaterId(sample.BlockId) || IsStoneId(sample.BlockId))
                        {
                            reason = MinerStopReason.BlockedTerrain;
                            blocked = candidate;
                            blockId = sample.BlockId;
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
        _attentionMinerIds.Remove(miner.InstanceId);
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

        if (miner.StopReason == MinerStopReason.BlockedTerrain)
        {
            return !_world.IsPresent(miner.BlockedVoxel);
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

        int minerCount = _miners.Count;
        if (minerCount == 0)
        {
            _visualPolicyCursor = 0;
            return;
        }

        bool resumed = false;
        int checks = Math.Min(minerCount, MaxVisualPolicyChecksPerRefresh);
        for (int i = 0; i < checks; i++)
        {
            if (_visualPolicyCursor >= minerCount) _visualPolicyCursor = 0;
            MinerInstance miner = _miners[_visualPolicyCursor++];
            if (miner.Exhausted
                && miner.StopReason is MinerStopReason.BlockedMaterial
                    or MinerStopReason.BlockedFeature
                    or MinerStopReason.BlockedTerrain
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
        TrackAttentionState(miner);
        if (!_visuals.TryGetValue(miner.InstanceId, out Node3D? root)) return;
        MinerDefinition definition = _catalog.Get(miner.DefinitionId);
        Vector3I outward = -miner.Direction;
        Vector3I anchor = MinerAnchorVoxel(miner, definition);
        root.Visible = _view.ShouldPresentAutomation(anchor, outward);
    }

    private bool ShouldEmitPresentation(MinerInstance miner, Vector3I voxel)
        => _view.ShouldPresentAutomation(voxel, -miner.Direction);
}
