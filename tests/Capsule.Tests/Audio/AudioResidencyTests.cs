using Capsule.Audio;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

public sealed class AudioResidencyTests
{
    // An .ogg past the cutoff streams and is never resident. One at or under it is preloaded and held
    // like a .wav, which spares an effect a decoder's setup on every play.
    [Theory]
    [InlineData(AudioClip.ResidentVorbisSeconds + 0.01, true)]
    [InlineData(AudioClip.ResidentVorbisSeconds, false)]
    public void AnOggStreamsOnlyPastTheResidentCutoff(double seconds, bool streamed)
    {
        using Fixture fixture = new();
        AudioClip clip = new("clip", ".ogg", seconds);

        fixture.Preload(clip);
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), clip));

        Assert.Equal(streamed ? [] : [clip], fixture.Loaded);
        Assert.Equal(streamed ? [clip] : [], fixture.Opened);
        MixCommand play = Assert.Single(fixture.Posted);
        Assert.Equal(streamed, play.Stream is not null);
        Assert.Equal(streamed, play.Samples is null);
    }

    // A voice holds the samples it plays, and a scene's release only drops the store's hold. A play
    // that outlives the scene keeps its samples, and the clip's next play loads it again.
    [Fact]
    public void AClipItsSceneReleased_IsLoadedAgainOnItsNextPlay()
    {
        using Fixture fixture = new();
        fixture.Preload(Step);
        fixture.Apply(Command(AudioCommandKind.Play, Slot(0, 1), Step));

        fixture.Preload();
        fixture.Apply(Command(AudioCommandKind.Play, Slot(1, 1), Step));

        Assert.Equal([Step, Step], fixture.Loaded);
        Assert.NotSame(fixture.Posted[0].Samples, fixture.Posted[1].Samples);
    }

    // The initial scene starts before the device is open. What its start raised stands on the
    // mixer with no step behind it. A boot that only waited for the first step's commands would lose
    // it: BeginStep clears the table the host has not applied yet.
    [Fact]
    public void ASourceThatPlaysOnStart_SoundsOnTheBootThatOpensTheDevice()
    {
        StartupScene scene = new();

        using SceneHost host = new(SceneTransition.ToScene(typeof(StartupScene), null), (in SceneTransition _) => scene, new Run());
        using Fixture fixture = new();

        // The order LoadContent boots in: the scene's preloads, then whatever its start left standing.
        fixture.Preload(Step);
        fixture.Player.Apply(host.Run.Audio.Commands);

        MixCommand sounding = Assert.Single(fixture.Posted);
        Assert.Equal(MixCommandKind.Play, sounding.Kind);
        Assert.True(sounding.Loop);
        Assert.Equal([Step], fixture.Loaded);

        // Nothing carries it past here, which is what makes the boot delivery the only chance at it.
        host.Step(SceneFixtures.Step(0));

        Assert.Empty(host.Run.Audio.Commands.ToArray());
    }
}
