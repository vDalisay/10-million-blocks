using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using TenMillionBlocks.Automation;

namespace TenMillionBlocks.Diagnostics;

public partial class AutomationRampStressSuite
{
    private const double DiagnosticRelocationIntervalSeconds = 0.25;
    private const int DiagnosticRelocationBudgetPerPass = 6;
    private const int DiagnosticRelocationLocalRadius = 6;

    private DiagnosticRelocationWorker? _diagnosticRelocationWorker;
    private DiagnosticStatusOverlay? _diagnosticStatusOverlay;

    public override void _Ready()
    {
        _diagnosticRelocationWorker = new DiagnosticRelocationWorker(this)
        {
            Name = "AutomationStressRelocationWorker",
        };
        AddChild(_diagnosticRelocationWorker);

        _diagnosticStatusOverlay = new DiagnosticStatusOverlay(this)
        {
            Name = "AutomationStressStatusOverlay",
        };
        AddChild(_diagnosticStatusOverlay);
    }

    /// <summary>
    /// F11 is supposed to benchmark a growing active late-game fleet, not a growing collection of
    /// orange stopped-machine markers. This child runs only while the F11 suite is active. It moves a
    /// bounded number of stopped machines using the diagnostic nearest-compatible search in
    /// MinerSimulationService. Normal gameplay never invokes this recovery policy.
    ///
    /// Relocation cost is measured and appended to the same benchmark report so it can be separated
    /// from actual automation/rendering cost.
    /// </summary>
    private sealed partial class DiagnosticRelocationWorker : Node
    {
        private readonly AutomationRampStressSuite _owner;
        private readonly List<string> _timeline = new(96);

        private bool _wasRunning;
        private double _relocationAccumulator;
        private double _timelineAccumulator;
        private Vector3I? _lastActiveMiningFront;
        private long _passes;
        private long _attempted;
        private long _relocated;
        private long _localRelocations;
        private long _fallbackRelocations;
        private long _failed;
        private long _cooldownSkipped;
        private long _candidateChecks;
        private double _searchMilliseconds;
        private double _maxPassMilliseconds;
        private int _maxSearchRadius;
        private int _maxAttention;
        private int _lastAttention;

        public DiagnosticRelocationWorker(AutomationRampStressSuite owner)
        {
            _owner = owner;
        }

        public override void _Process(double delta)
        {
            double safeDelta = Math.Max(0.0, delta);
            if (_owner._running)
            {
                if (!_wasRunning)
                {
                    ResetRun();
                    _wasRunning = true;
                }

                if (_owner._manual?.HoveredVoxel is Vector3I activeFront)
                {
                    _lastActiveMiningFront = activeFront;
                }

                _relocationAccumulator += safeDelta;
                _timelineAccumulator += safeDelta;

                if (_relocationAccumulator >= DiagnosticRelocationIntervalSeconds)
                {
                    _relocationAccumulator = 0.0;
                    RunRelocationPass();
                }

                if (_timelineAccumulator >= 1.0)
                {
                    _timelineAccumulator -= 1.0;
                    CaptureTimeline();
                }
                return;
            }

            if (_wasRunning)
            {
                CaptureTimeline();
                AppendRelocationReport();
                _wasRunning = false;
            }
        }

        private void ResetRun()
        {
            _timeline.Clear();
            _relocationAccumulator = 0.0;
            _timelineAccumulator = 0.0;
            _lastActiveMiningFront = null;
            _passes = 0L;
            _attempted = 0L;
            _relocated = 0L;
            _localRelocations = 0L;
            _fallbackRelocations = 0L;
            _failed = 0L;
            _cooldownSkipped = 0L;
            _candidateChecks = 0L;
            _searchMilliseconds = 0.0;
            _maxPassMilliseconds = 0.0;
            _maxSearchRadius = 0;
            _maxAttention = 0;
            _lastAttention = 0;
        }

