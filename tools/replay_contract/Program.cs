using System.Diagnostics;
using TenMillionBlocks.Replay;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectReadFailure(string path, string label)
{
    try
    {
        _ = ReplayBinaryCodec.Read(path);
    }
    catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
    {
        return;
    }

    throw new InvalidOperationException($"Replay decoder accepted {label} fixture.");
}

const int eventCount = 125_000;
var events = new List<ReplayRemovalEvent>(eventCount);
for (int i = 0; i < eventCount; i++)
{
    uint tick = (uint)(i / 24);
    ReplayMiningSource source = i % 4096 < 48
        ? ReplayMiningSource.WorldEvent
        : ReplayMiningSource.Automation;
    long index = i;
    events.Add(new ReplayRemovalEvent(tick, index, source));
}

var stopwatch = Stopwatch.StartNew();
byte[] packed = ReplayBinaryCodec.EncodeEvents(events);
stopwatch.Stop();
double encodeMs = stopwatch.Elapsed.TotalMilliseconds;
Require(packed.Length > 0, "Replay encoder returned an empty stream.");
Require(packed[0] == (byte)ReplayEventKind.RemoveVoxelBatch,
    "Representative replay did not use schema-3 batch packing at the first same-tick run.");
Require(packed.Length / (double)eventCount <= 4.0,
    $"Packed replay regressed above 4 bytes/block: {packed.Length / (double)eventCount:0.00}.");

stopwatch.Restart();
List<ReplayRemovalEvent> decoded = ReplayBinaryCodec.DecodeEvents(packed, eventCount);
stopwatch.Stop();
double decodeMs = stopwatch.Elapsed.TotalMilliseconds;
Require(decoded.Count == events.Count, "Replay round-trip changed event count.");
for (int i = 0; i < events.Count; i++)
{
    Require(decoded[i] == events[i], $"Replay round-trip diverged at logical event {i:N0}.");
}

string directory = Path.Combine(Path.GetTempPath(), "ten-million-blocks-replay-contract");
Directory.CreateDirectory(directory);
string validPath = Path.Combine(directory, "representative.cmbreplay");
var header = new ReplayHeader
{
    WorldId = "reference_ridges",
    WorldVersion = 2,
    GenerationVersion = 2,
    WorldContentHash = new string('a', 64),
    MinCoordinate = -32,
    AxisSize = 65,
    TickRate = 20,
    EventCount = eventCount,
    FinalMinedCount = eventCount,
};
ReplayBinaryCodec.Write(validPath, header, events);
ReplayData fileRoundTrip = ReplayBinaryCodec.Read(validPath);
Require(fileRoundTrip.Header.SchemaVersion == ReplayHeader.CurrentSchemaVersion,
    "Replay writer did not emit the current schema version.");
Require(fileRoundTrip.Events.Count == eventCount, "Replay file round-trip changed event count.");

byte[] fileBytes = File.ReadAllBytes(validPath);
string corruptPath = Path.Combine(directory, "corrupt.cmbreplay");
byte[] corrupt = (byte[])fileBytes.Clone();
corrupt[^1] ^= 0x5A;
File.WriteAllBytes(corruptPath, corrupt);
ExpectReadFailure(corruptPath, "corrupt checksum/payload");

string futurePath = Path.Combine(directory, "future-schema.cmbreplay");
byte[] future = (byte[])fileBytes.Clone();
BitConverter.GetBytes(ReplayHeader.CurrentSchemaVersion + 99).CopyTo(future, 4);
File.WriteAllBytes(futurePath, future);
ExpectReadFailure(futurePath, "unsupported future schema");

double packedPerBlock = packed.Length / (double)eventCount;
double filePerBlock = fileBytes.Length / (double)eventCount;
Console.WriteLine(
    $"replay contract passed: schema={ReplayHeader.CurrentSchemaVersion}, events={eventCount:N0}, " +
    $"packed={packed.Length:N0} bytes ({packedPerBlock:0.00}/block), " +
    $"brotli file={fileBytes.Length:N0} bytes ({filePerBlock:0.00}/block), " +
    $"encode={encodeMs:0.0}ms decode={decodeMs:0.0}ms");
