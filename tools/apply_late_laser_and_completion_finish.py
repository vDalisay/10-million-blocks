#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding="utf-8")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise RuntimeError(f"Anchor not found in {path}: {old[:180]!r}")
    write(path, text.replace(old, new, 1))


# -----------------------------------------------------------------------------
# Finish the reviewed completion ceremony: compressed pickup settlement,
# authoritative-resource preview during suction, and debug exact-count profiling.
# -----------------------------------------------------------------------------
replace_once(
    "src/Collection/ResourceCollectionField.cs",
    "    public void CollectAllPending()\n    {\n        CollectPending(collectManual: true, collectAutomation: true);\n        System.Diagnostics.Debug.Assert(PendingCount == 0 && PendingAmount == 0, \"End-of-world collection must clear every pickup.\");\n    }\n",
    "    public void CollectAllPending()\n    {\n        CollectPending(collectManual: true, collectAutomation: true);\n        System.Diagnostics.Debug.Assert(PendingCount == 0 && PendingAmount == 0, \"End-of-world collection must clear every pickup.\");\n    }\n\n"
    "    /// <summary>\n"
    "    /// Completion-only settlement path. Pending ordinary rewards are credited once in one\n"
    "    /// currency transaction and all pickup presentation buckets are discarded as a single sweep.\n"
    "    /// This deliberately avoids emitting one HUD flight/event per pickup at the final block.\n"
    "    /// </summary>\n"
    "    public void ResolveAllForCompletion()\n"
    "    {\n"
    "        long amount = Math.Max(0L, _pendingAmount);\n"
    "        if (amount > 0) _mining.GrantCurrency(amount);\n\n"
    "        foreach (RenderBucket bucket in _buckets.Values)\n"
    "        {\n"
    "            bucket.Node.QueueFree();\n"
    "            bucket.OutlineNode.QueueFree();\n"
    "        }\n\n"
    "        _pickups.Clear();\n"
    "        _buckets.Clear();\n"
    "        _bucketsByCell.Clear();\n"
    "        _hoverCandidates.Clear();\n"
    "        _sweepIds.Clear();\n"
    "        _activeSpawnIds.Clear();\n"
    "        _suctionIds.Clear();\n"
    "        _coastingIds.Clear();\n"
    "        _pendingAmount = 0L;\n"
    "        NotifyPendingChanged();\n"
    "        System.Diagnostics.Debug.Assert(PendingCount == 0 && PendingAmount == 0, \"Completion settlement must leave no authoritative pickup behind.\");\n"
    "    }\n",
)

replace_once(
    "src/App/GameRoot.WorldCeremony.cs",
    "        if (_manualMining is not null) _manualMining.InputEnabled = enabled;\n",
    "        if (_manualMining is not null) _manualMining.InputEnabled = enabled;\n"
    "        if (_laser is not null) _laser.InputEnabled = enabled;\n",
)
replace_once(
    "src/App/GameRoot.WorldCeremony.cs",
    "        _resourceCollection?.CollectAllPending();\n",
    "        _resourceCollection?.ResolveAllForCompletion();\n",
)
replace_once(
    "src/App/GameRoot.WorldCeremony.cs",
    "            center,\n            _completionBonusResources,\n            scatterRadius);",
    "            center,\n            _completionBonusResources,\n            scatterRadius,\n            _mining.Currency);",
)

# WorldCompletionCeremony: add one presentation-only currency counter whose displayed value
# moves with suction progress; authoritative currency is still granted once by GameRoot.
replace_once(
    "src/Presentation/WorldCompletionCeremony.cs",
    "    private bool _reducedMotion;\n",
    "    private bool _reducedMotion;\n"
    "    private long _startingResources;\n"
    "    private CanvasLayer? _resourceLayer;\n"
    "    private Label? _resourceCounter;\n",
)
replace_once(
    "src/Presentation/WorldCompletionCeremony.cs",
    "        Vector3 center,\n        long bonusParticles,\n        float scatterRadius)\n",
    "        Vector3 center,\n        long bonusParticles,\n        float scatterRadius,\n        long startingResources = 0L)\n",
)
replace_once(
    "src/Presentation/WorldCompletionCeremony.cs",
    "        BonusParticleCount = Math.Max(0L, bonusParticles);\n        _reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;\n",
    "        BonusParticleCount = Math.Max(0L, bonusParticles);\n"
    "        _startingResources = Math.Max(0L, startingResources);\n"
    "        _reducedMotion = GraphicsSettingsRuntime.Current?.ReducedMotionEnabled == true;\n",
)
replace_once(
    "src/Presentation/WorldCompletionCeremony.cs",
    "        BuildBlackHole(profile.BlockSpacing);\n        BuildBonusParticles(profile, assets, scatterRadius);\n",
    "        BuildBlackHole(profile.BlockSpacing);\n"
    "        BuildBonusParticles(profile, assets, scatterRadius);\n"
    "        BuildResourceCounter();\n",
)
replace_once(
    "src/Presentation/WorldCompletionCeremony.cs",
    "        UpdateBlackHole(time, delta * speed);\n\n        WorldCompletionVisualStage next = time < ScatterStart\n",
    "        UpdateBlackHole(time, delta * speed);\n"
    "        UpdateResourceCounter(time);\n\n"
    "        WorldCompletionVisualStage next = time < ScatterStart\n",
)
insert_anchor = "    private void BuildImplosion(float spacing, float scatterRadius)\n"
resource_counter_code = r'''    private void BuildResourceCounter()
    {
        _resourceLayer = new CanvasLayer { Name = "CompletionResourceCounter", Layer = 36 };
        AddChild(_resourceLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -330.0f,
            OffsetTop = 24.0f,
            OffsetRight = -24.0f,
            OffsetBottom = 106.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _resourceLayer.AddChild(panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        _resourceCounter = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _resourceCounter.AddThemeFontSizeOverride("font_size", 17);
        margin.AddChild(_resourceCounter);
        UpdateResourceCounter(0.0f);
    }

    private void UpdateResourceCounter(float time)
    {
        if (_resourceCounter is null) return;
        float suction = Mathf.Clamp((time - SuctionStart) / Math.Max(0.001f, FinishAt - SuctionStart), 0.0f, 1.0f);
        long visualBonus = Math.Clamp((long)Math.Floor(BonusParticleCount * suction), 0L, BonusParticleCount);
        long displayed = checked(_startingResources + visualBonus);
        _resourceCounter.Text = suction <= 0.0f
            ? $"BLACK HOLE BONUS  +{BonusParticleCount:N0}\nRESOURCES  {_startingResources:N0}"
            : $"ABSORBING  +{visualBonus:N0} / +{BonusParticleCount:N0}\nRESOURCES  {displayed:N0}";
    }

'''
text = read("src/Presentation/WorldCompletionCeremony.cs")
if insert_anchor not in text:
    raise RuntimeError("Completion ceremony insertion anchor missing")
