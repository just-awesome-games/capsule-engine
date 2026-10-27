using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Capsule.Bench.Logic.Scenes;
using Capsule.Runtime.Platform;

namespace Capsule.Bench;

// `latency [--label <text>] [--cap <hz>]`: plays Still in a child process while a thread in that child
// presses a key at random phases against the frames, and records each press from its event's SDL
// timestamp to the present of the first frame whose steps consumed it. Vertical sync paces the run
// unless --cap names a frame cap, which runs without it.
internal static class LatencyBench
{
    internal const string DirectoryOption = "--latency";
    internal const string CapOption = "--cap";

    private const string ArtifactsDirectory = "artifacts/bench/latency";

    // The child's frame budget. The injector waits for focus, lets two seconds pass, presses for
    // twenty, and leaves the rest for the last presses to reach a present.
    private const double RunSeconds = 25d;
    internal const int WarmUpMs = 2000;
    internal const int PressingMs = 20000;

    private const string Usage = "usage: Capsule.Bench latency [--label <text>] [--cap <hz>]";

    internal static int Run(string[] args)
    {
        string? label = null;
        int cap = 0;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--label" && index + 1 < args.Length && label is null)
            {
                label = args[++index];
                continue;
            }

            if (args[index] == CapOption && index + 1 < args.Length && cap == 0 && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out cap) && cap > 0)
            {
                index++;
                continue;
            }

            Console.Error.WriteLine($"unknown latency option '{args[index]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        label ??= "unlabelled";
        DateTime started = DateTime.UtcNow;
        string directory = Path.GetFullPath(ArtifactsDirectory);
        string frames = Path.Combine(directory, "frames.csv");
        Directory.CreateDirectory(directory);

        ProcessStartInfo start = new(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (string argument in (string[])["--scene", nameof(Still), "--frames", frames, RunSeconds.ToString(CultureInfo.InvariantCulture), DirectoryOption, directory])
        {
            start.ArgumentList.Add(argument);
        }

        if (cap > 0)
        {
            start.ArgumentList.Add(CapOption);
            start.ArgumentList.Add(cap.ToString(CultureInfo.InvariantCulture));
        }

        Console.WriteLine($"bench: latency ({(cap > 0 ? $"cap {cap} Hz, no vertical sync" : "vertical sync")}) for {RunSeconds} s. Leave the window in front.");
        using (Process child = Process.Start(start)!)
        {
            child.WaitForExit();
            if (child.ExitCode != 0)
            {
                Console.Error.WriteLine($"bench: the latency run exited with {child.ExitCode}.");
                return 1;
            }
        }

        List<(ulong Pressed, ulong Presented)> measured = [];
        foreach (string line in File.ReadLines(Path.Combine(directory, Injector.PressesFile)).Skip(1))
        {
            string[] fields = line.Split(',');
            measured.Add((ulong.Parse(fields[0], CultureInfo.InvariantCulture), ulong.Parse(fields[1], CultureInfo.InvariantCulture)));
        }

        string[] injection = File.ReadAllLines(Path.Combine(directory, Injector.InjectedFile));
        ulong[] injected = [.. injection.Skip(2).Select(static line => ulong.Parse(line, CultureInfo.InvariantCulture))];
        if (measured.Count == 0)
        {
            Console.Error.WriteLine($"bench: no press reached a step. {injected.Length} were injected. The window must hold focus.");
            return 1;
        }

        Percentiles latency = Percentiles.Of([.. measured.Select(static press => (press.Presented - press.Pressed) / 1e6)]);

        // The event's timestamp is the OS message's, which Windows keeps in whole ticks of its coarse
        // clock. Paired press for press, the injector's own clock bounds the error that adds.
        Percentiles? fromInjection = injected.Length == measured.Count
            ? Percentiles.Of([.. measured.Select((press, index) => (press.Presented - injected[index]) / 1e6)])
            : null;

        LatencyRecord record = new(
            Records.Timestamp(started),
            label,
            Machine.Commit(Records.SourceDirectory()),
            Machine.Configuration,
            Machine.NativeAot,
            Machine.Os,
            Machine.Cpu(),
            Machine.Gpu(),
            Machine.EngineVersion(),
            nameof(Still),
            VSync: cap == 0,
            Cap: cap,
            Injection: injection[0],
            Injected: injected.Length,
            Presses: measured.Count,
            LatencyMs: latency,
            InjectedLatencyMs: fromInjection);

        string path = Records.Save(record, started, "latency");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: latency presses={measured.Count}/{injected.Length} median={latency.Median:F3} p95={latency.P95:F3} max={latency.Max:F3} ms, from injection median={fromInjection?.Median:F3} p95={fromInjection?.P95:F3} max={fromInjection?.Max:F3}"));
        Console.WriteLine($"bench: wrote {path}");

        return 0;
    }
}

// The child's side: a background thread pressing and releasing F13 at random gaps and holds. On Windows
// each press is a scancode SendInput, which travels the OS queue to SDL's pump as a player's key does,
// and is sent only while this process's window is in front. Elsewhere the press is an SDL event pushed
// straight into SDL's queue, and the record says so.
internal static class Injector
{
    internal const string PressesFile = "presses.csv";
    internal const string InjectedFile = "injected.csv";