        private void RunRelocationPass()
        {
            MinerSimulationService? miners = _owner._miners;
            if (miners is null) return;

            int attentionBefore = miners.AttentionMinerCount;
            _maxAttention = Math.Max(_maxAttention, attentionBefore);
            _lastAttention = attentionBefore;
            if (attentionBefore <= 0) return;

            DiagnosticRelocationBatch batch = miners.RelocateStoppedDiagnosticMiners(
                DiagnosticRelocationBudgetPerPass,
                DiagnosticRelocationLocalRadius,
                _lastActiveMiningFront);

            _passes++;
            _attempted += batch.Attempted;
            _relocated += batch.Relocated;
            _localRelocations += batch.LocalRelocations;
            _fallbackRelocations += batch.FallbackRelocations;
            _failed += batch.Failed;
            _cooldownSkipped += batch.CooldownSkipped;
            _candidateChecks += batch.CandidateChecks;
            _searchMilliseconds += batch.SearchMilliseconds;
            _maxPassMilliseconds = Math.Max(_maxPassMilliseconds, batch.SearchMilliseconds);
            _maxSearchRadius = Math.Max(_maxSearchRadius, batch.MaximumSearchRadius);
            _lastAttention = miners.AttentionMinerCount;
            _maxAttention = Math.Max(_maxAttention, _lastAttention);
        }

        private void CaptureTimeline()
        {
            MinerSimulationService? miners = _owner._miners;
            int attention = miners?.AttentionMinerCount ?? _lastAttention;
            int units = miners?.Miners.Count ?? 0;
            _lastAttention = attention;
            _maxAttention = Math.Max(_maxAttention, attention);
            _timeline.Add(
                $"{_owner._elapsed:0.0}," +
                $"{units}," +
                $"{attention}," +
                $"{_relocated}," +
                $"{_failed}," +
                $"{_cooldownSkipped}," +
                $"{_candidateChecks}," +
                $"{_searchMilliseconds:0.000}");
        }