write("src/Presentation/WorldCompletionCeremony.cs", text.replace(insert_anchor, resource_counter_code + insert_anchor, 1))

# -----------------------------------------------------------------------------
# Late-game laser derived stats + data branch.
# Base contract from request: 1 block-damage/sec, 5 sec charged burst, 60 sec cooldown;
# manual clicks charge materially faster than Hover Mining auto-actions.
# -----------------------------------------------------------------------------
replace_once(
    "src/Skills/SkillTreeService.cs",
    "    public int ManualPenetrationDepth { get; internal set; } = 1;\n\n    public double CollectionRadiusBlocks",
    "    public int ManualPenetrationDepth { get; internal set; } = 1;\n\n"
    "    public bool LaserUnlocked { get; internal set; }\n"
    "    public double LaserManualChargePerAction { get; internal set; } = 0.0125;\n"
    "    public double LaserAutoChargePerAction { get; internal set; } = 0.0030;\n"
    "    public double LaserDamagePerSecond { get; internal set; } = 1.0;\n"
    "    public int LaserBeamRadius { get; internal set; } = 1;\n"
    "    public double LaserDurationSeconds { get; internal set; } = 5.0;\n"
    "    public double LaserCooldownSeconds { get; internal set; } = 60.0;\n"
    "    public bool LaserResourceBurnUnlocked { get; internal set; }\n"
    "    public double LaserResourceCostPerSecond { get; internal set; } = 300.0;\n\n"
    "    public double CollectionRadiusBlocks",
)
replace_once(
    "src/Skills/SkillTreeService.cs",
    "            case \"unlock_hover_mining\": stats.HoverMiningUnlocked = true; break;\n",
    "            case \"unlock_hover_mining\": stats.HoverMiningUnlocked = true; break;\n"
    "            case \"unlock_laser\": stats.LaserUnlocked = true; break;\n"
    "            case \"multiply_laser_manual_charge_rate\": stats.LaserManualChargePerAction *= Math.Max(0.01, effect.Value); break;\n"
    "            case \"multiply_laser_auto_charge_rate\": stats.LaserAutoChargePerAction *= Math.Max(0.01, effect.Value); break;\n"
    "            case \"multiply_laser_damage\": stats.LaserDamagePerSecond *= Math.Max(0.01, effect.Value); break;\n"
    "            case \"set_laser_beam_radius\": stats.LaserBeamRadius = Math.Max(stats.LaserBeamRadius, Math.Max(1, (int)Math.Round(effect.Value))); break;\n"
    "            case \"set_laser_duration_seconds\": stats.LaserDurationSeconds = Math.Max(stats.LaserDurationSeconds, Math.Max(0.5, effect.Value)); break;\n"
    "            case \"set_laser_cooldown_seconds\": stats.LaserCooldownSeconds = Math.Min(stats.LaserCooldownSeconds, Math.Max(1.0, effect.Value)); break;\n"
    "            case \"unlock_laser_resource_burn\": stats.LaserResourceBurnUnlocked = true; break;\n"
    "            case \"set_laser_resource_cost_per_second\": stats.LaserResourceCostPerSecond = Math.Max(1.0, effect.Value); break;\n"
    "            case \"multiply_laser_resource_cost\": stats.LaserResourceCostPerSecond *= Math.Clamp(effect.Value, 0.05, 10.0); break;\n",
)

replace_once(
    "tools/validate_content.py",
    "    \"unlock_hover_mining\",\n",
    "    \"unlock_hover_mining\",\n"
    "    \"unlock_laser\",\n"
    "    \"multiply_laser_manual_charge_rate\",\n"
    "    \"multiply_laser_auto_charge_rate\",\n"
    "    \"multiply_laser_damage\",\n"
    "    \"set_laser_beam_radius\",\n"
    "    \"set_laser_duration_seconds\",\n"
    "    \"set_laser_cooldown_seconds\",\n"
    "    \"unlock_laser_resource_burn\",\n"
    "    \"set_laser_resource_cost_per_second\",\n"
    "    \"multiply_laser_resource_cost\",\n",
)

