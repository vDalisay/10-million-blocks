using System;
using System.Collections.Generic;
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
/// Recorded wall-clock gaps are intentionally ignored: at 1x the replay removes the first block
/// immediately and then advances at one recorded removal per second.
/// </summary>
public partial class ReplayPlayer : Node
{
    public const double MinSpeed = 1.0;
    public const double MaxSpeed = 64.0;

    private readonly List<Vector3I> _changedVoxels = new();
    private VirtualWorld _world = null!;
    private WorldView _view = null!;
    private ReplayData _data = null!;
    private int _cursor;
    private double _sequenceSeconds;
    private double _eventAccumulator;
    private bool _finishedRaised;

    public event Action? Changed;
    public event Action? Finished;

    public bool IsPlaying { get; private set; } = true;
    public double Speed { get; private set; } = 1.0;
    public int AppliedEventCount => _cursor;
    public int EventCount => _data?.Events.Count ?? 0;
    public double CurrentSeconds => _data is null ? 0.0 : _sequenceSeconds;
    public double DurationSeconds
        => _data is null || _data.Events.Count <= 1
            ? 0.0
            : _data.Events.Count - 1.0;
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

        double scaledSeconds = Math.Max(0.0, delta) * Speed;
        _sequenceSeconds = Math.Min(DurationSeconds, _sequenceSeconds + scaledSeconds);
        _eventAccumulator += scaledSeconds;
        int due = (int)Math.Floor(_eventAccumulator);
        if (due <= 0) return;

        _eventAccumulator -= due;
        ApplyNextEvents(due);
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
        double selected = double.IsFinite(speed)
            ? Math.Clamp(speed, MinSpeed, MaxSpeed)
            : MinSpeed;
        if (Math.Abs(Speed - selected) < 0.001) return;
        Speed = selected;
        Changed?.Invoke();
    }

    public void Restart(bool autoplay = true)
    {
        // Restore the untouched deterministic baseline without reconstructing gameplay services.
        // Presentation invalidation is batched to unique chunks, which matters for a 50³ full-clear
        // replay where resetting 125k historical voxels should not enqueue 875k independent paths.
        int previouslyApplied = _cursor;
        _world.State.RestoreSnapshot(
            Array.Empty<MinedChunkSnapshot>(),
            Array.Empty<ExhaustedRegionSnapshot>());

        if (previouslyApplied > 0)
        {
            _changedVoxels.Clear();
            _changedVoxels.EnsureCapacity(previouslyApplied);
            for (int i = 0; i < previouslyApplied && i < _data.Events.Count; i++)
            {
                _changedVoxels.Add(FromLinearIndex(_data.Events[i].LinearIndex));
            }
            _view.MarkDirtyBatch(_changedVoxels);
        }

        _cursor = 0;
        _sequenceSeconds = 0.0;
        _eventAccumulator = 0.0;
        _finishedRaised = false;
        IsPlaying = autoplay && _data.Events.Count > 0;

        // A replay is a compact visualization of the run rather than a recording of player inactivity.
        // Start on the first action immediately; subsequent events occur once per second at 1x.
        if (IsPlaying)
        {
            ApplyNextEvents(1);
        }
        else
        {
            Changed?.Invoke();
        }
    }

    private void ApplyNextEvents(int count)
    {
        int appliedBefore = _cursor;
        _changedVoxels.Clear();
        int remaining = Math.Max(0, count);
        _changedVoxels.EnsureCapacity(Math.Min(remaining, Math.Max(0, _data.Events.Count - _cursor)));

        while (remaining-- > 0 && _cursor < _data.Events.Count)
        {
            ReplayRemovalEvent item = _data.Events[_cursor];
            Vector3I voxel = FromLinearIndex(item.LinearIndex);
            var sample = _world.SampleVoxel(voxel);
            if (!sample.Present)
            {
                throw new InvalidOperationException(
                    $"Replay event {_cursor:N0} targets missing/already-removed voxel {voxel} in '{_world.Profile.Id}'.");
            }

            if (!_world.State.MarkMined(voxel))
            {
                throw new InvalidOperationException(
                    $"Replay event {_cursor:N0} duplicates voxel {voxel} in '{_world.Profile.Id}'.");
            }

            _changedVoxels.Add(voxel);
            _view.SpawnManualMinePop(voxel, sample.BlockId);
            int seed = unchecked(voxel.X * 73856093
                ^ voxel.Y * 19349663
                ^ voxel.Z * 83492791
                ^ _cursor * 265443576);
            _view.SpawnMiningDebris(voxel, sample.BlockId, seed, "ReplayMiningDebris");
            _cursor++;
        }

        if (_cursor != appliedBefore)
        {
            _view.MarkDirtyBatch(_changedVoxels);
            Changed?.Invoke();
        }

        if (IsFinished)
        {
            IsPlaying = false;
            _sequenceSeconds = DurationSeconds;
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
