using Capsule.Audio;
using Capsule.Input;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Audio;

internal static class AudioMixerFixtures
{
    internal static readonly AudioClip Step = new("step-soft", ".wav", 0.08);

    // The tick a voice on Step has run out by: its duration in steps at the run's default rate,
    // rounded up, rather than the 5 that rate happens to give.
    internal static readonly long StepEnds =
        (long)Math.Ceiling(Step.DurationSeconds * StepContext.DefaultStepHertz);

    internal static readonly AudioClip Theme = new("music/theme", ".ogg", 4.0);

    internal static readonly AudioBus Sfx = new("sfx");

    internal static readonly AudioBus Music = new("music");

    internal static void Advance(AudioMixer mixer, long tick) => mixer.BeginStep(SceneFixtures.Step(tick));

    internal static void Advance(AudioMixer mixer, long tick, int stepHertz) =>
        mixer.BeginStep(new StepContext(1.0 / stepHertz, new InputState(new ActionBindings()), tick));

    // The bus and gain of every SetBusGain the last step raised, in order.
    internal static (AudioBus Bus, float Gain)[] BusGains(AudioMixer mixer) =>
        [.. mixer.Commands.ToArray().Where(static command => command.Kind == AudioCommandKind.SetBusGain).Select(static command => (command.Bus!, command.Gain))];

    // The kind and voice of every command the last step raised, in order.
    internal static (AudioCommandKind Kind, Voice Voice)[] Raised(AudioMixer mixer) =>
        [.. mixer.Commands.ToArray().Select(static command => (command.Kind, command.Voice))];
}