skills_path = ROOT / "data/skills/skill_tree.json"
skills_doc = json.loads(skills_path.read_text(encoding="utf-8"))
nodes = skills_doc["nodes"]
by_id = {node["id"]: node for node in nodes}
if "laser_core" not in by_id:
    for required in ("manual_aftershock", "orb_breaker_swarm"):
        if required not in by_id:
            raise RuntimeError(f"Late laser prerequisite missing: {required}")
    max_x = max(int(node.get("grid_x", 0)) for node in nodes)
    base_x = max_x + 2
    base_y = max(int(by_id["manual_aftershock"].get("grid_y", 0)), int(by_id["orb_breaker_swarm"].get("grid_y", 0)))

    def prereq(*ids: str):
        return [{"node_id": ident, "required_rank": 1} for ident in ids]

    laser_nodes = [
        {
            "id": "laser_core", "display_name": "Flux Laser",
            "description": "Very-late active/idle capstone. Manual clicks and Hover Mining charge a capacitor; at full charge a wide cursor laser fires automatically for 5 seconds at 1.0 block-damage per second, then locks into a 60-second cooldown.",
            "grid_x": base_x, "grid_y": base_y, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("manual_aftershock", "orb_breaker_swarm"), "hide_until_prerequisites_met": True,
            "cost": 18000, "max_rank": 1, "effects": [{"type": "unlock_laser"}],
        },
        {
            "id": "laser_capacitor_1", "display_name": "Click Capacitor",
            "description": "Active clicks feed 50% more charge into the Flux Laser. The branch keeps active play materially faster than idle charging.",
            "grid_x": base_x - 2, "grid_y": base_y + 2, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_core"), "hide_until_prerequisites_met": True,
            "cost": 22000, "max_rank": 1, "effects": [{"type": "multiply_laser_manual_charge_rate", "value": 1.5}],
        },
        {
            "id": "laser_auto_coupler", "display_name": "Auto Flux Coupler",
            "description": "Hover Mining auto-actions feed the capacitor twice as efficiently. Idle play still charges the laser, but remains slower than deliberate clicking.",
            "grid_x": base_x + 2, "grid_y": base_y + 2, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_core"), "hide_until_prerequisites_met": True,
            "cost": 28000, "max_rank": 1, "effects": [{"type": "multiply_laser_auto_charge_rate", "value": 2.0}],
        },
        {
            "id": "laser_wide_lens", "display_name": "Wide Lens",
            "description": "Increase the beam footprint from a 3x3 face to a 5x5 face, borrowing the classic incremental laser upgrade of making the lens itself bigger rather than only multiplying damage.",
            "grid_x": base_x, "grid_y": base_y + 3, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_core"), "hide_until_prerequisites_met": True,
            "cost": 30000, "max_rank": 1, "effects": [{"type": "set_laser_beam_radius", "value": 2.0}],
        },
        {
            "id": "laser_cooling", "display_name": "Cryo Radiator",
            "description": "Reduce the post-burst lockout from 60 seconds to 50 seconds. Cooldown reduction is deliberately late so the first laser still has a strong rhythm.",
            "grid_x": base_x - 2, "grid_y": base_y + 5, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_capacitor_1", "laser_auto_coupler"), "hide_until_prerequisites_met": True,
            "cost": 35000, "max_rank": 1, "effects": [{"type": "set_laser_cooldown_seconds", "value": 50.0}],
        },
        {
            "id": "laser_hotter_beam", "display_name": "Hotter Beam",
            "description": "Raise beam damage from 1.0 to 1.5 block-damage per second. This is a true hardness interaction, not an instant-delete exception.",
            "grid_x": base_x + 2, "grid_y": base_y + 5, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_wide_lens"), "hide_until_prerequisites_met": True,
            "cost": 42000, "max_rank": 1, "effects": [{"type": "multiply_laser_damage", "value": 1.5}],
        },
        {
            "id": "laser_duration", "display_name": "Extended Burn",
            "description": "Increase the natural charged burst from 5 seconds to 7 seconds after both cooling and beam-power investment.",
            "grid_x": base_x, "grid_y": base_y + 7, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_cooling", "laser_hotter_beam"), "hide_until_prerequisites_met": True,
            "cost": 50000, "max_rank": 1, "effects": [{"type": "set_laser_duration_seconds", "value": 7.0}],
        },
        {
            "id": "laser_resource_furnace", "display_name": "Resource Furnace",
            "description": "Unlock optional OVERBURN. After the free charged burst empties, keep the laser alive by literally burning ordinary resources at 300 per second until disabled or unaffordable; cooldown starts when the burn stops.",
            "grid_x": base_x, "grid_y": base_y + 9, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_duration", "laser_auto_coupler"), "hide_until_prerequisites_met": True,
            "cost": 75000, "max_rank": 1,
            "effects": [{"type": "unlock_laser_resource_burn"}, {"type": "set_laser_resource_cost_per_second", "value": 300.0}],
        },
        {
            "id": "laser_furnace_efficiency", "display_name": "Closed-Loop Furnace",
            "description": "Late resource-sink refinement: overburn consumes 40% fewer ordinary resources while preserving the same beam output.",
            "grid_x": base_x, "grid_y": base_y + 11, "category": "manual", "purchase_mode": "once",
            "prerequisites": prereq("laser_resource_furnace"), "hide_until_prerequisites_met": True,
            "cost": 90000, "max_rank": 1, "effects": [{"type": "multiply_laser_resource_cost", "value": 0.6}],
        },
    ]
    nodes.extend(laser_nodes)
    skills_doc["content_version"] = int(skills_doc.get("content_version", 0)) + 1
    skills_path.write_text(json.dumps(skills_doc, indent=2) + "\n", encoding="utf-8")

# -----------------------------------------------------------------------------
# Persistent laser cycle state. Additive save fields deliberately do not require a schema bump.
# -----------------------------------------------------------------------------
replace_once(
    "src/Save/SaveService.cs",
    "    public bool HoverMiningEnabled { get; set; }\n    public bool Completed { get; set; }",
    "    public bool HoverMiningEnabled { get; set; }\n"
    "    public double LaserCharge { get; set; }\n"
    "    public double LaserCooldownSeconds { get; set; }\n"
    "    public double LaserActiveSeconds { get; set; }\n"
    "    public bool LaserResourceBurnEnabled { get; set; }\n"
    "    public bool Completed { get; set; }",
)

# GameRoot owns one world-bound laser controller and persists the capacitor/cooldown cycle.
replace_once(
    "src/App/GameRoot.cs",
    "    private ManualMiningController? _manualMining;\n    private MinerSimulationService? _miners;",
    "    private ManualMiningController? _manualMining;\n"
    "    private LaserMiningController? _laser;\n"
    "    private MinerSimulationService? _miners;",
)
replace_once(
    "src/App/GameRoot.cs",
    "        _sessionRoot.AddChild(_manualMining);\n\n        _resourceCollection = new ResourceCollectionField",
    "        _sessionRoot.AddChild(_manualMining);\n\n"
    "        _laser = new LaserMiningController { Name = \"FluxLaser\" };\n"
    "        _laser.Initialize(_world, _camera, _worldView, _mining, _skills, _manualMining);\n"
    "        if (savedWorld is not null)\n"
    "        {\n"
    "            _laser.RestoreState(\n"
    "                savedWorld.LaserCharge,\n"
    "                savedWorld.LaserCooldownSeconds,\n"
    "                savedWorld.LaserActiveSeconds,\n"
    "                savedWorld.LaserResourceBurnEnabled);\n"
    "        }\n"
    "        _laser.StateChanged += MarkAutosaveDirty;\n"
    "        _sessionRoot.AddChild(_laser);\n\n"
    "        _resourceCollection = new ResourceCollectionField",
)
replace_once(
    "src/App/GameRoot.cs",
    "        _manualMining = null;\n        _resourceCollection = null;",
    "        _manualMining = null;\n"
    "        _laser = null;\n"
    "        _resourceCollection = null;",
)
replace_once(
    "src/App/GameRoot.cs",
    "            HoverMiningEnabled = _manualMining.HoverMiningEnabled,\n            Completed = completed,",
    "            HoverMiningEnabled = _manualMining.HoverMiningEnabled,\n"
    "            LaserCharge = _laser?.Charge ?? previous?.LaserCharge ?? 0.0,\n"
    "            LaserCooldownSeconds = _laser?.CooldownRemainingForSave ?? previous?.LaserCooldownSeconds ?? 0.0,\n"
    "            LaserActiveSeconds = _laser?.ActiveRemainingForSave ?? previous?.LaserActiveSeconds ?? 0.0,\n"
    "            LaserResourceBurnEnabled = _laser?.ResourceBurnEnabled ?? previous?.LaserResourceBurnEnabled ?? false,\n"
    "            Completed = completed,",
)
replace_once(
    "src/App/GameRoot.cs",
    "        var harness = new ReferenceVisualHarness { Name = \"ReferenceVisualHarness\" };\n        harness.Initialize(_camera);\n        AddChild(harness);\n\n        _completionView",
    "        var harness = new ReferenceVisualHarness { Name = \"ReferenceVisualHarness\" };\n"
    "        harness.Initialize(_camera);\n"
    "        AddChild(harness);\n\n"
    "        var completionBenchmark = new CompletionParticleBenchmark { Name = \"CompletionParticleBenchmark\" };\n"
    "        completionBenchmark.Initialize(_assets, _camera, () => _world);\n"
    "        AddChild(completionBenchmark);\n\n"
    "        _completionView",
)

