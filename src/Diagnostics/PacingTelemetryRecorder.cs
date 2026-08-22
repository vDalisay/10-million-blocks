using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Content;
using TenMillionBlocks.Economy;
using TenMillionBlocks.Mining;
using TenMillionBlocks.Skills;
using TenMillionBlocks.Tutorial;

namespace TenMillionBlocks.Diagnostics;

/// <summary>
/// Debug-only local telemetry for Phase Q pacing/balance playtests. It records the measurements called
/// out by the pacing plan without sending anything off-device: active session time, manual/automation
/// split, first automation timing, placements/stops/relocations, final resources and skill ranks.
/// It also records the longest stretch without an observable player decision/action so a playtest can
/// quickly flag the plan's "nothing meaningful to do for 1-2 minutes" failure mode. Reports are plain
/// text under user://pacing_reports. This node is a passive observer and never changes authoritative
/// gameplay state.
/// </summary>
public partial class PacingTelemetryRecorder : Node
{
    private WorldProfile _profile = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private MinerSimulationService _miners = null!;
    private SpecialResourceInventory _specialResources = null!;
    private GameplayEventHub _events = null!;

    private readonly Dictionary<long, Vector3I> _minerOrigins = new();
    private readonly Dictionary<GameplayEventKind, int> _semanticCounts = new();
    private readonly List<string> _skillTimeline = new();
    private readonly Dictionary<string, int> _lastRanks = new(StringComparer.Ordinal);

    private long _baselineManualBlocks;
    private long _baselineAutomatedBlocks;
    private long _sessionManualBlocks;
    private long _sessionAutomatedBlocks;
    private int _automationUnitsAtStart;
    private int _placements;
    private int _stops;
    private int _relocations;
    private int _maxAutomationUnits;
    private int _decisionEvents;
    private double _activeSeconds;
    private double _lastDecisionSeconds;
    private double _longestDecisionGapSeconds;
    private double _firstAutomationPlacementSeconds = -1.0;
    private bool _completionWritten;
    private bool _subscribed;

    public void Initialize(
        WorldProfile profile,
        MiningService mining,
        SkillTreeService skills,
        MinerSimulationService miners,
        SpecialResourceInventory specialResources,
        GameplayEventHub events,
        long baselineManualBlocks,
        long baselineAutomatedBlocks)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _miners = miners ?? throw new ArgumentNullException(nameof(miners));
        _specialResources = specialResources ?? throw new ArgumentNullException(nameof(specialResources));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _baselineManualBlocks = Math.Max(0L, baselineManualBlocks);
        _baselineAutomatedBlocks = Math.Max(0L, baselineAutomatedBlocks);

