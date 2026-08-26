using System;
using System.Collections.Generic;
using Godot;
using TenMillionBlocks.Mining;
using TenMillionBlocks.World;
using TenMillionBlocks.World.Authoring;

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
    private readonly List<ReplayRemovalEvent> _events;
    private readonly ulong _startedUsec;
    private readonly int _minCoordinate;
    private readonly int _axisSize;
    private readonly string _worldContentHash;
    private readonly uint _tickOffset;
    private ulong _cachedProcessFrame = ulong.MaxValue;
    private uint _cachedTick;
    private bool _disposed;

    public ReplayRecorder(VirtualWorld world, MiningService mining, string? existingAbsolutePath = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mining = mining ?? throw new ArgumentNullException(nameof(mining));
        _minCoordinate = -world.MaxCoordinate;
        _axisSize = checked(world.MaxCoordinate * 2 + 1);
        _worldContentHash = WorldFreezeService.ComputeContentHash(world.Profile);
        _startedUsec = Time.GetTicksUsec();

        // Million-block worlds can record hundreds of thousands of exact removals. Reserve a modest
        // fraction up front so List<T> does not repeatedly allocate and copy an ever larger event buffer,
        // while still keeping tiny tutorial worlds tiny.
        int initialCapacity = (int)Math.Clamp(world.InitialMineableBlocks / 16L, 256L, 65_536L);
        _events = new List<ReplayRemovalEvent>(initialCapacity);

        if (!string.IsNullOrWhiteSpace(existingAbsolutePath) && System.IO.File.Exists(existingAbsolutePath))
        {
            ReplayData existing = ReplayBinaryCodec.Read(existingAbsolutePath);
            ValidateExisting(existing.Header);
            _events.EnsureCapacity(existing.Events.Count + 256);
            var seenIndices = new HashSet<long>(existing.Events.Count);
            foreach (ReplayRemovalEvent item in existing.Events)
            {
                if (seenIndices.Add(item.LinearIndex)
                    && _world.State.IsMined(FromLinearIndex(item.LinearIndex)))
                {
                    _events.Add(item);
                }
            }
            if (_events.Count != _world.State.MinedVoxelCount)
            {
                GD.PushWarning(
                    $"Replay for '{_world.Profile.Id}' is incomplete: save has {_world.State.MinedVoxelCount:N0} mined voxels " +
                    $"but replay contains {_events.Count:N0}. Recording will continue from the available history.");
            }
            _tickOffset = _events.Count == 0
                ? 0u
                : checked(_events[^1].Tick + 1u);
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
            WorldVersion = _world.Profile.WorldVersion,
            GenerationVersion = _world.Profile.GenerationVersion,
            WorldContentHash = _worldContentHash,
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

        long linearIndex = ToLinearIndex(result.Voxel);
        _events.Add(new ReplayRemovalEvent(CurrentReplayTick(), linearIndex, ReplaySourceMapper.FromMiningSource(result.Source)));
    }

    /// <summary>
    /// Hundreds of automation removals can happen synchronously inside one rendered frame. They should
    /// share a replay timestamp anyway, so query the wall clock once per process frame instead of once
    /// per block. This removes a native timer call from the hottest persistent-world event path without
    /// changing the 20 Hz replay timeline visible to the player.
    /// </summary>
    private uint CurrentReplayTick()
    {
        ulong processFrame = Engine.GetProcessFrames();
        if (_cachedProcessFrame == processFrame)
        {
            return _cachedTick;
        }

        _cachedProcessFrame = processFrame;
        ulong elapsedUsec = Time.GetTicksUsec() - _startedUsec;
        ulong sessionTicks = elapsedUsec * DefaultTickRate / 1_000_000UL;
        _cachedTick = checked((uint)Math.Min(uint.MaxValue, (ulong)_tickOffset + sessionTicks));
        return _cachedTick;
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
        bool legacyBaselineMatches = string.Equals(header.WorldId, _world.Profile.Id, StringComparison.Ordinal)
            && header.GenerationVersion == _world.Profile.GenerationVersion
            && header.MinCoordinate == _minCoordinate
            && header.AxisSize == _axisSize
            && header.TickRate == DefaultTickRate;

        if (!legacyBaselineMatches)
        {
            throw new InvalidOperationException(
                $"Replay baseline does not match world '{_world.Profile.Id}' generation {_world.Profile.GenerationVersion}.");
        }

        if (header.HasFrozenBaselineIdentity
            && (header.WorldVersion != _world.Profile.WorldVersion
                || !string.Equals(header.WorldContentHash, _worldContentHash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Replay was recorded for a different frozen version/content hash of world '{_world.Profile.Id}'.");
        }
    }
}
