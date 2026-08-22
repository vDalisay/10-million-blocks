using System;
using Godot;
using TenMillionBlocks.Presentation;
using TenMillionBlocks.UI;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private PauseMenuView? _pauseMenu;

    public override void _EnterTree()
    {
        GraphicsSettingsRuntime graphics = GraphicsSettingsRuntime.Ensure(GetTree());
        _pauseMenu = new PauseMenuView { Name = "PauseMenuView" };
        _pauseMenu.Initialize(graphics, CanOpenPauseMenu);
        _pauseMenu.ReturnToMainMenuRequested += OnPauseReturnToMainMenuRequested;
        _pauseMenu.WorldsRequested += OnPauseWorldsRequested;
        AddChild(_pauseMenu);
        _pauseMenu.EnableWorldBrowserEntry();

        Callable.From(AttachDemoCompletionActions).CallDeferred();
    }

    private void AttachDemoCompletionActions()
    {
        if (_completionView is null || !GodotObject.IsInstanceValid(_completionView)) return;
        _completionView.ContinueRequested += OnCompletionContinueBrowse;
        _completionView.ReturnToMainMenuRequested += OnDemoCompletionReturnToMainMenu;
    }

    private bool CanOpenPauseMenu()
    {
        if (WorldLoadingScreen.IsActive
            || !_sessionPersists
            || _world is null
            || _completionShown
            || _replayView is not null)
        {
            return false;
        }
        if (_worldBrowser?.IsOpen == true || _skillTree?.IsOpen == true)
        {
            return false;
        }
        return true;
    }

    private void OnPauseWorldsRequested()
    {
        if (!_sessionPersists || _world is null) return;

        if (_worldBrowser is null
            || !GodotObject.IsInstanceValid(_worldBrowser)
            || _worldBrowser.GetParent() != _sessionRoot)
        {
            AttachWorldBrowser(_world.Profile);
        }

        _pauseMenu?.Close();
        GetTree().Paused = false;
        _worldBrowser?.Open();
    }

    private void OnPauseReturnToMainMenuRequested()
    {
        if (!TrySaveBeforeLeaving(out string error))
        {
            _pauseMenu?.ReportReturnFailure(error);
            return;
        }

        _pauseMenu?.Close();
        GetTree().Paused = false;
        ChangeToMainMenu();
    }

    private void OnDemoCompletionReturnToMainMenu()
    {
        // Completion normally already committed the final save, but write once more before leaving so
        // the explicit end-of-demo action has the same transactional guarantee as pause -> Save & Return.
        if (!TrySaveBeforeLeaving(out string error))
        {
            GD.PushError(error);
            return;
        }

        _completionView?.HideCompletion();
        _completionShown = false;
        GetTree().Paused = false;
        ChangeToMainMenu();
    }

    private bool TrySaveBeforeLeaving(out string error)
    {
        error = string.Empty;
        if (!_sessionPersists || _world is null)
        {
            return true;
        }

        try
        {
            CaptureCurrentSession();
            _saveService.Save(_save);
            _autosaveDirty = false;
            _autosaveTimer = 0.0;
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not save before returning to the main menu: {exception}");
            error = "SAVE FAILED — gameplay was kept open. Check the Godot log and try again.";
            return false;
        }
    }

    private void ChangeToMainMenu()
    {
        Error result = GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        if (result == Error.Ok) return;

        GD.PushError($"Could not return to main menu ({result}).");
        _pauseMenu?.ReportReturnFailure($"Could not open the main menu ({result}).");
    }

    private void OnCompletionContinueBrowse()
    {
        if (_world?.Profile.Id != "reference_ridges"
            || !_save.CompletedWorldIds.Contains("reference_ridges"))
        {
            return;
        }

        _completionShown = false;
        if (_manualMining is not null) _manualMining.InputEnabled = true;
        if (_placement is not null)
        {
            _placement.InputEnabled = _world.Profile.AutomationAvailable;
        }
        if (_miners is not null) _miners.ProcessMode = ProcessModeEnum.Inherit;
        if (_worldEvents is not null) _worldEvents.ProcessMode = ProcessModeEnum.Inherit;

        _worldBrowser?.Open();
    }
}
