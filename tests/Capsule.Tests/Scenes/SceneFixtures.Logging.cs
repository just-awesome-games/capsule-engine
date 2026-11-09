using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The fixtures that log the hooks they run, each as a name and its hook's mark.
internal static partial class SceneFixtures
{
    /// <param name="logsStart">Also log <c>name!</c> as time begins. Off by default, which keeps a log
    /// cleared before the scene starts a record of the steps alone.</param>
    internal sealed class Recorder(string name, List<string> log, bool logsStart = false) : Entity(Vector2.Zero)
    {
        protected internal override void OnStart()
        {
            if (logsStart)
            {
                log.Add($"{name}!");
            }
        }

        protected internal override void OnStep(in StepContext context) => log.Add(name);

        protected internal override void OnLateStep(in StepContext context) => log.Add($"{name}.late");

        protected internal override void OnAddedToScene() => log.Add($"{name}+");

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }

    /// <summary>
    /// The hooks a <see cref="RecordingComponent"/> logs, worded as <see cref="Recorder"/> words them: <c>name+</c>
    /// as it joins, <c>name!</c> as it starts, <c>name</c> and <c>name.late</c> in each step, <c>name-</c> as it leaves.
    /// </summary>
    [Flags]
    internal enum Logs
    {
        Join = 1,
        Start = 2,
        Step = 4,
        LateStep = 8,
        Leave = 16,
    }

    /// <param name="logs">The hooks it logs, by default its steps alone.</param>
    internal sealed class RecordingComponent(string name, List<string> log, Logs logs = Logs.Step | Logs.LateStep) : Component
    {
        protected internal override void OnAddedToScene() => Write(Logs.Join, "+");

        protected internal override void OnStart() => Write(Logs.Start, "!");

        protected internal override void OnStep(in StepContext context) => Write(Logs.Step, string.Empty);

        protected internal override void OnLateStep(in StepContext context) => Write(Logs.LateStep, ".late");

        protected internal override void OnRemovedFromScene() => Write(Logs.Leave, "-");

        private void Write(Logs hook, string suffix)
        {
            if (logs.HasFlag(hook))
            {
                log.Add(name + suffix);
            }
        }
    }

    internal sealed class Twin(string name, List<string> log) : Entity(Vector2.Zero)
    {
        public override bool Equals(object? obj) => obj is Twin;

        public override int GetHashCode() => 0;

        protected internal override void OnRemovedFromScene() => log.Add($"{name}-");
    }
}
