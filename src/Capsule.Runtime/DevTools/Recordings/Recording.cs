using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime.DevTools.Recordings;

namespace Capsule.Runtime;

/// <summary>
/// One run of a game as a windowed development run recorded it, from boot to the tick it was saved,
/// which <see cref="EngineBuilder.RunHeadless(Recording, Action{Capsule.Scenes.Scene, long})"/> replays exactly.
/// </summary>
/// <remarks>
/// A file holds the run's seed, opening scene, restored saves and every step's input, and checksums of
/// the run's state once a simulated second. <c>docs/debugging.md</c> says where runs are saved.
/// </remarks>
public sealed class Recording
{
    // What a shipping build answers a replay with. Replaying is development tooling, and a trimmed
    // publish drops the decoder.
    internal const string ShippingRefusal =
        "Recordings replay only where the development plane is on, and this is a shipping build. Replay it from a development build, such as a test project or dotnet run.";

    internal RecordingHeader Header { get; }

    // The whole file. A replay reads the body from BodyStart.
    internal byte[] File { get; }

    internal int BodyStart { get; }

    internal RecordingEnd End { get; }

    // Every checkpoint in tick order, the end's own last when it carries one.
    internal (long Tick, ulong Checksum)[] Checkpoints { get; }

    private Recording(RecordingHeader header, byte[] file, int bodyStart, RecordingEnd end, (long Tick, ulong Checksum)[] checkpoints)
    {
        Header = header;
        File = file;
        BodyStart = bodyStart;
        End = end;
        Checkpoints = checkpoints;
    }

    /// <summary>Reads the recording at <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">
    /// The file is not a recording, is cut short or damaged, or a newer Capsule wrote it.
    /// </exception>
    /// <exception cref="InvalidOperationException"><see cref="Development.IsSupported"/> is false.</exception>
    public static Recording Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Development.IsSupported)
        {
            throw new InvalidOperationException(ShippingRefusal);
        }

        return Read(System.IO.File.ReadAllBytes(path), path);
    }

    // Validates the whole file: the header, every record of the body and the end.
    internal static Recording Read(byte[] file, string source)
    {
        ReadOnlySpan<byte> magic = RecordingFormat.Magic;
        if (file.Length < magic.Length || !file.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new InvalidDataException(
                $"'{source}' is not a Capsule recording: it does not begin with the recording magic. Pass a .recording file a development run saved.");
        }

        RecordingReader reader = new(file, magic.Length, source);
        int version = reader.ReadCount(int.MaxValue);
        if (version > RecordingFormat.Version)
        {
            throw new InvalidDataException(
                $"The recording '{source}' is format version {version}, and this Capsule reads up to version {RecordingFormat.Version}. "
                + "Replay it with the Capsule that recorded it, or record the run again.");
        }

        RecordingHeader header = RecordingHeader.Read(reader);
        int bodyStart = reader.Position;
        List<(long, ulong)> checkpoints = [];
        RecordingBody body = new(reader);
        long steps = 0;
        while (body.Next(out RecordingEvent recorded))
        {
            switch (recorded.Tag)
            {
                case RecordingFormat.Step:
                    steps += recorded.Steps;
                    break;
                case RecordingFormat.Checkpoint:
                    checkpoints.Add((recorded.Tick, recorded.Checksum));
                    break;
            }
        }

        RecordingEnd end = body.End;
        if (end.FinalTick != steps)
        {
            throw reader.Defect($"ends at tick {end.FinalTick} but holds input for {steps} steps");
        }

        if (!reader.AtEnd)
        {
            throw reader.Defect($"carries bytes after its end, at byte {reader.Position}");
        }

        if (end.Checksum is { } checksum)
        {
            checkpoints.Add((end.FinalTick, checksum));
        }

        return new Recording(header, file, bodyStart, end, [.. checkpoints]);
    }
}

// What a recording ends with: the steps it holds, the checksum after the last of them unless the run
// crashed inside it, and whether the run's last step asked to exit.
internal readonly record struct RecordingEnd(long FinalTick, ulong? Checksum, bool Exited);

internal readonly record struct RecordedSave(string Name, string Document, SaveMetadata Metadata);

// The run's boot state and the shell settings the simulation reads. A recorder sets the saves as the
// run's medium restores them, and the key layout and the initial device at the run's first step.
internal sealed class RecordingHeader
{
    internal string GameVersion { get; init; } = "";

    internal string CapsuleVersion { get; init; } = "";

    internal ulong Seed { get; init; }

    internal double StepSeconds { get; init; }

    internal Vector2 Canvas { get; init; }

    internal (int Width, int Height)? RenderResolution { get; init; }

    // A scene class name, or a scene document key when OpensDocument.
    internal string Scene { get; init; } = "";

    internal bool OpensDocument { get; init; }

    internal string? Payload { get; init; }

    // Null when the run asked the platform for none.
    internal string[]? PreferredLanguages { get; init; }

    internal string? LanguageOverride { get; init; }