# -----------------------------------------------------------------------------
# New laser runtime controller.
# -----------------------------------------------------------------------------
write("src/Mining/LaserMiningController.cs", r'''using System;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.Skills;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Interaction;
using TenMillionBlocks.World.Rendering;

namespace TenMillionBlocks.Mining;

/// <summary>
/// Late-game active/idle bridge. One capacitor is charged by player clicks and Hover Mining auto-actions.
/// At full charge it automatically fires a wide cursor-tracked beam, then enters a hard cooldown. The
/// optional Resource Furnace can extend a natural burst by spending ordinary currency, making the laser
/// a deliberate late resource sink without changing the free charge/cooldown loop.
/// </summary>
public partial class LaserMiningController : Node3D
{
    private const double DamageTickSeconds = 0.10;
    private const int MaxDamageTicksPerFrame = 8;

    private VirtualWorld _world = null!;
    private OrbitCameraController _camera = null!;
    private WorldView _view = null!;
    private MiningService _mining = null!;
    private SkillTreeService _skills = null!;
    private ManualMiningController _manual = null!;

    private double _charge;
    private double _cooldownRemaining;
    private double _activeRemaining;
    private double _damageAccumulator;
    private double _resourceBurnAccumulator;
    private bool _overburning;
    private bool _resourceBurnEnabled;
    private ulong _lastPhysicalClickFrame = ulong.MaxValue;
    private ulong _lastAutoChargeFrame = ulong.MaxValue;

    private MeshInstance3D _beam = null!;
    private MeshInstance3D _beamGlow = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _detail = null!;
    private ProgressBar _bar = null!;
    private Button _burnToggle = null!;

    public event Action? StateChanged;

    public bool InputEnabled { get; set; } = true;
    public double Charge => Math.Clamp(_charge, 0.0, 1.0);
    public double CooldownRemainingForSave
        => _overburning ? Math.Max(1.0, _skills.Derived.LaserCooldownSeconds) : Math.Max(0.0, _cooldownRemaining);
    public double ActiveRemainingForSave => _overburning ? 0.0 : Math.Max(0.0, _activeRemaining);
    public bool ResourceBurnEnabled => _resourceBurnEnabled;

    public void Initialize(
        VirtualWorld world,
        OrbitCameraController camera,
        WorldView view,
        MiningService mining,
        SkillTreeService skills,
        ManualMiningController manual)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _manual = manual ?? throw new ArgumentNullException(nameof(manual));

        BuildBeam();
        BuildHud();
        _mining.BlockMined += OnManualMiningObserved;
        _mining.BlockDamaged += OnManualMiningObserved;
        _skills.Changed += OnSkillsChanged;
        RefreshHud();
    }

    public override void _ExitTree()
    {
        if (_mining is not null)
        {
            _mining.BlockMined -= OnManualMiningObserved;
            _mining.BlockDamaged -= OnManualMiningObserved;
        }
        if (_skills is not null) _skills.Changed -= OnSkillsChanged;
    }

    public void RestoreState(double charge, double cooldownSeconds, double activeSeconds, bool resourceBurnEnabled)
    {
        _charge = Math.Clamp(charge, 0.0, 1.0);
        _cooldownRemaining = Math.Clamp(cooldownSeconds, 0.0, Math.Max(60.0, _skills.Derived.LaserCooldownSeconds));
        _activeRemaining = Math.Clamp(activeSeconds, 0.0, Math.Max(5.0, _skills.Derived.LaserDurationSeconds));
        _resourceBurnEnabled = resourceBurnEnabled && _skills.Derived.LaserResourceBurnUnlocked;
        _overburning = false;
        if (_activeRemaining > 0.0) _cooldownRemaining = 0.0;
        else if (_cooldownRemaining > 0.0) _charge = 0.0;
        RefreshHud();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button
            || button.ButtonIndex != MouseButton.Left
            || !button.Pressed
            || !CanCharge()
            || _manual.HoveredVoxel is null
            || _manual.PlacementMode
            || _camera.IsManipulating)
        {
            return;
        }

        _lastPhysicalClickFrame = Engine.GetProcessFrames();
        AddCharge(_skills.Derived.LaserManualChargePerAction);
    }

    public override void _Process(double delta)
    {
        double dt = Math.Max(0.0, delta);
        bool unlocked = _skills.Derived.LaserUnlocked;
        _panel.Visible = unlocked;
        if (!unlocked)
        {
            HideBeam();
            return;
        }

        if (!InputEnabled || !_manual.InputEnabled)
        {
            HideBeam();
            RefreshHud();
            return;
        }

        if (_cooldownRemaining > 0.0)
        {
            double before = _cooldownRemaining;
            _cooldownRemaining = Math.Max(0.0, _cooldownRemaining - dt);
            HideBeam();
            if (before > 0.0 && _cooldownRemaining <= 0.0) StateChanged?.Invoke();
            RefreshHud();
            return;
        }

        if (_activeRemaining > 0.0)
        {
            _activeRemaining = Math.Max(0.0, _activeRemaining - dt);
            FireLaser(dt);
            if (_activeRemaining <= 0.0)
            {
                if (_skills.Derived.LaserResourceBurnUnlocked && _resourceBurnEnabled && _mining.Currency > 0)
                {
                    _overburning = true;
                    _resourceBurnAccumulator = 0.0;
                    StateChanged?.Invoke();
                }
                else
                {
                    BeginCooldown();
                }
            }
            RefreshHud();
            return;
        }

        if (_overburning)
        {
            if (!_skills.Derived.LaserResourceBurnUnlocked
                || !_resourceBurnEnabled
                || !PayForOverburn(dt))
            {
                _overburning = false;
                BeginCooldown();
                HideBeam();
                RefreshHud();
                return;
            }

            FireLaser(dt);
            RefreshHud();
            return;
        }

        if (_charge >= 1.0 && TryResolveTarget(out _, out _, out _))
        {
            StartBurst();
        }
        else
        {
            HideBeam();
        }
        RefreshHud();
    }

    private bool CanCharge()
        => _skills.Derived.LaserUnlocked
           && InputEnabled
           && _manual.InputEnabled
           && _cooldownRemaining <= 0.0
           && _activeRemaining <= 0.0
           && !_overburning
           && _charge < 1.0;

    private void OnManualMiningObserved(MiningResult result)
    {
        if (!result.Success || result.Source != MiningSource.Manual || !CanCharge()) return;
        ulong frame = Engine.GetProcessFrames();
        if (frame == _lastPhysicalClickFrame || frame == _lastAutoChargeFrame) return;
        _lastAutoChargeFrame = frame;
        AddCharge(_skills.Derived.LaserAutoChargePerAction);
    }

    private void AddCharge(double amount)
    {
        if (amount <= 0.0 || !CanCharge()) return;
        double before = _charge;
        _charge = Math.Clamp(_charge + amount, 0.0, 1.0);
        if (_charge > before) StateChanged?.Invoke();
        RefreshHud();
    }

    private void StartBurst()
    {
        _charge = 1.0;
        _activeRemaining = Math.Max(0.5, _skills.Derived.LaserDurationSeconds);
        _damageAccumulator = 0.0;
        _resourceBurnAccumulator = 0.0;
        _overburning = false;
        StateChanged?.Invoke();
    }

    private void BeginCooldown()
    {
        _charge = 0.0;
        _activeRemaining = 0.0;
        _damageAccumulator = 0.0;
        _resourceBurnAccumulator = 0.0;
        _cooldownRemaining = Math.Max(1.0, _skills.Derived.LaserCooldownSeconds);
        StateChanged?.Invoke();
    }

    private bool PayForOverburn(double delta)
    {
        double perSecond = Math.Max(1.0, _skills.Derived.LaserResourceCostPerSecond);
        _resourceBurnAccumulator += perSecond * Math.Max(0.0, delta);
        long due = Math.Max(0L, (long)Math.Floor(_resourceBurnAccumulator));
        if (due <= 0) return true;
        if (!_mining.TrySpend(due)) return false;
        _resourceBurnAccumulator -= due;
        return true;
    }

    private void FireLaser(double delta)
    {
        if (!TryResolveTarget(out Vector3I center, out Vector3I normal, out Vector3 hitPoint))
        {
            HideBeam();
            return;
        }

        ShowBeam(hitPoint, normal);
        _damageAccumulator += Math.Max(0.0, delta);
        int ticks = 0;
        while (_damageAccumulator >= DamageTickSeconds && ticks < MaxDamageTicksPerFrame)
        {
            _damageAccumulator -= DamageTickSeconds;
            ApplyDamageTick(center, normal, DamageTickSeconds);
            ticks++;
        }
        if (ticks >= MaxDamageTicksPerFrame) _damageAccumulator = Math.Min(_damageAccumulator, DamageTickSeconds);
    }

    private void ApplyDamageTick(Vector3I center, Vector3I normal, double tickSeconds)
    {
        ManualMiningFootprintKind footprint = _skills.Derived.LaserBeamRadius >= 2
            ? ManualMiningFootprintKind.Square5
            : ManualMiningFootprintKind.Square3;
        var targets = ManualMiningFootprint.ResolveFromCenter(_world, center, footprint, normal);
        double damage = Math.Max(0.01, _skills.Derived.LaserDamagePerSecond * tickSeconds);
        int presentation = 0;

        _mining.BeginCurrencyNotificationBatch();
        try
        {
            foreach (Vector3I target in targets)
            {
                MiningResult result = _mining.TryMineManual(target, damage);
                if (!result.Success || !result.Removed) continue;
                _view.MarkDirtyVoxel(result.Voxel);
                if (presentation++ < 3)
                    _view.SpawnManualMinePop(result.Voxel, result.BlockId, 1.36f);
            }
        }
        finally
        {
            _mining.EndCurrencyNotificationBatch();
        }
    }

    private bool TryResolveTarget(out Vector3I voxel, out Vector3I normal, out Vector3 point)
    {
        Camera3D camera = _camera.Camera;
        Vector2 mouse = GetViewport().GetMousePosition();
        float maxDistance = _world.GetWorldBounds().Size.Length() * 2.5f;
        if (VoxelRaycaster.TryRaycast(_world, camera, mouse, maxDistance, out voxel, out normal))
        {
            float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
            point = (Vector3)voxel * spacing + (Vector3)normal * (spacing * 0.54f);
            return true;
        }

        voxel = default;
        normal = default;
        point = default;
        return false;
    }

    private void BuildBeam()
    {
        var coreMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.96f, 1.0f, 0.92f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.36f, 0.90f, 1.0f),
            EmissionEnergyMultiplier = 7.0f,
        };
        var glowMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.72f, 1.0f, 0.20f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.12f, 0.60f, 1.0f),
            EmissionEnergyMultiplier = 3.5f,
        };

        _beamGlow = new MeshInstance3D
        {
            Name = "FluxLaserGlow",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = glowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_beamGlow);

        _beam = new MeshInstance3D
        {
            Name = "FluxLaserCore",
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = coreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_beam);
    }

    private void ShowBeam(Vector3 target, Vector3I surfaceNormal)
    {
        Camera3D camera = _camera.Camera;
        Vector3 start = camera.GlobalPosition + (-camera.GlobalBasis.Z.Normalized()) * 0.35f;
        Vector3 end = target;
        Vector3 delta = end - start;
        float length = Math.Max(0.05f, delta.Length());
        Vector3 middle = start + delta * 0.5f;
        float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
        float width = spacing * (0.055f + 0.012f * Math.Max(1, _skills.Derived.LaserBeamRadius));
        Vector3 up = camera.GlobalBasis.Y.Normalized();
        if (MathF.Abs(delta.Normalized().Dot(up)) > 0.985f) up = camera.GlobalBasis.X.Normalized();

        PositionBeam(_beamGlow, middle, end, up, width * 2.8f, length);
        PositionBeam(_beam, middle, end, up, width, length);
    }

    private static void PositionBeam(MeshInstance3D beam, Vector3 middle, Vector3 target, Vector3 up, float width, float length)
    {
        beam.Visible = true;
        beam.GlobalPosition = middle;
        beam.LookAt(target, up);
        beam.Scale = new Vector3(width, width, length);
    }

    private void HideBeam()
    {
        if (_beam is not null) _beam.Visible = false;
        if (_beamGlow is not null) _beamGlow.Visible = false;
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "FluxLaserHud", Layer = 25 };
        AddChild(layer);
        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -230.0f,
            OffsetTop = 18.0f,
            OffsetRight = 230.0f,
            OffsetBottom = 116.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        layer.AddChild(_panel);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _panel.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 3);
        margin.AddChild(column);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _title.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(_title);

        _bar = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 100.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0.0f, 12.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(_bar);

        var row = new HBoxContainer();
        _detail = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _detail.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(_detail);

        _burnToggle = new Button
        {
            Text = "OVERBURN OFF",
            CustomMinimumSize = new Vector2(116.0f, 28.0f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _burnToggle.Pressed += ToggleResourceBurn;
        row.AddChild(_burnToggle);
        column.AddChild(row);
    }

    private void ToggleResourceBurn()
    {
        if (!_skills.Derived.LaserResourceBurnUnlocked) return;
        _resourceBurnEnabled = !_resourceBurnEnabled;
        if (!_resourceBurnEnabled && _overburning)
        {
            _overburning = false;
            BeginCooldown();
        }
        StateChanged?.Invoke();
        RefreshHud();
    }

    private void OnSkillsChanged()
    {
        if (!_skills.Derived.LaserResourceBurnUnlocked)
        {
            _resourceBurnEnabled = false;
            if (_overburning)
            {
                _overburning = false;
                BeginCooldown();
            }
        }
        RefreshHud();
    }

    private void RefreshHud()
    {
        if (_panel is null || _skills is null) return;
        bool unlocked = _skills.Derived.LaserUnlocked;
        _panel.Visible = unlocked;
        if (!unlocked) return;

        _burnToggle.Visible = _skills.Derived.LaserResourceBurnUnlocked;
        _burnToggle.Text = _resourceBurnEnabled ? "OVERBURN ON" : "OVERBURN OFF";

        if (_cooldownRemaining > 0.0)
        {
            double total = Math.Max(1.0, _skills.Derived.LaserCooldownSeconds);
            _bar.Value = Math.Clamp((1.0 - _cooldownRemaining / total) * 100.0, 0.0, 100.0);
            _title.Text = $"FLUX LASER // COOLDOWN  {Math.Ceiling(_cooldownRemaining):0}s";
            _detail.Text = "Capacitor locked until radiator cycle completes";
            return;
        }

        if (_overburning)
        {
            _bar.Value = 100.0;
            _title.Text = "FLUX LASER // RESOURCE OVERBURN";
            _detail.Text = $"{_skills.Derived.LaserDamagePerSecond:0.##} dmg/s  |  burn {_skills.Derived.LaserResourceCostPerSecond:N0} resources/s";
            return;
        }

        if (_activeRemaining > 0.0)
        {
            double duration = Math.Max(0.5, _skills.Derived.LaserDurationSeconds);
            _bar.Value = Math.Clamp(_activeRemaining / duration * 100.0, 0.0, 100.0);
            int width = _skills.Derived.LaserBeamRadius * 2 + 1;
            _title.Text = $"FLUX LASER // ACTIVE  {_activeRemaining:0.0}s";
            _detail.Text = $"{width}x{width} beam  |  {_skills.Derived.LaserDamagePerSecond:0.##} block-damage/s";
            return;
        }

        _bar.Value = Math.Clamp(_charge * 100.0, 0.0, 100.0);
        if (_charge >= 1.0)
        {
            _title.Text = "FLUX LASER // PRIMED";
            _detail.Text = "Aim at a mineable surface to fire automatically";
        }
        else
        {
            _title.Text = $"FLUX LASER // CHARGING  {_charge * 100.0:0}%";
            _detail.Text = $"click +{_skills.Derived.LaserManualChargePerAction * 100.0:0.##}%  |  hover auto +{_skills.Derived.LaserAutoChargePerAction * 100.0:0.##}%";
        }
    }
}
''')

