using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TenMillionBlocks.Replay;

/// <summary>
/// Compact replay codec. The event payload uses tick deltas and ZigZag-varint linear-index deltas,
/// then Brotli-compresses the stream. Schema 2 pinned replay identity to a frozen world hash; schema 3
/// additionally packs consecutive same-tick/same-source removals into one batch record while decoding
/// back to the same flat logical removal stream used by ReplayPlayer.
/// </summary>
public static class ReplayBinaryCodec
{
    private const int MinimumBatchLength = 3;
    private const int Sha256Length = 32;
    private const int MaxReplayPayloadBytes = 128 * 1024 * 1024;
    private const long MaxReplayEvents = 20_000_000L;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CMBR");

    public static void Write(string absolutePath, ReplayHeader header, IReadOnlyList<ReplayRemovalEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(events);
        if (header.WorldVersion <= 0)
        {
            throw new InvalidDataException("Replay schema 3 requires a positive world version.");
        }
        if (string.IsNullOrWhiteSpace(header.WorldContentHash))
        {
            throw new InvalidDataException("Replay schema 3 requires a frozen world content hash.");
        }

        byte[] eventBytes = EncodeEvents(events);
        byte[] checksum = SHA256.HashData(eventBytes);
        byte[] compressed = Compress(eventBytes);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? ".");
        string temp = absolutePath + ".tmp";
        using (FileStream stream = File.Create(temp))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(Magic);
            writer.Write(ReplayHeader.CurrentSchemaVersion);
            WriteString(writer, header.WorldId);
            writer.Write(header.WorldVersion);
            writer.Write(header.GenerationVersion);
            WriteString(writer, header.WorldContentHash);
            writer.Write(header.MinCoordinate);
            writer.Write(header.AxisSize);
            writer.Write(header.TickRate);
            writer.Write((long)events.Count);
            writer.Write(header.FinalMinedCount);
            writer.Write(eventBytes.Length);
            writer.Write(compressed.Length);
            writer.Write(checksum.Length);
            writer.Write(checksum);
            writer.Write(compressed);
        }

        File.Move(temp, absolutePath, overwrite: true);
    }

    public static ReplayData Read(string absolutePath)
    {
        using FileStream stream = File.OpenRead(absolutePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        byte[] magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Replay has invalid magic bytes.");
        }

        int schema = reader.ReadInt32();
        if (schema < ReplayHeader.MinimumReadableSchemaVersion || schema > ReplayHeader.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported replay schema {schema}.");
        }

        string worldId = ReadString(reader);
        int worldVersion = 0;
        int generationVersion;
        string worldContentHash = string.Empty;
        if (schema >= 2)
        {
            worldVersion = reader.ReadInt32();
            generationVersion = reader.ReadInt32();
            worldContentHash = ReadString(reader);
        }
        else
        {
            // Schema 1 predates immutable world-version/hash identity. It can still be viewed after
            // its older generation/bounds checks pass.
            generationVersion = reader.ReadInt32();
        }

        int minCoordinate = reader.ReadInt32();
        int axisSize = reader.ReadInt32();
        int tickRate = reader.ReadInt32();
        long eventCount = reader.ReadInt64();
        long finalMinedCount = reader.ReadInt64();
        int rawLength = reader.ReadInt32();
        int compressedLength = reader.ReadInt32();
        int checksumLength = reader.ReadInt32();

        // Validate every declared allocation before reading variable-length data from disk. Corrupt
        // local replay files must fail as data errors, not turn into giant allocations or OOM failures.
        if (rawLength < 0 || compressedLength < 0 || eventCount < 0 || finalMinedCount < 0
            || axisSize <= 0 || tickRate <= 0)
        {
            throw new InvalidDataException("Replay header contains invalid sizes.");
        }
        if (rawLength > MaxReplayPayloadBytes || compressedLength > MaxReplayPayloadBytes)
        {
            throw new InvalidDataException("Replay payload exceeds the supported size limit.");
        }
        if (eventCount > MaxReplayEvents)
        {
            throw new InvalidDataException($"Replay contains {eventCount:N0} events; the supported limit is {MaxReplayEvents:N0}.");
        }
        if (schema >= 2 && (worldVersion <= 0 || string.IsNullOrWhiteSpace(worldContentHash)))
        {
            throw new InvalidDataException($"Replay schema {schema} is missing frozen world identity.");
        }
        if (checksumLength != Sha256Length)
        {
            throw new InvalidDataException("Replay checksum length is invalid.");
        }

        long remaining = stream.Length - stream.Position;
        long declaredTail = (long)checksumLength + compressedLength;
        if (remaining < declaredTail)
        {
            throw new EndOfStreamException("Replay ended before its declared payload completed.");
        }

        byte[] checksum = reader.ReadBytes(checksumLength);
        byte[] compressed = reader.ReadBytes(compressedLength);
        if (compressed.Length != compressedLength || checksum.Length != checksumLength)
        {
            throw new EndOfStreamException("Replay ended before its declared payload completed.");
        }

        byte[] eventBytes = Decompress(compressed, rawLength);
        byte[] actualChecksum = SHA256.HashData(eventBytes);
        if (!CryptographicOperations.FixedTimeEquals(checksum, actualChecksum))
        {
            throw new InvalidDataException("Replay event checksum mismatch.");
        }

        List<ReplayRemovalEvent> events = DecodeEvents(eventBytes, eventCount);
        return new ReplayData
        {
            Header = new ReplayHeader
            {
                SchemaVersion = schema,
                WorldId = worldId,
                WorldVersion = worldVersion,
                GenerationVersion = generationVersion,
                WorldContentHash = worldContentHash,
                MinCoordinate = minCoordinate,
                AxisSize = axisSize,
                TickRate = tickRate,
                EventCount = eventCount,
                FinalMinedCount = finalMinedCount,
                EventChecksum = checksum,
            },
            Events = events,
        };
    }

    public static byte[] EncodeEvents(IReadOnlyList<ReplayRemovalEvent> events)
    {
        using var stream = new MemoryStream();
        uint previousTick = 0;
        long previousIndex = 0;
        int cursor = 0;

        while (cursor < events.Count)
        {
            ReplayRemovalEvent first = events[cursor];
            if (first.Tick < previousTick)
            {
                throw new InvalidDataException("Replay events must be ordered by nondecreasing tick.");
            }

            int runLength = 1;
            while (cursor + runLength < events.Count)
            {
                ReplayRemovalEvent next = events[cursor + runLength];
                if (next.Tick < first.Tick)
                {
                    throw new InvalidDataException("Replay events must be ordered by nondecreasing tick.");
                }
                if (next.Tick != first.Tick || next.Source != first.Source) break;
                runLength++;
            }

            if (runLength >= MinimumBatchLength)
            {
                stream.WriteByte((byte)ReplayEventKind.RemoveVoxelBatch);
                WriteVarUInt(stream, first.Tick - previousTick);
                WriteVarUInt(stream, checked((ulong)runLength));
                stream.WriteByte((byte)first.Source);

                for (int i = 0; i < runLength; i++)
                {
                    ReplayRemovalEvent item = events[cursor + i];
                    WriteVarUInt(stream, ZigZag(item.LinearIndex - previousIndex));
                    previousIndex = item.LinearIndex;
                }

                previousTick = first.Tick;
                cursor += runLength;
                continue;
            }

            stream.WriteByte((byte)ReplayEventKind.RemoveVoxel);
            WriteVarUInt(stream, first.Tick - previousTick);
            WriteVarUInt(stream, ZigZag(first.LinearIndex - previousIndex));
            stream.WriteByte((byte)first.Source);
            previousTick = first.Tick;
            previousIndex = first.LinearIndex;
            cursor++;
        }

        return stream.ToArray();
    }

    public static List<ReplayRemovalEvent> DecodeEvents(ReadOnlySpan<byte> bytes, long expectedCount)
    {
        if (expectedCount > int.MaxValue)
        {
            throw new InvalidDataException($"Replay event count {expectedCount:N0} exceeds supported in-memory playback size.");
        }

        var result = new List<ReplayRemovalEvent>((int)Math.Max(0L, expectedCount));
        int offset = 0;
        uint tick = 0;
        long index = 0;

        while (offset < bytes.Length)
        {
            ReplayEventKind kind = (ReplayEventKind)bytes[offset++];
            switch (kind)
            {
                case ReplayEventKind.RemoveVoxel:
                    tick = checked(tick + (uint)ReadVarUInt(bytes, ref offset));
                    index = checked(index + UnZigZag(ReadVarUInt(bytes, ref offset)));
                    ReplayMiningSource source = ReadSource(bytes, ref offset);
                    result.Add(new ReplayRemovalEvent(tick, index, source));
                    break;

                case ReplayEventKind.RemoveVoxelBatch:
                    tick = checked(tick + (uint)ReadVarUInt(bytes, ref offset));
                    ulong rawCount = ReadVarUInt(bytes, ref offset);
                    if (rawCount == 0 || rawCount > int.MaxValue)
                    {
                        throw new InvalidDataException($"Replay batch length {rawCount:N0} is invalid.");
                    }
                    if ((long)result.Count + (long)rawCount > expectedCount)
                    {
                        throw new InvalidDataException("Replay batch exceeds the event count declared by the header.");
                    }

                    ReplayMiningSource batchSource = ReadSource(bytes, ref offset);
                    int batchCount = checked((int)rawCount);
                    for (int i = 0; i < batchCount; i++)
                    {
                        index = checked(index + UnZigZag(ReadVarUInt(bytes, ref offset)));
                        result.Add(new ReplayRemovalEvent(tick, index, batchSource));
                    }
                    break;

                default:
                    throw new InvalidDataException($"Unknown replay event kind {(byte)kind}.");
            }
        }

        if (result.Count != expectedCount)
        {
            throw new InvalidDataException($"Replay decoded {result.Count} events; header declared {expectedCount}.");
        }

        return result;
    }

    private static ReplayMiningSource ReadSource(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset >= bytes.Length) throw new EndOfStreamException("Replay source byte is missing.");
        ReplayMiningSource source = (ReplayMiningSource)bytes[offset++];
        if (!Enum.IsDefined(source))
        {
            throw new InvalidDataException($"Replay contains unknown mining source {(byte)source}.");
        }
        return source;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, int expectedLength)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = expectedLength > 0 ? new MemoryStream(expectedLength) : new MemoryStream();
        byte[] buffer = new byte[81_920];
        int total = 0;

        while (true)
        {
            int read = brotli.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            total = checked(total + read);
            if (total > expectedLength)
            {
                throw new InvalidDataException("Replay decompressed beyond its declared payload length.");
            }
            output.Write(buffer, 0, read);
        }

        if (total != expectedLength)
        {
            throw new InvalidDataException($"Replay decompressed to {total} bytes; header declared {expectedLength}.");
        }
        return output.ToArray();
    }

    private static ulong ZigZag(long value)
        => unchecked((ulong)((value << 1) ^ (value >> 63)));

    private static long UnZigZag(ulong value)
        => unchecked((long)(value >> 1) ^ -((long)value & 1L));

    private static void WriteVarUInt(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static ulong ReadVarUInt(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            if (offset >= bytes.Length) throw new EndOfStreamException("Replay varint was truncated.");
            byte current = bytes[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return value;
            shift += 7;
            if (shift >= 64) throw new InvalidDataException("Replay varint is too long.");
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 1_048_576) throw new InvalidDataException("Replay string length is invalid.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Replay string was truncated.");
        return Encoding.UTF8.GetString(bytes);
    }
}
