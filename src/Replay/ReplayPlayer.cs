using System;
using Godot;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Authoring;
using TenMillionBlocks.World.Rendering;
using TenMillionBlocks.World.Storage;

namespace TenMillionBlocks.Replay;

/// <summary>
/// Read-only playback over a fresh deterministic world baseline. Replay events mutate only this
/// session's WorldStateStore; they never pass through MiningService and therefore cannot award
/// currency, special resources, statistics, skill progress, or write new replay events.
/// </summary>
public partial class ReplayPlayer : Node
{
    private static readonly double[] AllowedSpeeds = [1.0, 2.0, 4.0, 8.0, 16.0, 32.0];

    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private ReplayData _data = null!;
    private int _cursor;
    private double _playheadTicks;
    private bool _finishedRaised;

    public event Action? Changed;
    public event Action? Finished;

    public bool IsPlaying { get; private set; } = true;
    public double Speed { get; private set; } = 1.0;
    public int AppliedEventCount => _cursor;
    public int EventCount => _data?.Events.Count ?? 0;
    public double CurrentSeconds => _data is null ? 0.0 : _playheadTicks / _data.Header.TickRate;
    public double DurationSeconds
        => _data is null || _data.Events.Count == 0
            ? 0.0
            : _data.Events[^1].Tick / (double)_data.Header.TickRate;
    public bool IsFinished => _data is not null && _cursor >= _data.Events.Count;

    public void Initialize(VirtualWorld world, WorldView view, ReplayData data)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ValidateHeader();
        Restart(autoplay: true);
    }

    public override void _Process(double delta)
    {
        if (!IsPlaying || _data is null || IsFinished)
        {
            return;
        }

        _playheadTicks += Math.Max(0.0, delta) * _data.Header.TickRate * Speed;
        ApplyThroughTick(_playheadTicks);
    }

    public void TogglePlaying()
    {
        if (IsFinished)
        {
            Restart(autoplay: true);
            return;
        }

        IsPlaying = !IsPlaying;
        Changed?.Invoke();
    }

    public void SetPlaying(bool playing)
    {
        if (playing && IsFinished)
        {
            Restart(autoplay: true);
            return;
        }

        if (IsPlaying == playing) return;
        IsPlaying = playing;
        Changed?.Invoke();
    }

    public void SetSpeed(double speed)
    {
        double selected = AllowedSpeeds[0];
        double bestDistance = double.MaxValue;
        foreach (double candidate in AllowedSpeeds)
        {
            double distance = Math.Abs(candidate - speed);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                selected = candidate;
            }
        }

        if (Math.Abs(Speed - selected) < 0.001) return;
        Speed = selected;
        Changed?.Invoke();
    }

    public void Restart(bool autoplay = true)
    {
        // Restore the untouched deterministic baseline without reconstructing gameplay services. Mark
        // every previously affected voxel dirty; WorldView's dirty chunk HashSet naturally coalesces
        // thousands of historical removals into a bounded number of chunk rebuilds.
        int previouslyApplied = _cursor;
        _world.State.RestoreSnapshot(
            Array.Empty<MinedChunkSnapshot>(),
            Array.Empty<ExhaustedRegionSnapshot>());

        for (int i = 0; i < previouslyApplied && i < _data.Events.Count; i++)
        {
            _view.MarkDirtyAround(FromLinearIndex(_data.Events[i].LinearIndex));
        }

        _cursor = 0;
        _playheadTicks = 0.0;
        _finishedRaised = false;
        IsPlaying = autoplay;
        Changed?.Invoke();
    }

    private void ApplyThroughTick(double targetTick)
    {
        int appliedBefore = _cursor;
        while (_cursor < _data.Events.Count && _data.Events[_cursor].Tick <= targetTick)
        {
            ReplayRemovalEvent item = _data.Events[_cursor];
            Vector3I voxel = FromLinearIndex(item.LinearIndex);
            if (!_world.SampleVoxel(voxel).Present)
            {
                throw new InvalidOperationException(
                    $"Replay event {_cursor:N0} targets missing/already-removed voxel {voxel} in '{_world.Profile.Id}'.");
            }

            if (!_world.State.MarkMined(voxel))
            {
                throw new InvalidOperationException(
                    $"Replay event {_cursor:N0} duplicates voxel {voxel} in '{_world.Profile.Id}'.");
            }

            _view.MarkDirtyAround(voxel);
            _cursor++;
        }

        if (_cursor != appliedBefore)
        {
            Changed?.Invoke();
        }

        if (IsFinished)
        {
            IsPlaying = false;
            if (!_finishedRaised)
            {
                _finishedRaised = true;
                Changed?.Invoke();
                Finished?.Invoke();
            }
        }
    }

    private Vector3I FromLinearIndex(long index)
    {
        long axis = _data.Header.AxisSize;
        long volume = checked(axis * axis * axis);
        if (index < 0 || index >= volume)
        {
            throw new InvalidOperationException($"Replay index {index:N0} is outside its declared address volume.");
        }

        long x = index % axis;
        long rest = index / axis;
        long y = rest % axis;
        long z = rest / axis;
        return new Vector3I(
            checked((int)(x + _data.Header.MinCoordinate)),
            checked((int)(y + _data.Header.MinCoordinate)),
            checked((int)(z + _data.Header.MinCoordinate)));
    }

    private void ValidateHeader()
    {
        ReplayHeader header = _data.Header;
        int expectedMin = -_world.MaxCoordinate;
        int expectedAxis = checked(_world.MaxCoordinate * 2 + 1);
        if (!string.Equals(header.WorldId, _world.Profile.Id, StringComparison.Ordinal)
            || header.GenerationVersion != _world.Profile.GenerationVersion
            || header.MinCoordinate != expectedMin
            || header.AxisSize != expectedAxis
            || header.TickRate <= 0)
        {
            throw new InvalidOperationException(
                $"Replay baseline does not match world '{_world.Profile.Id}' generation {_world.Profile.GenerationVersion}.");
        }

        if (header.HasFrozenBaselineIdentity)
        {
            string currentHash = WorldFreezeService.ComputeContentHash(_world.Profile);
            if (header.WorldVersion != _world.Profile.WorldVersion
                || !string.Equals(header.WorldContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Replay targets frozen world '{header.WorldId}' v{header.WorldVersion} with a different content hash.");
            }
        }

        if (header.FinalMinedCount < 0 || header.EventCount != _data.Events.Count)
        {
            throw new InvalidOperationException("Replay header final/event counts are inconsistent.");
        }

        uint previousTick = 0;
        for (int i = 0; i < _data.Events.Count; i++)
        {
            ReplayRemovalEvent item = _data.Events[i];
            if (i > 0 && item.Tick < previousTick)
            {
                throw new InvalidOperationException("Replay events are not ordered by nondecreasing tick.");
            }
            _ = FromLinearIndex(item.LinearIndex);
            previousTick = item.Tick;
        }
    }
}