        SnapshotOrigins();
        SnapshotRanks(recordChanges: false);
        _automationUnitsAtStart = _miners.Miners.Count;
        _maxAutomationUnits = _automationUnitsAtStart;
    }

    public override void _Ready()
    {
        if (!OS.IsDebugBuild())
        {
            QueueFree();
            return;
        }

        Subscribe();
    }

    public override void _Process(double delta)
    {
        _activeSeconds += Math.Max(0.0, delta);
        _longestDecisionGapSeconds = Math.Max(_longestDecisionGapSeconds, _activeSeconds - _lastDecisionSeconds);
        _maxAutomationUnits = Math.Max(_maxAutomationUnits, _miners.Miners.Count);
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        Unsubscribe();

        if (!_completionWritten && HasMeaningfulActivity())
        {
            WriteReport("left_world", completed: false);
        }
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _mining.BlockMined += OnBlockMined;
        _mining.BulkMined += OnBulkMined;
        _skills.Changed += OnSkillsChanged;
        _miners.MinerPlaced += OnMinerPlaced;
        _miners.MinerStopped += OnMinerStopped;
        _miners.Changed += OnMinersChanged;
        _events.EventPublished += OnGameplayEvent;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        _mining.BlockMined -= OnBlockMined;
        _mining.BulkMined -= OnBulkMined;
        _skills.Changed -= OnSkillsChanged;
        _miners.MinerPlaced -= OnMinerPlaced;
        _miners.MinerStopped -= OnMinerStopped;
        _miners.Changed -= OnMinersChanged;
        _events.EventPublished -= OnGameplayEvent;
        _subscribed = false;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed) return;
        if (result.Source is MiningSource.Automated or MiningSource.Offline)
        {
            _sessionAutomatedBlocks++;
        }
        else if (result.Source == MiningSource.Manual)
        {
            _sessionManualBlocks++;
            MarkDecision();
        }
    }

    private void OnBulkMined(BulkMiningResult result)
    {
        if (!result.Success) return;
        if (result.Source is MiningSource.Automated or MiningSource.Offline)
        {
            _sessionAutomatedBlocks = checked(_sessionAutomatedBlocks + result.BlocksMined);
        }
        else if (result.Source == MiningSource.Manual)
        {
            MarkDecision();
        }
    }

    private void OnSkillsChanged()
    {
        SnapshotRanks(recordChanges: true);
        MarkDecision();
    }

    private void OnMinerPlaced(MinerInstance miner)
    {
        _placements++;
        _minerOrigins[miner.InstanceId] = miner.Origin;
        _maxAutomationUnits = Math.Max(_maxAutomationUnits, _miners.Miners.Count);
        if (_firstAutomationPlacementSeconds < 0.0)
        {
            _firstAutomationPlacementSeconds = _activeSeconds;
        }
        MarkDecision();
    }

    private void OnMinerStopped(MinerInstance _)
    {
        _stops++;
    }

    private void OnMinersChanged()
    {
        bool moved = false;
        // Origin is the route anchor and only changes when an existing physical unit is deliberately
        // relocated. Ordinary mining advances LastMinedVoxel/CandidateIndex, so this detects moves
        // without adding analytics hooks to the simulation itself.
        foreach (MinerInstance miner in _miners.Miners)
        {
            if (!_minerOrigins.TryGetValue(miner.InstanceId, out Vector3I previous))
            {
                _minerOrigins[miner.InstanceId] = miner.Origin;
                continue;
            }
            if (previous == miner.Origin) continue;
            _minerOrigins[miner.InstanceId] = miner.Origin;
            _relocations++;
            moved = true;
        }
        if (moved) MarkDecision();
        _maxAutomationUnits = Math.Max(_maxAutomationUnits, _miners.Miners.Count);
    }

    private void OnGameplayEvent(GameplayEvent gameplayEvent)
    {
        if (!string.Equals(gameplayEvent.WorldId, _profile.Id, StringComparison.Ordinal)) return;
        _semanticCounts[gameplayEvent.Kind] = _semanticCounts.GetValueOrDefault(gameplayEvent.Kind) + 1;

        // These semantic events represent deliberate active-system interactions that are not necessarily
        // visible through MiningService or SkillTreeService. Do not count passive spawn/stop events as a
        // player decision; otherwise automation waiting would artificially look interactive.
        if (gameplayEvent.Kind is GameplayEventKind.LightningCharged
            or GameplayEventKind.LightningImpact
            or GameplayEventKind.MeteorGrabbed
            or GameplayEventKind.MeteorImpact)
        {
            MarkDecision();
        }

        if (gameplayEvent.Kind == GameplayEventKind.WorldCompleted && !_completionWritten)
        {
            WriteReport("completed", completed: true);
            _completionWritten = true;
        }
    }

    private void MarkDecision()
    {
        double gap = Math.Max(0.0, _activeSeconds - _lastDecisionSeconds);
        _longestDecisionGapSeconds = Math.Max(_longestDecisionGapSeconds, gap);
        _lastDecisionSeconds = _activeSeconds;
        _decisionEvents++;
    }

    private void SnapshotOrigins()
    {
        _minerOrigins.Clear();
        foreach (MinerInstance miner in _miners.Miners)
        {
            _minerOrigins[miner.InstanceId] = miner.Origin;
        }
    }

    private void SnapshotRanks(bool recordChanges)
    {
        foreach ((string id, int rank) in _skills.Ranks)
        {
            int previous = _lastRanks.GetValueOrDefault(id);
            if (recordChanges && rank != previous)
            {
                _skillTimeline.Add(FormattableString.Invariant($"{_activeSeconds:0.00}s {id} {previous}->{rank}"));
            }
            _lastRanks[id] = rank;
        }
    }

    private bool HasMeaningfulActivity()
        => _activeSeconds >= 5.0
            || _sessionManualBlocks > 0
            || _sessionAutomatedBlocks > 0
            || _placements > 0
            || _relocations > 0
            || _skillTimeline.Count > 0;

    private void WriteReport(string reason, bool completed)
    {
        try
        {
            const string relativeDirectory = "user://pacing_reports";
            string directory = ProjectSettings.GlobalizePath(relativeDirectory);
            Directory.CreateDirectory(directory);

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            string fileName = $"{SafeFilePart(_profile.Id)}_{timestamp}_{reason}.txt";
            string path = Path.Combine(directory, fileName);

            long manualRun = checked(_baselineManualBlocks + _sessionManualBlocks);
            long automatedRun = checked(_baselineAutomatedBlocks + _sessionAutomatedBlocks);
            long accounted = checked(manualRun + automatedRun);
            long otherMined = Math.Max(0L, _mining.TotalMined - accounted);
            double currentGap = Math.Max(0.0, _activeSeconds - _lastDecisionSeconds);
            double longestGap = Math.Max(_longestDecisionGapSeconds, currentGap);

            var report = new StringBuilder(4096);
            report.AppendLine("10 Million Blocks pacing report");
            report.AppendLine("report_version=2");
            report.AppendLine($"reason={reason}");
            report.AppendLine($"completed={completed.ToString().ToLowerInvariant()}");
            report.AppendLine($"world={_profile.Id}");
            report.AppendLine($"world_name={Sanitize(_profile.DisplayName)}");
            report.AppendLine($"world_version={_profile.WorldVersion}");
            report.AppendLine($"generation_version={_profile.GenerationVersion}");
            report.AppendLine(FormattableString.Invariant($"active_session_seconds={_activeSeconds:0.00}"));
            report.AppendLine(FormattableString.Invariant($"longest_observed_decision_gap_seconds={longestGap:0.00}"));
            report.AppendLine($"decision_events_session={_decisionEvents}");
            report.AppendLine($"blocks_mined_total={_mining.TotalMined}");
            report.AppendLine($"blocks_manual_run={manualRun}");
            report.AppendLine($"blocks_automated_run={automatedRun}");
            report.AppendLine($"blocks_other_sources_run={otherMined}");
            report.AppendLine($"resources_end={_mining.Currency}");
            report.AppendLine($"automation_units_start={_automationUnitsAtStart}");
            report.AppendLine($"automation_units_end={_miners.Miners.Count}");
            report.AppendLine($"automation_units_max_session={_maxAutomationUnits}");
            report.AppendLine($"automation_placements_session={_placements}");
            report.AppendLine($"automation_stops_session={_stops}");
            report.AppendLine($"automation_relocations_session={_relocations}");
            report.AppendLine("first_automation_placement_seconds=" + FirstAutomationTiming());
            report.AppendLine("skills_end=" + SerializeRanks());
            report.AppendLine("special_resources_end=" + SerializeSpecialResources());
            report.AppendLine();

            report.AppendLine("[skill_timeline]");
            if (_skillTimeline.Count == 0) report.AppendLine("none");
            else foreach (string line in _skillTimeline) report.AppendLine(line);
            report.AppendLine();

            report.AppendLine("[semantic_event_counts]");
            foreach ((GameplayEventKind kind, int count) in _semanticCounts.OrderBy(pair => pair.Key))
            {
                report.AppendLine($"{kind}={count}");
            }
            report.AppendLine();
            report.AppendLine("note=decision gaps are an objective action-gap signal, not a claim that the player was bored; manual mining, skill changes, placements/relocations and active lightning/meteor interactions reset the gap.");
            report.AppendLine("note=active_session_seconds excludes time outside this loaded world session; combine partial reports when a run is revisited before completion.");
            report.AppendLine("note=use these reports to tune costs and decision density; do not infer balance from world size alone.");

            File.WriteAllText(path, report.ToString());
            GD.Print($"Pacing report saved: {path}");
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not write pacing telemetry: {exception.Message}");
        }
    }

    private string FirstAutomationTiming()
    {
        if (_automationUnitsAtStart > 0) return "already_present_at_session_start";
        return _firstAutomationPlacementSeconds < 0.0
            ? "none"
            : _firstAutomationPlacementSeconds.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private string SerializeRanks()
        => string.Join(
            ';',
            _skills.Ranks
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));

    private string SerializeSpecialResources()
        => string.Join(
            ';',
            _specialResources.Balances
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));

    private static string SafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace(' ', '_').ToLowerInvariant();
    }

    private static string Sanitize(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
}
