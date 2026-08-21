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
            || string.Equals(_world.Profile.Id, worldId, StringComparison.Ordinal))
        {
            RecoverWorldBrowserTransition("Revisit request became invalid before it could start.");
            return;
        }

        try
        {
            // Do not leave the active world until its latest state has actually reached disk. The
            // ordinary autosave helper intentionally swallows I/O errors, which is appropriate for a
            // background retry but not for an explicit navigation operation.
            CaptureCurrentSession();
            _saveService.Save(_save);
            _autosaveDirty = false;
            _autosaveTimer = 0.0;

            _progression.RestoreWorld(worldId);
            _save.CurrentWorldId = worldId;
            _saveService.Save(_save);
            BuildWorldSession(_worlds.Get(worldId), applyOfflineProgress: false, persistSession: true);
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not revisit world '{worldId}': {exception}");
            RecoverWorldBrowserTransition("Could not load the selected world. See the Godot log.");
        }
    }

    private void OnWorldReplayRequested(string worldId)
    {
        if (!_sessionPersists || _world is null || !_save.CompletedWorldIds.Contains(worldId))
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

        try
        {
            string activeWorldId = _world.Profile.Id;
            CaptureCurrentSession();
            _saveService.Save(_save);
            _autosaveDirty = false;
            _autosaveTimer = 0.0;

            _replayReturnWorldId = activeWorldId;
            ReplayData replay = ReplayBinaryCodec.Read(absolute);
            BuildReplaySession(_worlds.Get(worldId), replay);
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not open replay for '{worldId}': {exception}");
            _replayReturnWorldId = string.Empty;
            RecoverWorldBrowserTransition("The replay could not be opened. See the Godot log.");
        }
    }

    private void RecoverWorldBrowserTransition(string message)
    {
        WorldLoadingScreen.CancelGlobal();
        GD.PushWarning(message);
        if (_worldBrowser is not null && IsInstanceValid(_worldBrowser))
        {
            _worldBrowser.Open();
        }
    }
}
