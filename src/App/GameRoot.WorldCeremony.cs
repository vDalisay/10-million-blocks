using System;
using Godot;
using TenMillionBlocks.Automation;
using TenMillionBlocks.Content;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;
using TenMillionBlocks.Progression;
using TenMillionBlocks.Save;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private enum WorldRunPhase
    {
        PreparingWorld,
        IntroLocked,
        Playing,
        CompletionLocked,
        Implosion,
        BonusScatter,
        BlackHoleSuction,
        Results,
    }

    private const double WorldIntroDurationSeconds = 3.0;

    private WorldRunPhase _runPhase = WorldRunPhase.PreparingWorld;
    private double _introElapsed;
    private double _activePlaySeconds;
    private bool _clearReached;
    private double _completionClearSeconds;
    private int _completionScorePercent;
    private long _completionBonusResources;
    private bool _completionBonusClaimed;
    private bool _loadedCompletedWorld;
    private WorldCompletionCeremony? _completionCeremony;

    private void ResetWorldRunLifecycle()
    {
        _worldView?.ResetIntroWave();
        if (_completionCeremony is not null && GodotObject.IsInstanceValid(_completionCeremony))
        {
            _completionCeremony.QueueFree();
        }
        _completionCeremony = null;
        _camera?.EndCinematicFocus(restoreInput: true);
        _runPhase = WorldRunPhase.PreparingWorld;
        _introElapsed = 0.0;
        _activePlaySeconds = 0.0;
        _clearReached = false;
        _completionClearSeconds = 0.0;
        _completionScorePercent = 0;
        _completionBonusResources = 0L;
        _completionBonusClaimed = false;
        _loadedCompletedWorld = false;
    }

    private void InitializeWorldRunLifecycle(WorldSaveData? savedWorld, OfflineProgressResult offline)
    {
        _runPhase = WorldRunPhase.PreparingWorld;
        _introElapsed = 0.0;
        _activePlaySeconds = Math.Max(0.0, savedWorld?.ActivePlaySeconds ?? 0.0);
        _clearReached = savedWorld?.ClearReached ?? false;
        _completionClearSeconds = Math.Max(0.0, savedWorld?.CompletionClearSeconds ?? 0.0);
        _completionScorePercent = Math.Clamp(savedWorld?.CompletionScorePercent ?? 0, 0, 100);
        _completionBonusResources = Math.Max(0L, savedWorld?.CompletionBonusResources ?? 0L);
        _completionBonusClaimed = savedWorld?.CompletionBonusClaimed ?? false;
        _loadedCompletedWorld = savedWorld?.Completed ?? false;

        if (offline.BlocksRemoved > 0)
        {
            _activePlaySeconds += Math.Max(0.0, offline.SimulatedSecondsConsumed);
            if (offline.ClearedWorld && !_clearReached)
            {
                _completionClearSeconds = _activePlaySeconds;
                _completionScorePercent = CompletionScore.CalculatePercent(_completionClearSeconds);
                _completionBonusResources = CompletionScore.CalculateBonus(_world?.InitialMineableBlocks ?? 0L, _completionScorePercent);
                _clearReached = true;
            }
        }

        SetGameplayInteractionEnabled(false);
    }

    private void ProcessWorldRun(double delta)
    {
        if (!_sessionPersists || _world is null || _worldView is null) return;

        switch (_runPhase)
        {
            case WorldRunPhase.PreparingWorld:
                if (!_worldView.InitialPresentationReady || WorldLoadingScreen.IsActive) return;

                if (_loadedCompletedWorld)
                {
                    _runPhase = WorldRunPhase.Results;
                    ShowCompletion(debugPreview: false);
                    return;
                }

                if (_world.RemainingMineableBlocks == 0)
                {
                    if (!_clearReached) FreezeCompletionResultAndSave();
                    BeginCompletionCinematic();
                    return;
                }

                _camera.InputEnabled = false;
                _worldView.PrepareIntroWave(_camera.Camera);
                _introElapsed = 0.0;
                _runPhase = WorldRunPhase.IntroLocked;
                return;

            case WorldRunPhase.IntroLocked:
                _introElapsed += Math.Max(0.0, delta);
                _worldView.UpdateIntroWave(_introElapsed);
                if (_introElapsed < WorldIntroDurationSeconds) return;
                _worldView.ResetIntroWave();
                _runPhase = WorldRunPhase.Playing;
                SetGameplayInteractionEnabled(true);
                return;

            case WorldRunPhase.Playing:
                _activePlaySeconds += Math.Max(0.0, delta);
                _autosaveDirty = true;
                return;
        }
    }

    private void SetGameplayInteractionEnabled(bool enabled)
    {
        if (_manualMining is not null) _manualMining.InputEnabled = enabled;
        if (_placement is not null) _placement.InputEnabled = enabled && (_world?.Profile.AutomationAvailable ?? false);
        if (_miners is not null) _miners.ProcessMode = enabled ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (_worldEvents is not null) _worldEvents.ProcessMode = enabled ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (_skillTree is not null)
        {
            if (!enabled) _skillTree.Close();
            _skillTree.InteractionEnabled = enabled;
        }
        if (_camera is not null) _camera.InputEnabled = enabled;
    }

    private void FreezeCompletionResultAndSave()
    {
        if (_world is null || _clearReached) return;
        _runPhase = WorldRunPhase.CompletionLocked;
        SetGameplayInteractionEnabled(false);
        _clearReached = true;
        _completionClearSeconds = Math.Max(0.0, _activePlaySeconds);
        _completionScorePercent = CompletionScore.CalculatePercent(_completionClearSeconds);
        _completionBonusResources = CompletionScore.CalculateBonus(_world.InitialMineableBlocks, _completionScorePercent);
        _completionBonusClaimed = false;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        GD.Print($"Clear frozen at {_completionClearSeconds:0.00}s: {_completionScorePercent}% => {_completionBonusResources:N0} bonus resources.");
    }

    private void BeginCompletionCinematic()
    {
        if (_world is null || _worldView is null || _mining is null || _sessionRoot is null || _completionCeremony is not null) return;
        _runPhase = WorldRunPhase.CompletionLocked;
        SetGameplayInteractionEnabled(false);
        _resourceCollection?.CollectAllPending();

        Aabb bounds = _world.GetWorldBounds();
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float spacing = Math.Max(0.01f, _world.Profile.BlockSpacing);
        float worldRadius = Math.Max(spacing * 2.0f, bounds.Size.Length() * 0.5f);
        float scatterRadius = Math.Max(spacing * 4.0f, Math.Min(worldRadius * 0.58f, spacing * 20.0f));
        float cameraDistance = Math.Max(scatterRadius * 2.7f, worldRadius * 1.55f);
        _camera.BeginCinematicFocus(center, cameraDistance, immediate: false);

        _completionCeremony = new WorldCompletionCeremony { Name = "WorldCompletionCeremony" };
        _completionCeremony.Initialize(
            _world.Profile,
            _assets,
            _camera.Camera,
            center,
            _completionBonusResources,
            scatterRadius);
        _completionCeremony.StageChanged += OnCompletionVisualStageChanged;
        _completionCeremony.Completed += CommitCompletionRewardAndShowResults;
        _sessionRoot.AddChild(_completionCeremony);
    }

    private void OnCompletionVisualStageChanged(WorldCompletionVisualStage stage)
    {
        _runPhase = stage switch
        {
            WorldCompletionVisualStage.Implosion => WorldRunPhase.Implosion,
            WorldCompletionVisualStage.BonusScatter => WorldRunPhase.BonusScatter,
            WorldCompletionVisualStage.BlackHoleSuction => WorldRunPhase.BlackHoleSuction,
            _ => WorldRunPhase.CompletionLocked,
        };
    }

    private void CommitCompletionRewardAndShowResults()
    {
        if (_world is null || _mining is null || _completionBonusClaimed) return;

        _completionBonusClaimed = true;
        if (_completionBonusResources > 0) _mining.GrantCurrency(_completionBonusResources);

        WorldProfile? next = _progression.NextProfile();
        _save.CompletedWorldIds.Add(_world.Profile.Id);
        if (next is not null) _save.UnlockedWorldIds.Add(next.Id);
        _loadedCompletedWorld = true;
        _runPhase = WorldRunPhase.Results;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        ShowCompletion(debugPreview: false);
    }

    private static string FormatClearTime(double seconds)
    {
        int total = Math.Max(0, (int)Math.Floor(seconds));
        int minutes = total / 60;
        int remainder = total % 60;
        return $"{minutes:00}:{remainder:00}";
    }
}
