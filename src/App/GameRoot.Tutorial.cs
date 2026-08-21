using Godot;
using TenMillionBlocks.Tutorial;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private GameplayEventHub? _gameplayEvents;
    private GameplayEventBridge? _gameplayEventBridge;
    private TutorialDirector? _tutorialDirector;

    private void EnsureTutorialLayer()
    {
        if (!_sessionPersists
            || _sessionRoot is null
            || _world is null
            || _mining is null
            || _skills is null
            || _miners is null)
        {
            return;
        }

        if (_tutorialDirector is not null
            && IsInstanceValid(_tutorialDirector)
            && _tutorialDirector.GetParent() == _sessionRoot
            && _gameplayEventBridge is not null
            && IsInstanceValid(_gameplayEventBridge)
            && _gameplayEventBridge.GetParent() == _sessionRoot)
        {
            if (_worldEvents is not null) _gameplayEventBridge.AttachWorldEvents(_worldEvents);
            return;
        }

        _gameplayEvents = new GameplayEventHub();

        // Add the director first so it is subscribed before the bridge emits WorldStarted from _Ready.
        _tutorialDirector = new TutorialDirector { Name = "TutorialDirector" };
        _tutorialDirector.Initialize(_world.Profile, _save, _gameplayEvents);
        _tutorialDirector.StateChanged += MarkAutosaveDirty;
        _sessionRoot.AddChild(_tutorialDirector);

        _gameplayEventBridge = new GameplayEventBridge { Name = "GameplayEventBridge" };
        _gameplayEventBridge.Initialize(_world.Profile, _mining, _skills, _miners, _gameplayEvents);
        if (_worldEvents is not null) _gameplayEventBridge.AttachWorldEvents(_worldEvents);
        _sessionRoot.AddChild(_gameplayEventBridge);
    }
}
