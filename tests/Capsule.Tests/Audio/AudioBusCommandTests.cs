using Capsule.Audio;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// The game thread's translation of a bus's gain and effects into the mix thread's commands.
public sealed class AudioBusCommandTests
{
    private static readonly AudioBus Fx = new("fx");

    // A bus first met through its gain or its effects is registered before the command reaches it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABusCommandForABusNeverPlayedOn_RegistersTheBusFirst(bool effects)
    {
        using Fixture fixture = new();
        fixture.Apply(Bus(effects ? AudioCommandKind.SetBusEffects : AudioCommandKind.SetBusGain, Fx, gain: 0.5f, EffectLevels.None));

        Assert.Equal(MixCommandKind.Buses, fixture.Posted[0].Kind);
        Assert.Single(fixture.Posted[0].Buses!);
        Assert.Equal(1, fixture.Posted[1].Bus);
    }

    // The line is the echo's delay at the output's rate, and is handed over whenever the delay changes,
    // even by less than a frame: a fade or an equal set keeps the line the bus has, and a delay of 0
    // drops it.
    [Fact]
    public void AnEchoLine_IsAllocatedOnlyWhenTheDelayChanges()
    {
        using Fixture fixture = new();
        EffectLevels echo = EffectLevels.None with { EchoDelaySeconds = 0.01f, EchoFeedback = 0.5f, EchoVolume = 0.5f };

        fixture.Apply(
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, echo),
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, echo with { EchoFeedback = 0.25f }),
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, echo with { EchoDelaySeconds = 0.010001f }),
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, echo with { EchoDelaySeconds = 0.02f }),
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, EffectLevels.None));

        Assert.Equal(
            [480 * 2, 0, 480 * 2, 960 * 2, 0],
            fixture.Posted.Select(static command => command.Line?.Length ?? 0));
        Assert.All(fixture.Posted, static command => Assert.Equal((MixCommandKind.SetBusEffects, 0), (command.Kind, command.Bus)));
    }

    // A bus's gain and effects are state, not a hold: they reach the mix while the host is suspended,
    // as at the end of a replay's fast-forward.
    [Fact]
    public void BusCommandsWhileSuspended_ReachTheMix()
    {
        using Fixture fixture = new();
        fixture.Player.Suspend();
        fixture.Apply(
            Bus(AudioCommandKind.SetBusGain, AudioBus.Master, 0.25f, default),
            Bus(AudioCommandKind.SetBusEffects, AudioBus.Master, 0f, EffectLevels.None with { LowPassHertz = 900f }));

        Assert.Equal(
            [(MixCommandKind.SetBusGain, 0.25f, 20_000f), (MixCommandKind.SetBusEffects, 0f, 900f)],
            fixture.Posted.Select(static command => (command.Kind, command.Gain, command.Kind == MixCommandKind.SetBusEffects ? command.Effects.LowPassHertz : 20_000f)));
    }

    private static AudioCommand Bus(AudioCommandKind kind, AudioBus bus, float gain, EffectLevels effects) =>
        new(kind, Voice.None, default, bus, gain, 0f, 0f, false, 0.0, effects);
}