    internal IReadOnlyList<RecordedSave> Saves { get; set; } = [];

    // Null for a US layout's characters.
    internal string?[]? KeyLayout { get; set; }

    internal InputDevice Device { get; set; }

    internal static RecordingHeader Read(RecordingReader reader)
    {
        string game = reader.ReadString();
        string capsule = reader.ReadString();
        ulong seed = reader.ReadUInt64();
        double stepSeconds = reader.ReadDouble();
        Vector2 canvas = reader.ReadVector();
        (int, int)? resolution = reader.ReadByte() == 0 ? null : ((int)reader.ReadUInt32(), (int)reader.ReadUInt32());
        bool document = reader.ReadByte() != 0;
        string scene = reader.ReadString();
        string? payload = ReadOptional(reader);
        string[]? languages = null;
        if (reader.ReadByte() != 0)
        {
            languages = new string[reader.ReadCount(1024)];
            for (int index = 0; index < languages.Length; index++)
            {
                languages[index] = reader.ReadString();
            }
        }

        string? language = ReadOptional(reader);
        RecordedSave[] saves = new RecordedSave[reader.ReadCount(1 << 20)];
        for (int index = 0; index < saves.Length; index++)
        {
            string name = reader.ReadString();
            string json = reader.ReadString();
            DateTimeOffset created = ReadTime(reader);
            saves[index] = new RecordedSave(name, json, new SaveMetadata(created, ReadTime(reader)));
        }

        string?[]? layout = null;
        if (reader.ReadByte() != 0)
        {
            layout = new string?[reader.ReadCount(1024)];
            for (int index = 0; index < layout.Length; index++)
            {
                layout[index] = ReadOptional(reader);
            }
        }

        byte device = reader.ReadByte();
        if (device > (byte)InputDevice.Gamepad)
        {
            throw reader.Defect($"names input device {device}, which is none Capsule has");
        }

        return new RecordingHeader
        {
            GameVersion = game,
            CapsuleVersion = capsule,
            Seed = seed,
            StepSeconds = stepSeconds,
            Canvas = canvas,
            RenderResolution = resolution,
            OpensDocument = document,
            Scene = scene,
            Payload = payload,
            PreferredLanguages = languages,
            LanguageOverride = language,
            Saves = saves,
            KeyLayout = layout,
            Device = (InputDevice)device,
        };
    }

    internal void Write(ChunkedBuffer writer)
    {
        writer.WriteString(GameVersion);
        writer.WriteString(CapsuleVersion);
        writer.WriteUInt64(Seed);
        writer.WriteDouble(StepSeconds);
        writer.WriteVector(Canvas);
        writer.WriteByte(RenderResolution is null ? (byte)0 : (byte)1);
        if (RenderResolution is { } resolution)
        {
            writer.WriteUInt32((uint)resolution.Width);
            writer.WriteUInt32((uint)resolution.Height);
        }

        writer.WriteByte(OpensDocument ? (byte)1 : (byte)0);
        writer.WriteString(Scene);
        WriteOptional(writer, Payload);
        writer.WriteByte(PreferredLanguages is null ? (byte)0 : (byte)1);
        if (PreferredLanguages is { } languages)
        {
            writer.WriteVarint((ulong)languages.Length);
            foreach (string language in languages)
            {
                writer.WriteString(language);
            }
        }

        WriteOptional(writer, LanguageOverride);
        writer.WriteVarint((ulong)Saves.Count);
        foreach (RecordedSave save in Saves)
        {
            writer.WriteString(save.Name);
            writer.WriteString(save.Document);
            WriteTime(writer, save.Metadata.CreatedAt);
            WriteTime(writer, save.Metadata.UpdatedAt);
        }

        writer.WriteByte(KeyLayout is null ? (byte)0 : (byte)1);
        if (KeyLayout is { } layout)
        {
            writer.WriteVarint((ulong)layout.Length);
            foreach (string? character in layout)
            {
                WriteOptional(writer, character);
            }
        }

        writer.WriteByte((byte)Device);
    }

    private static void WriteOptional(ChunkedBuffer writer, string? value)
    {
        writer.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null)
        {
            writer.WriteString(value);
        }
    }

    private static string? ReadOptional(RecordingReader reader) => reader.ReadByte() == 0 ? null : reader.ReadString();

    // UTC ticks, then the offset in minutes.
    private static void WriteTime(ChunkedBuffer writer, DateTimeOffset time)
    {
        writer.WriteUInt64((ulong)time.UtcTicks);
        writer.WriteUInt32((uint)(int)time.Offset.TotalMinutes);
    }

    private static DateTimeOffset ReadTime(RecordingReader reader)
    {
        long ticks = (long)reader.ReadUInt64();
        int minutes = (int)reader.ReadUInt32();
        try
        {
            return new DateTimeOffset(ticks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(minutes));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw reader.Defect($"holds a save time out of range before byte {reader.Position}");
        }
    }
}
