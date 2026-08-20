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
        if (_placement is not null) _placement.InputEnabled = !open && !_completionShown && (_world?.Profile.AutomationAvailable ?? false);
    }

    private void OnWorldRevisitRequested(string worldId)
    {
        if (!_sessionPersists || _world is null || !_save.UnlockedWorldIds.Contains(worldId)) return;
        if (string.Equals(_world.Profile.Id, worldId, StringComparison.Ordinal)) return;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        _progression.RestoreWorld(worldId);
        _save.CurrentWorldId = worldId;
        _saveService.Save(_save);
        BuildWorldSession(_worlds.Get(worldId), applyOfflineProgress: false, persistSession: true);
    }

    private void OnWorldReplayRequested(string worldId)
    {
        if (!_sessionPersists || _world is null || !_save.CompletedWorldIds.Contains(worldId)) return;
        if (!_save.Worlds.TryGetValue(worldId, out WorldSaveData? savedWorld)
            || string.IsNullOrWhiteSpace(savedWorld.ReplayFile))
        {
            return;
        }

        string absolute = ProjectSettings.GlobalizePath(savedWorld.ReplayFile);
        if (!System.IO.File.Exists(absolute)) return;

        CaptureCurrentSession();
        TrySaveCurrentSession(captureFirst: false);
        ReplayData replay = ReplayBinaryCodec.Read(absolute);
        BuildReplaySession(_worlds.Get(worldId), replay);
    }
}