        private void AppendRelocationReport()
        {
            try
            {
                string path = ProjectSettings.GlobalizePath("user://automation_stress_benchmark_latest.txt");
                double averageAttemptMs = _attempted <= 0 ? 0.0 : _searchMilliseconds / _attempted;
                double successRate = _attempted <= 0 ? 100.0 : _relocated * 100.0 / _attempted;

                var report = new StringBuilder(8192);
                report.AppendLine();
                report.AppendLine("[stress_relocation]");
                report.AppendLine("relocation_extension_version=3");
                report.AppendLine($"interval_s={DiagnosticRelocationIntervalSeconds:0.00}");
                report.AppendLine($"budget_per_pass={DiagnosticRelocationBudgetPerPass}");
                report.AppendLine($"local_nearest_manhattan_radius={DiagnosticRelocationLocalRadius}");
                report.AppendLine($"active_mining_front_fallback_radius={DiagnosticRelocationLocalRadius + 2}");
                report.AppendLine("policy=nearest compatible exposed block; shovel relocation requires dirt-backed surface-edge block; fallback around active center-screen mining front");
                report.AppendLine("search_optimization=O(1) occupied-anchor lookup + material-first rejection before six-neighbour exposure + failed-search cooldown");
                report.AppendLine($"passes_with_attention={_passes}");
                report.AppendLine($"relocation_attempts={_attempted}");
                report.AppendLine($"relocation_successes={_relocated}");
                report.AppendLine($"relocation_success_rate_pct={successRate:0.00}");
                report.AppendLine($"relocation_local_successes={_localRelocations}");
                report.AppendLine($"relocation_active_front_fallback_successes={_fallbackRelocations}");
                report.AppendLine($"relocation_failures={_failed}");
                report.AppendLine($"relocation_cooldown_skips={_cooldownSkipped}");
                report.AppendLine($"relocation_candidate_checks={_candidateChecks}");
                report.AppendLine($"relocation_search_total_ms={_searchMilliseconds:0.000}");
                report.AppendLine($"relocation_search_avg_ms_per_attempt={averageAttemptMs:0.000}");
                report.AppendLine($"relocation_search_max_pass_ms={_maxPassMilliseconds:0.000}");
                report.AppendLine($"relocation_max_search_radius_used={_maxSearchRadius}");
                report.AppendLine($"attention_miners_max_seen_by_relocator={_maxAttention}");
                report.AppendLine($"attention_miners_end_seen_by_relocator={_lastAttention}");
                if (_owner._miners is MinerSimulationService activeMiners)
                {
                    report.AppendLine($"automation_visual_lod_hidden_end={activeMiners.VisualLodHiddenMinerCount}");
                    report.AppendLine($"automation_rotor_lod_hidden_end={activeMiners.RotorLodHiddenCount}");
                }
                report.AppendLine();
                report.AppendLine("[relocation_timeline_csv]");
                report.AppendLine("time_s,automation_units,attention_miners,relocations_total,relocation_failures_total,cooldown_skips_total,candidate_checks_total,relocation_search_ms_total");
                foreach (string line in _timeline)
                {
                    report.AppendLine(line);
                }

                System.IO.File.AppendAllText(path, report.ToString());
                GD.Print(
                    $"Automation stress relocation: {_relocated}/{_attempted} stopped-machine moves succeeded " +
                    $"({_localRelocations} local, {_fallbackRelocations} active-front fallback); " +
                    $"{_cooldownSkipped} cooldown skips; search cost {_searchMilliseconds:0.0} ms total. Metrics appended to benchmark report.");
            }
            catch (Exception exception)
            {
                GD.PushWarning($"Could not append F11 relocation diagnostics: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Visible test-state indicator for loading, running and completion. The benchmark timer is not
    /// allowed to start until WorldView reports its initial shell fully resolved, so the waiting state is
    /// shown explicitly instead of silently contaminating the measured run with initial chunk loading.
    /// </summary>
    private sealed partial class DiagnosticStatusOverlay : CanvasLayer
    {
        private const double CompletedVisibleSeconds = 6.0;
        private readonly AutomationRampStressSuite _owner;
        private PanelContainer? _panel;
        private Label? _label;
        private bool _wasRunning;
        private double _completedRemaining;

        public DiagnosticStatusOverlay(AutomationRampStressSuite owner)
        {
            _owner = owner;
            Layer = 200;
        }

        public override void _Ready()
        {
            _panel = new PanelContainer
            {
                AnchorLeft = 0.5f,
                AnchorRight = 0.5f,
                OffsetLeft = -300.0f,
                OffsetRight = 300.0f,
                OffsetTop = 18.0f,
                OffsetBottom = 62.0f,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            var box = new StyleBoxFlat
            {
                BgColor = new Color(0.03f, 0.04f, 0.06f, 0.90f),
                BorderColor = new Color(0.40f, 0.78f, 1.0f, 0.95f),
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
            };
            _panel.AddThemeStyleboxOverride("panel", box);
            AddChild(_panel);

            _label = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _label.AddThemeFontSizeOverride("font_size", 17);
            _panel.AddChild(_label);
        }

        public override void _Process(double delta)
        {
            if (_panel is null || _label is null) return;

            if (_owner._pendingStart)
            {
                _completedRemaining = 0.0;
                _panel.Visible = true;
                float progress = (_owner._view?.InitialPresentationProgress ?? 0.0f) * 100.0f;
                int pending = _owner._view?.PendingChunkLoads ?? 0;
                _label.Text =
                    $"F11 WAITING FOR FULL WORLD BASELINE   {progress:0.0}%   |   {pending} chunks pending";
                return;
            }

            if (_owner._running)
            {
                _wasRunning = true;
                _completedRemaining = 0.0;
                _panel.Visible = true;
                int units = _owner._miners?.Miners.Count ?? 0;
                int stopped = _owner._miners?.AttentionMinerCount ?? 0;
                _label.Text =
                    $"F11 STRESS TEST RUNNING   {_owner._elapsed:0.0} / {SuiteDurationSeconds:0}s   |   " +
                    $"{units} automations   |   {stopped} being recovered";
                return;
            }

            if (_wasRunning)
            {
                _wasRunning = false;
                _completedRemaining = CompletedVisibleSeconds;
                _panel.Visible = true;
                _label.Text = "F11 STRESS TEST ENDED   |   report saved to automation_stress_benchmark_latest.txt";
                return;
            }

            if (_completedRemaining <= 0.0)
            {
                _panel.Visible = false;
                return;
            }

            _completedRemaining = Math.Max(0.0, _completedRemaining - Math.Max(0.0, delta));
            if (_completedRemaining <= 0.0) _panel.Visible = false;
        }
    }
}
