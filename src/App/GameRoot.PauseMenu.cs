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
}