    // Set 1 scancode of F13, which nothing in the bench or the OS binds.
    private const ushort F13 = 0x64;
    private const uint KeyUpFlag = 0x0002;
    private const uint ScancodeFlag = 0x0008;

    internal static Thread Start(string directory, int warmUpMs, int pressingMs)
    {
        Thread thread = new(() => Press(directory, warmUpMs, pressingMs)) { IsBackground = true, Name = "bench-latency-injector" };
        thread.Start();

        return thread;
    }

    private static void Press(string directory, int warmUpMs, int pressingMs)
    {
        bool native = OperatingSystem.IsWindows();
        List<ulong> injected = new(capacity: 1024);
        Random random = new(1729);

        Stopwatch waiting = Stopwatch.StartNew();
        while (native && !InFront() && waiting.ElapsedMilliseconds < 15000)
        {
            Thread.Sleep(50);
        }

        Thread.Sleep(warmUpMs);
        Stopwatch pressing = Stopwatch.StartNew();
        while (pressing.ElapsedMilliseconds < pressingMs)
        {
            Thread.Sleep(random.Next(30, 110));
            if (native && !InFront())
            {
                continue;
            }

            injected.Add(Sdl3.SDL_GetTicksNS());
            Key(native, down: true);
            Thread.Sleep(random.Next(10, 45));
            Key(native, down: false);
        }

        File.WriteAllLines(Path.Combine(directory, InjectedFile), [native ? "SendInput" : "SDL_PushEvent", "injectedNs", .. injected.Select(static time => time.ToString(CultureInfo.InvariantCulture))]);
    }

    private static void Key(bool native, bool down)
    {
        if (native)
        {
            Input[] inputs = [new Input { Type = 1, Keyboard = new KeyboardInput { Scan = F13, Flags = ScancodeFlag | (down ? 0 : KeyUpFlag) } }];
            _ = SendInput(1, inputs, Marshal.SizeOf<Input>());
            return;
        }

        nint buffer = Marshal.AllocHGlobal(Sdl3.EventSize);
        try
        {
            Marshal.Copy(new byte[Sdl3.EventSize], 0, buffer, Sdl3.EventSize);
            Marshal.StructureToPtr(new Sdl3.KeyboardEvent { Type = down ? Sdl3.EventKeyDown : Sdl3.EventKeyUp, Scancode = 104, Down = down ? (byte)1 : (byte)0 }, buffer, fDeleteOld: false);
            Sdl3.SDL_PushEvent(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool InFront()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint process);

        return process == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    // INPUT with its union. The union is sized by MOUSEINPUT, its largest member.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;

        // Pads KEYBDINPUT to MOUSEINPUT's size: two ints and two uints before the same tail.
        public long Padding;
    }
}