# -----------------------------------------------------------------------------
# Debug exact-count completion particle benchmark. F6 cycles the required plan cases.
# -----------------------------------------------------------------------------
write("src/Diagnostics/CompletionParticleBenchmark.cs", r'''using System;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Diagnostics;

/// <summary>
/// Renderer-side exact-count completion benchmark. Debug F6 cycles through the reviewed cases without
/// requiring a real world clear. No reward/progression mutation is attached to the ceremony instance.
/// </summary>
public partial class CompletionParticleBenchmark : Node
{
    private static readonly long[] Cases = [25L, 6_824L, 61_225L, 123_412L, 1_000_000L];

    private BlockAssetRegistry _assets = null!;
    private OrbitCameraController _camera = null!;
    private Func<VirtualWorld?> _worldProvider = null!;
    private WorldCompletionCeremony? _ceremony;
    private int _caseIndex = -1;
    private long _activeCount;
    private double _elapsed;
    private double _peakImplosionMs;
    private double _peakScatterMs;
    private double _peakSuctionMs;
    private WorldCompletionVisualStage _stage = WorldCompletionVisualStage.Implosion;

    public void Initialize(BlockAssetRegistry assets, OrbitCameraController camera, Func<VirtualWorld?> worldProvider)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _worldProvider = worldProvider ?? throw new ArgumentNullException(nameof(worldProvider));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!OS.IsDebugBuild() || @event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.F6)
            return;

        StartNextCase();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_ceremony is null) return;
        _elapsed += Math.Max(0.0, delta);
        double ms = Math.Max(0.0, delta) * 1000.0;
        switch (_stage)
        {
            case WorldCompletionVisualStage.Implosion: _peakImplosionMs = Math.Max(_peakImplosionMs, ms); break;
            case WorldCompletionVisualStage.BonusScatter: _peakScatterMs = Math.Max(_peakScatterMs, ms); break;
            case WorldCompletionVisualStage.BlackHoleSuction: _peakSuctionMs = Math.Max(_peakSuctionMs, ms); break;
        }
    }

    private void StartNextCase()
    {
        VirtualWorld? world = _worldProvider();
        if (world is null)
        {
            GD.PushWarning("Completion particle benchmark requires an active world.");
            return;
        }

        ClearActive();
        _caseIndex = (_caseIndex + 1) % Cases.Length;
        _activeCount = Cases[_caseIndex];
        Aabb bounds = world.GetWorldBounds();
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float spacing = Math.Max(0.01f, world.Profile.BlockSpacing);
        float worldRadius = Math.Max(spacing * 2.0f, bounds.Size.Length() * 0.5f);
        float scatterRadius = Math.Max(spacing * 4.0f, Math.Min(worldRadius * 0.58f, spacing * 20.0f));

        ulong beforeUsec = Time.GetTicksUsec();
        _ceremony = new WorldCompletionCeremony { Name = $"CompletionParticleBenchmark_{_activeCount}" };
        _ceremony.Initialize(world.Profile, _assets, _camera.Camera, center, _activeCount, scatterRadius, 0L);
        _ceremony.StageChanged += OnStageChanged;
        _ceremony.Completed += OnCompleted;
        AddChild(_ceremony);
        ulong afterUsec = Time.GetTicksUsec();

        _elapsed = 0.0;
        _peakImplosionMs = 0.0;
        _peakScatterMs = 0.0;
        _peakSuctionMs = 0.0;
        _stage = WorldCompletionVisualStage.Implosion;
        double setupMs = (afterUsec - beforeUsec) / 1000.0;
        GD.Print($"COMPLETION PARTICLE BENCH start count={_activeCount:N0} setup={setupMs:0.00}ms adapter='{RenderingServer.GetVideoAdapterName()}'. F6 starts the next preset.");
    }

    private void OnStageChanged(WorldCompletionVisualStage stage) => _stage = stage;

    private void OnCompleted()
    {
        GD.Print(
            $"COMPLETION PARTICLE BENCH done count={_activeCount:N0} elapsed={_elapsed:0.00}s " +
            $"peak_implosion={_peakImplosionMs:0.00}ms peak_scatter={_peakScatterMs:0.00}ms peak_suction={_peakSuctionMs:0.00}ms.");
        ClearActive();
    }

    private void ClearActive()
    {
        if (_ceremony is null) return;
        _ceremony.StageChanged -= OnStageChanged;
        _ceremony.Completed -= OnCompleted;
        _ceremony.QueueFree();
        _ceremony = null;
    }
}
''')

