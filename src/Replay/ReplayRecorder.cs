using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.World;

namespace TenMillionBlocks.Replay;

/// <summary>
/// Records only accepted authoritative block removals. Camera/UI/automation decisions are intentionally
/// absent: replay playback reconstructs the visible mining history against the frozen deterministic
/// world baseline.
/// </summary>
public sealed class ReplayRecorder : IDisposable
{
    public const int DefaultTickRate = 20;

    private readonly VirtualWorld _world;
    private readonly MiningService _mining;
    private readonly List<ReplayRemovalEvent> _events = new();
    private readonly ulong _startedUsec;
    private readonly int _minCoordinate;
    private readonly int _axisSize;
    private readonly uint _tickOffset;
    private bool _disposed;

    public ReplayRecorder(VirtualWorld world, MiningService mining, string? existingAbsolutePath = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _minCoordinate = -world.MaxCoordinate;
        _axisSize = checked(world.MaxCoordinate * 2 + 1);
        _startedUsec = Time.GetTicksUsec();

        if (!string.IsNullOrWhiteSpace(existingAbsolutePath) && System.IO.File.Exists(existingAbsolutePath))
        {
            ReplayData existing = ReplayBinaryCodec.Read(existingAbsolutePath);
            ValidateExisting(existing.Header);
            _events.AddRange(existing.Events);
            _tickOffset = existing.Events.Count == 0
                ? 0u
                : checked(existing.Events[^1].Tick + 1u);
        }

        _mining.BlockMined += OnBlockMined;
    }

    public int EventCount => _events.Count;
    public IReadOnlyList<ReplayRemovalEvent> Events => _events;

    public string FlushToUserPath(string relativePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ReplayRecorder));
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Replay path is empty.", nameof(relativePath));

        string absolute = ProjectSettings.GlobalizePath(relativePath);
        ReplayBinaryCodec.Write(absolute, CreateHeader(), _events);
        return relativePath;
    }

    public ReplayHeader CreateHeader()
        => new()
        {
            WorldId = _world.Profile.Id,
            GenerationVersion = _world.Profile.GenerationVersion,
            MinCoordinate = _minCoordinate,
            AxisSize = _axisSize,
            TickRate = DefaultTickRate,
            EventCount = _events.Count,
            FinalMinedCount = _world.State.MinedVoxelCount,
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mining.BlockMined -= OnBlockMined;
    }

    private void OnBlockMined(MiningResult result)
    {
        if (!result.Success || !result.Removed || result.BlocksRemoved <= 0) return;

        ulong elapsedUsec = Time.GetTicksUsec() - _startedUsec;
        ulong sessionTicks = elapsedUsec * DefaultTickRate / 1_000_000UL;
        uint tick = checked((uint)Math.Min(uint.MaxValue, (ulong)_tickOffset + sessionTicks));
        long linearIndex = ToLinearIndex(result.Voxel);
        _events.Add(new ReplayRemovalEvent(tick, linearIndex, ReplaySourceMapper.FromMiningSource(result.Source)));
    }

    private long ToLinearIndex(Vector3I voxel)
    {
        long x = voxel.X - (long)_minCoordinate;
        long y = voxel.Y - (long)_minCoordinate;
        long z = voxel.Z - (long)_minCoordinate;
        if ((ulong)x >= (ulong)_axisSize || (ulong)y >= (ulong)_axisSize || (ulong)z >= (ulong)_axisSize)
        {
            throw new InvalidOperationException($"Replay voxel {voxel} is outside frozen address bounds.");
        }

        return checked(x + (long)_axisSize * (y + (long)_axisSize * z));
    }

    public Vector3I FromLinearIndex(long index)
    {
        long volume = checked((long)_axisSize * _axisSize * _axisSize);
        if (index < 0 || index >= volume) throw new ArgumentOutOfRangeException(nameof(index));

        long x = index % _axisSize;
        long rest = index / _axisSize;
        long y = rest % _axisSize;
        long z = rest / _axisSize;
        return new Vector3I(
            checked((int)(x + _minCoordinate)),
            checked((int)(y + _minCoordinate)),
            checked((int)(z + _minCoordinate)));
    }

    private void ValidateExisting(ReplayHeader header)
    {
        if (!string.Equals(header.WorldId, _world.Profile.Id, StringComparison.Ordinal)
            || header.GenerationVersion != _world.Profile.GenerationVersion
            || header.MinCoordinate != _minCoordinate
            || header.AxisSize != _axisSize
            || header.TickRate != DefaultTickRate)
        {
            throw new InvalidOperationException(
                $"Replay baseline does not match world '{_world.Profile.Id}' generation {_world.Profile.GenerationVersion}.");
        }
    }
}
