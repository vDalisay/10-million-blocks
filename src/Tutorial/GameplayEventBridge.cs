using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Content;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;

namespace TenMillionBlocks.Tutorial;

/// <summary>
/// Observes existing authoritative gameplay services and translates their state changes into semantic
/// events. It never changes mining, skill or automation behavior, so tutorial UI can be removed without
/// altering the game simulation.
/// </summary>
public partial class GameplayEventBridge : Node
{
    private WorldProfile _profile = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private MinerSimulationService _miners = null!;
    private GameplayEventHub _hub = null!;

    private readonly Dictionary<string, int> _previousRanks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previousUnlockedMiners = new(StringComparer.Ordinal);
    private ulong _lastManualMineAtMs;
    private Vector3I _lastManualVoxel;
    private bool _firstManualMinePublished;
    private bool _firstAreaMinePublished;

    public void Initialize(
        WorldProfile profile,
        MiningService mining,
        SkillTreeService skills,
        MinerSimulationService miners,
        GameplayEventHub hub)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _miners = miners ?? throw new ArgumentNullException(nameof(miners));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));

        SnapshotSkillState();
    }

    public override void _Ready()
    {
        _mining.BlockMined += OnBlockMined;
        _skills.Changed += OnSkillsChanged;
        _miners.MinerPlaced += OnMinerPlaced;
        _miners.MinerStopped += OnMinerStopped;

        _hub.Publish(new GameplayEvent(
            GameplayEventKind.WorldStarted,
            _profile.Id,
            _profile.IntroText));
    }

    public override void _ExitTree()
    {
        if (_mining is not null) _mining.BlockMined -= OnBlockMined;
        if (_skills is not null) _skills.Changed -= OnSkillsChanged;
        if (_miners is not null)
        {
            _miners.MinerPlaced -= OnMinerPlaced;
            _miners.MinerStopped -= OnMinerStopped;
        }
        DetachWorldEvents();
    }

    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed) return;

        if (result.Source == MiningSource.Manual)
        {
            if (!_firstManualMinePublished)
            {
                _firstManualMinePublished = true;
                _hub.Publish(new GameplayEvent(
                    GameplayEventKind.FirstManualMine,
                    _profile.Id,
                    result.BlockId,
                    result.Voxel,
                    result.BlocksRemoved));
            }

            ulong now = Time.GetTicksMsec();
            if (!_firstAreaMinePublished
                && _lastManualMineAtMs > 0
                && now - _lastManualMineAtMs <= 90
                && result.Voxel != _lastManualVoxel)
            {
                _firstAreaMinePublished = true;
                _hub.Publish(new GameplayEvent(
                    GameplayEventKind.FirstAreaMine,
                    _profile.Id,
                    result.BlockId,
                    result.Voxel,
                    2));
            }
            _lastManualMineAtMs = now;
            _lastManualVoxel = result.Voxel;
        }

        BlockDefinition definition = _mining.GetBlockDefinition(result.BlockId);
        if (definition.Tags.Contains("gem", StringComparer.Ordinal))
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.SpecialResourceFound,
                _profile.Id,
                result.BlockId,
                result.Voxel,
                1));
        }

        if (result.Remaining == 0)
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.WorldCompleted,
                _profile.Id,
                _profile.DisplayName,
                result.Voxel,
                _mining.TotalMined));
        }
    }

    private void OnSkillsChanged()
    {
        int oldHover = _previousRanks.GetValueOrDefault("hover_mining_unlock");
        int newHover = _skills.GetRank("hover_mining_unlock");
        if (oldHover <= 0 && newHover > 0)
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.HoverMiningUnlocked,
                _profile.Id,
                "hover_mining_unlock"));
        }

        int oldWide = _previousRanks.GetValueOrDefault("wide_bore_unlock");
        int newWide = _skills.GetRank("wide_bore_unlock");
        if (oldWide <= 0 && newWide > 0)
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.TransformationPurchased,
                _profile.Id,
                "wide_bore_unlock"));
        }

        foreach (string minerId in _skills.Derived.UnlockedMiners)
        {
            if (_previousUnlockedMiners.Contains(minerId)) continue;
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.AutomationClassUnlocked,
                _profile.Id,
                minerId));
        }

        SnapshotSkillState();
    }

    private void OnMinerPlaced(MinerInstance miner)
    {
        _hub.Publish(new GameplayEvent(
            GameplayEventKind.AutomationPlaced,
            _profile.Id,
            miner.DefinitionId,
            miner.Origin,
            miner.InstanceId));
    }

    private void OnMinerStopped(MinerInstance miner)
    {
        _hub.Publish(new GameplayEvent(
            GameplayEventKind.AutomationStopped,
            _profile.Id,
            miner.BlockedBlockId,
            miner.BlockedVoxel,
            miner.InstanceId));

        if (!string.Equals(miner.DefinitionId, "shovel_miner", StringComparison.Ordinal)) return;

        string blocker = miner.BlockedBlockId;
        if (blocker == _profile.WaterBlock
            || blocker == _profile.ShallowWaterBlock
            || blocker == _profile.DeepWaterBlock)
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.ShovelStoppedByWater,
                _profile.Id,
                blocker,
                miner.BlockedVoxel,
                miner.InstanceId));
        }
        else if (blocker == _profile.StoneBlock || blocker == _profile.DarkStoneBlock)
        {
            _hub.Publish(new GameplayEvent(
                GameplayEventKind.ShovelStoppedByStone,
                _profile.Id,
                blocker,
                miner.BlockedVoxel,
                miner.InstanceId));
        }
    }

    private void SnapshotSkillState()
    {
        _previousRanks.Clear();
        foreach ((string id, int rank) in _skills.Ranks)
        {
            _previousRanks[id] = rank;
        }

        _previousUnlockedMiners.Clear();
        foreach (string minerId in _skills.Derived.UnlockedMiners)
        {
            _previousUnlockedMiners.Add(minerId);
        }
    }
}
