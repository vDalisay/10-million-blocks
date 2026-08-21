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
        AddChild(_pauseMenu);

        // The completion overlay is created during GameRoot._Ready. Hook it one frame later so the
        // terminal demo button can move directly into the already-existing world browser after the
        // normal completion handler has committed the final save.
        Callable.From(AttachDemoBrowseAction).CallDeferred();
    }

    private void AttachDemoBrowseAction()
    {
        if (_completionView is null || !GodotObject.IsInstanceValid(_completionView)) return;
        _completionView.ContinueRequested += OnCompletionContinueBrowse;
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

    private void OnPauseReturnToMainMenuRequested()
    {
        if (_sessionPersists && _world is not null)
        {
            CaptureCurrentSession();
            TrySaveCurrentSession(captureFirst: false);
        }

        _pauseMenu?.Close();
        GetTree().Paused = false;
        Error result = GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        if (result != Error.Ok)
        {
            GD.PushError($"Could not return to main menu ({result}).");
        }
    }

    private void OnCompletionContinueBrowse()
    {
        // For ordinary worlds the first completion listener has already advanced into the next world,
        // so the active profile no longer matches. A debug preview also never marks the finale complete.
        if (_world?.Profile.Id != "reference_ridges"
            || !_save.CompletedWorldIds.Contains("reference_ridges"))
        {
            return;
        }

        _worldBrowser?.Open();
    }
}
