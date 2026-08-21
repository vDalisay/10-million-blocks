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

        // BuildPersistentPresentation creates the completion overlay during _Ready. Attach after that
        // setup so the ordinary progression handler remains authoritative; this second listener only
        // handles the special terminal demo action after the final save has been committed.
        Callable.From(AttachDemoCompletionPolish).CallDeferred();
    }

    private void AttachDemoCompletionPolish()
    {
        if (_completionView is null || !GodotObject.IsInstanceValid(_completionView)) return;
        _completionView.ContinueRequested += OnCompletionContinuePolish;
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

        ReturnToMainMenu();
    }

    private void OnCompletionContinuePolish()
    {
        // The normal completion handler runs first. For an actual final clear it has already marked and
        // saved reference_ridges; debug completion previews deliberately do not satisfy this condition.
        if (_world?.Profile.Id != "reference_ridges"
            || !_save.CompletedWorldIds.Contains("reference_ridges"))
        {
            return;
        }

        ReturnToMainMenu();
    }

    private void ReturnToMainMenu()
    {
        _pauseMenu?.Close();
        GetTree().Paused = false;
        Error result = GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        if (result != Error.Ok)
        {
            GD.PushError($"Could not return to main menu ({result}).");
        }
    }
}