# -----------------------------------------------------------------------------
# Plan/research update: explicitly record source patterns and staged implementation decisions.
# -----------------------------------------------------------------------------
plan_path = "docs/WORLD_INTRO_AND_BLACK_HOLE_COMPLETION_PLAN.md"
plan = read(plan_path)
if "# 22. Late-game Flux Laser branch" not in plan:
    plan += r'''

---

# 22. Late-game Flux Laser branch

## 22.1 Reference-game research

The laser should feel like a qualitative late-game system rather than another flat Breaker Power rank.
The implementation takes the following patterns heavily as design references while keeping this game's
world/cursor mining identity:

- **To The Core**: the Lens Enhancer is an equipment laser fired at range with LMB and explicitly costs
  fuel. Equipment itself levels from collected materials. Reference:
  https://steamcommunity.com/sharedfiles/filedetails/?id=3019699442
- **Nodebuster**: a short, highly readable incremental built around a sprawling upgrade tree where
  individual nodes create the sense of repeatedly transforming the core action rather than hiding every
  improvement inside one repeated rank. Reference: https://store.steampowered.com/app/3107330/Nodebuster/
- **(the) Gnorp Apologue**: qualitative upgrades/talents combine into synergistic late builds rather than
  only applying linear output multipliers. Reference: https://gnorp.dev/
- **Fire Bug**: its beam branch explicitly separates raw power, a bigger lens, hotter beam, faster
  cooldown, split-ray behavior, lingering trails and critical effects. That is the clearest direct model
  for giving a laser its own identity. Reference: https://store.steampowered.com/app/4924290/Fire_Bug/
- **Revolution Idle-style charge grammar**: manual clicks fill the main progress faster while automation
  also advances it, with separate click/auto upgrades and burst effects. Reference:
  https://revolutionidle.org/wiki/mechanics
- **Cosmic Brothers**: its incremental weapon tree separates Fire Rate, Chain Arc and Multi-Beam, useful
  later references if Flux Laser eventually grows beyond one wide beam. Reference:
  https://namjo-games.itch.io/cosmicbrothers/devlog/1285400/cosmic-brothers-v0110-lightning-strikes-twice

The resulting rule is: copy the *upgrade grammar*, not another game's numbers or presentation.

## 22.2 Base Flux Laser contract

The first unlock is deliberately late and depends on both `manual_aftershock` and
`orb_breaker_swarm`, making it a convergence capstone between active manual mining and autonomous
late-game systems.

Base behavior:

```text
state            behavior
---------------  ----------------------------------------------------------
Charging         valid manual click adds 1.25% capacitor charge
Charging         Hover Mining auto-action adds 0.30% capacitor charge
Primed           100% charge waits only until cursor has a valid world target
Active           automatically fires a 3x3 cursor beam for exactly 5 seconds
Damage           1.0 block-hardness damage per second to each covered block
Cooldown         60 seconds; no charge can accumulate during cooldown
Ready            cooldown completes -> empty capacitor can charge again
```

Active clicking is therefore roughly four times as valuable per action as the automatic route, but an
idle/Hover Mining build will still eventually earn every laser burst.

The laser uses the same authored hardness state as manual mining. It is not an instant-delete bypass and
must not invent a second block-health system.

## 22.3 Beam targeting

- Beam follows the live cursor throughout the burst.
- Target is resolved with the same authoritative voxel raycaster used by manual mining.
- Base beam damages the exposed 3x3 face around the raycast center.
- `Wide Lens` upgrades that to 5x5.
- Damage is integrated at a bounded 10 Hz gameplay cadence so 1.0 damage/sec remains deterministic
  without creating 60 damage events per affected block per second.
- Removed blocks still flow through normal mining, collection, replay and completion observers.

## 22.4 Laser skill branch

Initial one-purchase nodes:

1. **Flux Laser** — unlock the base 5s / 60s / 1.0 dmg/s cycle.
2. **Click Capacitor** — +50% manual-click charge.
3. **Auto Flux Coupler** — 2x Hover Mining charge contribution.
4. **Wide Lens** — 3x3 -> 5x5 beam.
5. **Cryo Radiator** — 60s -> 50s cooldown.
6. **Hotter Beam** — 1.0 -> 1.5 block-damage/sec.
7. **Extended Burn** — natural charged burst 5s -> 7s.
8. **Resource Furnace** — optional paid overburn after the natural burst.
9. **Closed-Loop Furnace** — 40% lower overburn resource consumption.

Future full-release extensions, only after real balance data, may use the other researched patterns:
refraction/multi-beam, chain arcs, a lingering heat trail, critical thermal events, or material-specific
beam interactions. Those are deliberately not bundled into the first implementation.

## 22.5 Resource Furnace / sacrifice mode

Resource Furnace is a late resource sink inspired most directly by To The Core's fuel-cost laser.
It never replaces the normal capacitor loop.

- Player explicitly arms `OVERBURN` on the laser HUD.
- The normal earned charge always supplies its complete free burst first.
- Only after that timer reaches zero does paid overburn begin.
- Overburn consumes ordinary resources continuously while the beam remains active.
- Initial tuning target: 300 resources/sec.
- If disabled or unaffordable, the beam stops immediately and normal cooldown begins.
- Saving during overburn must not be a cooldown exploit; reload resumes from cooldown, not free paid fire.
- `Closed-Loop Furnace` reduces the spend rate but never produces resources or refunds burned currency.

## 22.6 Persistence / lifecycle

Persist per-world:

```text
LaserCharge
LaserCooldownSeconds
LaserActiveSeconds
LaserResourceBurnEnabled
```

Rules:

- intro/completion locks stop laser progression exactly like manual/automation input;
- pause freezes the node normally;
- normal active burst can survive Save & Return and resume after reload;
- overburn is serialized as cooldown-on-return rather than as an unpaid active beam;
- cooldown cannot be bypassed by world/browser transitions;
- reset-save clears all cycle state naturally with the rest of the world save.

## 22.7 HUD

A compact top-center capacitor bar is visible only after Flux Laser unlocks.

States must be explicit:

- `CHARGING 63%`;
- `PRIMED`;
- `ACTIVE 3.4s`;
- `RESOURCE OVERBURN` plus resources/sec;
- `COOLDOWN 47s`.

The bar drains during Active, becomes a cooldown progress bar while locked, and returns to an empty
charge bar afterward. Resource Furnace adds an explicit `OVERBURN ON/OFF` control so ordinary currency
can never be destroyed accidentally by merely buying the skill.

## 22.8 Implementation order

### Phase I1 — finish black-hole plan gaps

1. Add compressed `ResolveAllForCompletion()` pickup settlement.
2. Add presentation-only resource count-up during suction.
3. Add F6 exact-count particle benchmark presets: 25 / 6,824 / 61,225 / 123,412 / 1,000,000.

### Phase I2 — laser foundation

1. Add laser derived stats/effect types and nine-node late branch.
2. Add world-save capacitor/cooldown/active/toggle fields.
3. Add cursor beam controller and compact HUD.
4. Feed manual clicks and Hover Mining auto-actions into the same capacitor with different weights.
5. Reuse manual hardness damage and existing pickup/replay/completion paths.

### Phase I3 — paid resource burn

1. Add explicit overburn toggle.
2. Start paid burn only after free burst ends.
3. Spend ordinary currency continuously in bounded integer transactions.
4. Start cooldown immediately on disable/insufficient funds.
5. Add cost-efficiency skill.

### Phase I4 — tuning / local regression

Profile and tune:

- clicks required for a base burst;
- idle Hover Mining time-to-burst at each late Breaker Speed level;
- 3x3 vs 5x5 effective blocks/sec on rough terrain;
- cooldown uptime and whether 50s is too permissive;
- paid overburn cost against real 50³ wallet sizes;
- simultaneous laser + Hover Mining feedback density;
- save/reload during charging, active burst, cooldown and overburn.

## 22.9 Laser acceptance criteria

1. Flux Laser is hidden until its two late capstone prerequisites are owned.
2. Manual valid clicks charge faster per action than Hover Mining.
3. Hover Mining can fill the capacitor without physical clicking.
4. Charge cannot increase during intro, pause, completion lock, active beam or cooldown.
5. At 100%, a valid cursor target automatically begins the burst.
6. Base natural burst lasts 5.0 seconds before upgrades.
7. Base cooldown lasts 60.0 seconds before upgrades.
8. Base beam applies 1.0 authored hardness damage/sec, not one instant block deletion/sec.
9. Base beam covers 3x3 exposed face cells and Wide Lens covers 5x5.
10. Laser removals use normal reward/pickup/replay/completion accounting exactly once.
11. Saving/reloading cannot reset cooldown or duplicate a burst.
12. Resource Furnace is opt-in and only burns currency after free charge is exhausted.
13. Resource burn can never drive currency negative.
14. Running out of resources immediately ends overburn and starts cooldown.
15. Normal CI remains green and the 50³ completion benchmark remains a local release gate.
'''
    write(plan_path, plan)

status_path = "docs/IMPLEMENTATION_STATUS.md"
status = read(status_path)
if "## Late-game Flux Laser" not in status:
    status += r'''

## Late-game Flux Laser

Implementation branch now contains the first Flux Laser pass described in
`WORLD_INTRO_AND_BLACK_HOLE_COMPLETION_PLAN.md`: late capstone-gated charge meter, faster manual vs
Hover Mining charge, automatic 5-second cursor burst, 60-second cooldown, authored hardness damage,
3x3/5x5 beam footprints, persistent cycle state, and opt-in resource-funded overburn. The laser branch
adds nine one-purchase nodes. Local Godot playtesting still owns final charge/cooldown/cost tuning.

The same pass closes the remaining completion-presentation gaps with compressed pickup settlement,
visual resource count-up during black-hole suction, and F6 exact-count GPU benchmark presets through
1,000,000 particles.
'''
    write(status_path, status)

print("Applied completion finish + late Flux Laser implementation and plan update.")
