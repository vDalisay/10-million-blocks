using Godot;
using TenMillionBlocks.Diagnostics;
using TenMillionBlocks.Tutorial;

namespace TenMillionBlocks.App;

public partial class GameRoot
{
    private GameplayEventHub? _gameplayEvents;
    private GameplayEventBridge? _gameplayEventBridge;
    private TutorialDirector? _tutorialDirector;
    private PacingTelemetryRecorder? _pacingTelemetry;

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
            if (_worldEvents is not null)
            {
                _gameplayEventBridge.AttachWorldEvents(_worldEvents);
                _worldEvents.AttachSkills(_skills);
            }
            return;
        }

        _gameplayEvents = new GameplayEventHub();

        // Add observers before the bridge so its _Ready-time WorldStarted event reaches every consumer.
        _tutorialDirector = new TutorialDirector { Name = "TutorialDirector" };
        _tutorialDirector.Initialize(_world.Profile, _save, _gameplayEvents);
        _tutorialDirector.StateChanged += MarkAutosaveDirty;
        _sessionRoot.AddChild(_tutorialDirector);

        if (OS.IsDebugBuild())
        {
            _pacingTelemetry = new PacingTelemetryRecorder { Name = "PacingTelemetryRecorder" };
            _pacingTelemetry.Initialize(
                _world.Profile,
                _mining,
                _skills,
                _miners,
                _specialResources,
                _gameplayEvents,
                _manualBlocksThisWorld,
                _automatedBlocksThisWorld);
            _sessionRoot.AddChild(_pacingTelemetry);
        }
        else
        {
            _pacingTelemetry = null;
        }

        _gameplayEventBridge = new GameplayEventBridge { Name = "GameplayEventBridge" };
        _gameplayEventBridge.Initialize(_world.Profile, _mining, _skills, _miners, _gameplayEvents);
        if (_worldEvents is not null)
        {
            _gameplayEventBridge.AttachWorldEvents(_worldEvents);
            _worldEvents.AttachSkills(_skills);
        }
        _sessionRoot.AddChild(_gameplayEventBridge);
    }
}
