using System;
using Godot;
using TenMillionBlocks.Content;
using TenMillionBlocks.Replay;
using TenMillionBlocks.Save;
using TenMillionBlocks.UI;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private WorldSelectView? _worldBrowser;
    private string _replayReturnWorldId = string.Empty;

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (!_sessionPersists || _sessionRoot is null || _world is null) return;

        // Persistent session-level observers/UI are attached lazily after BuildWorldSession has wired
        // every service. Replay/debug sessions remain read-only and therefore never receive them.
        EnsureTutorialLayer();

        if (_worldBrowser is null
            || !IsInstanceValid(_worldBrowser)
            || _worldBrowser.GetParent() != _sessionRoot)
        {
            AttachWorldBrowser(_world.Profile);
        }
    }

    private void AttachWorldBrowser(WorldProfile profile)
    {
        if (_sessionRoot is null) return;

        _worldBrowser = new WorldSelectView { Name = "WorldSelectView" };
        _worldBrowser.Initialize(_worlds, _save, profile.Id);
        _worldBrowser.RevisitRequested += OnWorldRevisitRequested;
        _worldBrowser.ReplayRequested += OnWorldReplayRequested;
        _worldBrowser.OpenChanged += OnWorldBrowserOpenChanged;
        _sessionRoot.AddChild(_worldBrowser);
    }

    private void OnWorldBrowserOpenChanged(bool open)
    {
        _skillTree?.Close();
        if (_manualMining is not null) _manualMining.InputEnabled = !open && !_completionShown;
        if (_placement is not null)
        {
            _placement.InputEnabled = !open && !_completionShown && (_world?.Profile.AutomationAvailable ?? false);
        }
    }

    private void OnWorldRevisitRequested(string worldId)
    {
        if (!_sessionPersists
            || _world is null
            || !_save.UnlockedWorldIds.Contains(worldId)
            || !_worlds.Worlds.ContainsKey(worldId)
            || string.Equals(_world.Profile.Id, worldId, StringComparison.Ordinal))
        {
            RecoverWorldBrowserTransition("Revisit request became invalid before it could start.");
            return;
        }

        string activeWorldId = _world.Profile.Id;
        string displayName = _worlds.Get(worldId).DisplayName;
        WorldLoadingScreen.RunTransition(
            this,
            $"LOADING {displayName}",
            () => PerformWorldRevisit(worldId, activeWorldId));
    }

    private void PerformWorldRevisit(string worldId, string activeWorldId)
    {
        try
        {
            // Save the active world before changing progression. BuildWorldSession normally performs a
            // defensive capture of the previous persistent session, but doing that after CurrentWorldId
            // has already moved to the target makes navigation order-dependent. This path saves once,
            // then temporarily marks the outgoing session non-persistent while it is replaced.
            SaveActiveWorldForNavigation();

            _progression.RestoreWorld(worldId);
            _save.CurrentWorldId = worldId;
            BuildPersistentWorldAfterExplicitSave(_worlds.Get(worldId));
            _save.CurrentWorldId = worldId;
            _saveService.Save(_save);
            _autosaveDirty = false;
            _autosaveTimer = 0.0;

            if (_world?.Profile.Id != worldId)
            {
                throw new InvalidOperationException(
                    $"World revisit resolved to '{_world?.Profile.Id ?? "none"}' instead of requested '{worldId}'.");
            }

            GD.Print($"Revisited world '{worldId}'.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not revisit world '{worldId}': {exception}");
            RestoreActiveWorldAfterFailedTransition(activeWorldId);
            RecoverWorldBrowserTransition("Could not load the selected world. The previous world was restored; see the Godot log for the exact reason.");
        }
    }

    private void OnWorldReplayRequested(string worldId)
    {
        if (!_sessionPersists
            || _world is null
            || !_save.CompletedWorldIds.Contains(worldId)
            || !_worlds.Worlds.ContainsKey(worldId))
        {
            RecoverWorldBrowserTransition("Replay request became invalid before it could start.");
            return;
        }
        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? savedWorld)
            || string.IsNullOrWhiteSpace(savedWorld.ReplayFile))
        {
            RecoverWorldBrowserTransition("That world no longer has a replay file recorded.");
            return;
        }

        string absolute = ProjectSettings.GlobalizePath(savedWorld.ReplayFile);
        if (!System.IO.File.Exists(absolute))
        {
            RecoverWorldBrowserTransition("The replay file is missing from disk.");
            return;
        }

        string activeWorldId = _world.Profile.Id;
        string displayName = _worlds.Get(worldId).DisplayName;
        WorldLoadingScreen.RunTransition(
            this,
            $"LOADING {displayName} REPLAY",
            () => PerformWorldReplay(worldId, activeWorldId, absolute));
    }

    private void PerformWorldReplay(string worldId, string activeWorldId, string absoluteReplayPath)
    {
        try
        {
            SaveActiveWorldForNavigation();
            _replayReturnWorldId = activeWorldId;
            ReplayData replay = ReplayBinaryCodec.Read(absoluteReplayPath);
            BuildReplaySession(_worlds.Get(worldId), replay);

            if (_world?.Profile.Id != worldId || _replayView is null)
            {
                throw new InvalidOperationException(
                    $"Replay resolved to '{_world?.Profile.Id ?? "none"}' instead of requested '{worldId}'.");
            }

            GD.Print($"Opened replay for world '{worldId}', returning to '{activeWorldId}' on exit.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not open replay for '{worldId}': {exception}");
            _replayReturnWorldId = string.Empty;
            RestoreActiveWorldAfterFailedTransition(activeWorldId);
            RecoverWorldBrowserTransition("The replay could not be opened. The active world was restored; see the Godot log for the exact reason.");
        }
    }

    private void SaveActiveWorldForNavigation()
    {
        CaptureCurrentSession();
        _saveService.Save(_save);
        _autosaveDirty = false;
        _autosaveTimer = 0.0;
    }

    private void BuildPersistentWorldAfterExplicitSave(WorldProfile profile)
    {
        bool previousPersistence = _sessionPersists;
        _sessionPersists = false;
        try
        {
            BuildWorldSession(profile, applyOfflineProgress: false, persistSession: true);
        }
        catch
        {
            _sessionPersists = previousPersistence;
            throw;
        }
    }

    private void RestoreActiveWorldAfterFailedTransition(string worldId)
    {
        try
        {
            if (!_worlds.Worlds.ContainsKey(worldId)) return;
            _progression.RestoreWorld(worldId);
            _save.CurrentWorldId = worldId;
            _saveService.Save(_save);

            if (_world?.Profile.Id == worldId
                && _sessionRoot is not null
                && IsInstanceValid(_sessionRoot))
            {
                return;
            }

            BuildPersistentWorldAfterExplicitSave(_worlds.Get(worldId));
            _save.CurrentWorldId = worldId;
            _saveService.Save(_save);
        }
        catch (Exception restoreException)
        {
            GD.PushError($"Failed to restore world '{worldId}' after a transition error: {restoreException}");
        }
    }

    private void RecoverWorldBrowserTransition(string message)
    {
        WorldLoadingScreen.CancelGlobal();
        GD.PushWarning(message);
        if (_worldBrowser is not null
            && IsInstanceValid(_worldBrowser)
            && _worldBrowser.IsInsideTree())
        {
            _worldBrowser.Open();
        }
    }
}
