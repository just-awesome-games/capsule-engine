using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Scenes;
using Capsule.Scenes.Audio;

namespace Capsule.Tests.Audio;

public sealed class AudioSourceTests
{
    private static readonly AudioClip Step = new("step-soft", ".wav", 0.08);

    [Fact]
    public void PlayOnStart_StartsTheSourceWhereTheSceneStartsIt()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        Assert.True(source.IsPlaying);
        Assert.Equal(AudioCommandKind.Play, run.Scene.Audio.Commands[0].Kind);
    }

    // The run's step length only reaches the mixer on the first step, after OnStart has played:
    // 0.08 s is 9.6 steps at 120 Hz, never the 4.8 the default rate would make of it.
    [Fact]
    public void PlayOnStart_UnderANonDefaultRate_LastsTheClipsOwnDuration()
    {
        AudioSource source = new(Step) { PlayOnStart = true };
        using SceneRun run = Run(source, stepHertz: 120);

        run.Run(10);
        Assert.True(source.IsPlaying);

        run.Step();

        Assert.False(source.IsPlaying);
    }

    [Fact]
    public void ASourceThatDoesNotPlayOnStart_StaysSilentUntilItIsPlayed()
    {
        AudioSource source = new(Step);
        using SceneRun run = Run(source);

        Assert.False(source.IsPlaying);

        source.Play();

        Assert.True(source.IsPlaying);
    }

    [Fact]
    public void ASourceWhoseEntityLeavesTheScene_StopsItsVoice()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        Voice voice = FirstVoice(run.Scene);
        run.Scene.Remove(source.Entity!);
        run.Step();

        Assert.False(source.IsPlaying);
        Assert.False(run.Scene.Audio.IsPlaying(voice));
    }

    // Stopping the scene reaches the same hook, so a transition silences a scene's own sources
    // while anything played through the mixer directly plays on.
    [Fact]
    public void StoppingTheScene_StopsASourcesVoiceAndLeavesAMixerPlayedOneAlone()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        SceneRun run = Run(source);

        Voice ambient = run.Scene.Audio.Play(new AudioPlayback(Step) { Loop = true });
        Voice owned = FirstVoice(run.Scene);
        AudioMixer mixer = run.Scene.Audio;

        run.Dispose();

        Assert.False(mixer.IsPlaying(owned));
        Assert.True(mixer.IsPlaying(ambient));
    }

    [Fact]
    public void ASourcesVoice_IsLiveWhileItIsHeldAndNotAfterItEnds()
    {
        AudioSource source = new(Step) { Loop = true };
        using SceneRun run = Run(source);

        Assert.False(source.IsLive);

        source.Play();
        Assert.True(source.IsLive);

        source.Pause();
        Assert.True(source.IsLive);
        Assert.False(source.IsPlaying);

        source.Resume();
        run.Scene.Audio.Pause(AudioBus.Master);
        Assert.True(source.IsLive);
        Assert.False(source.IsPlaying);

        run.Scene.Audio.Resume(AudioBus.Master);
        source.Stop();
        Assert.False(source.IsLive);
    }

    [Fact]
    public void ASourcesOneShot_IsNotLiveOnceItExpires()
    {
        AudioSource source = new(Step) { PlayOnStart = true };
        using SceneRun run = Run(source);

        Assert.True(source.IsLive);

        // 0.08 s is 4.8 steps at the default rate, so the voice is spent on the sixth tick.
        run.Run(6);

        Assert.False(source.IsLive);
    }

    [Fact]
    public void ASource_DeclaresItsClipAsAPreload()
    {
        AudioSource source = new(Step);
        Speaker entity = new();
        entity.Add(source);

        Scene scene = new();
        scene.Add(entity);

        AssetCollection assets = scene.CollectAssetPreloads();

        Assert.Equal([Step], assets.Clips);
    }

    [Fact]
    public void ASourcesVolume_ReachesTheLiveVoiceAndTheNextOne()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        source.Volume = 0.25f;

        AudioCommand raised = run.Scene.Audio.Commands[^1];
        Assert.Equal(AudioCommandKind.SetGain, raised.Kind);
        Assert.Equal(0.25f, raised.Gain);

        source.Play();
        Assert.Equal(0.25f, run.Scene.Audio.Commands[^1].Gain);
    }

    [Fact]
    public void Play_RestartsTheSourcesOwnVoiceRatherThanStackingOne()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        Voice first = FirstVoice(run.Scene);
        source.Play();

        Assert.False(run.Scene.Audio.IsPlaying(first));
        Assert.True(source.IsPlaying);
    }

    [Fact]
    public void PlayOneShot_StartsASeparateVoiceTheSourceDoesNotHold()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        Voice held = FirstVoice(run.Scene);
        Voice once = source.PlayOneShot(Step);

        Assert.NotEqual(held, once);
        Assert.True(run.Scene.Audio.IsPlaying(held));
        Assert.True(run.Scene.Audio.IsPlaying(once));

        source.Stop();
        Assert.True(run.Scene.Audio.IsPlaying(once));
    }

    [Fact]
    public void PlayOneShot_ScalesTheSourcesStandingLevelsWithoutChangingThem()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true, Volume = 0.5f, Pitch = 2f };
        using SceneRun run = Run(source);

        Voice held = FirstVoice(run.Scene);
        source.PlayOneShot(Step, volumeScale: 0.5f, pitchScale: 1.05f);

        AudioCommand once = run.Scene.Audio.Commands[^1];
        Assert.Equal(AudioCommandKind.Play, once.Kind);
        Assert.Equal(0.25f, once.Gain);
        Assert.Equal(2.1f, once.Pitch, 5);

        Assert.Equal(0.5f, source.Volume);
        Assert.Equal(2f, source.Pitch);
        Assert.True(run.Scene.Audio.IsPlaying(held));
    }

    [Fact]
    public void PlayOneShot_RejectsAScaleOutsideItsRange()
    {
        AudioSource source = new(Step);
        using SceneRun run = Run(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.PlayOneShot(Step, volumeScale: 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.PlayOneShot(Step, pitchScale: 0f));
    }

    // Pan behaves as Volume does — the live voice moves at once and the next play starts there — and
    // Time reads the clip position the run has reached, from wherever the play began.
    [Fact]
    public void Pan_MovesTheLiveVoiceAndEveryPlayAfterIt_AndTimeFollowsTheClip()
    {
        AudioSource source = new(Step) { PlayOnStart = true, Loop = true };
        using SceneRun run = Run(source);

        source.Pan = -1f;

        AudioCommand moved = run.Scene.Audio.Commands[^1];
        Assert.Equal(AudioCommandKind.SetPan, moved.Kind);
        Assert.Equal(-1f, moved.Pan);

        // Three steps run ticks 0 through 2, and the mixer stands on the last of them.
        run.Run(3);
        Assert.Equal(2.0 / StepContext.DefaultStepHertz, source.Time, 6);

        source.Play(0.04);

        Assert.Equal(-1f, run.Scene.Audio.Commands[^1].Pan);
        Assert.Equal(0.04, source.Time, 9);
    }

    [Fact]
    public void ASourceOnNoStartedScene_CannotPlay()
    {
        AudioSource source = new(Step);

        Assert.Throws<InvalidOperationException>(source.Play);
        Assert.Throws<InvalidOperationException>(() => source.PlayOneShot(Step));
    }

    private static Voice FirstVoice(Scene scene)
    {
        foreach (AudioCommand command in scene.Audio.Commands)
        {
            if (command.Kind == AudioCommandKind.Play)
            {
                return command.Voice;
            }
        }

        return Voice.None;
    }

    private sealed class Speaker() : Entity(Vector2.Zero);

    private static SceneRun Run(AudioSource source, int stepHertz = StepContext.DefaultStepHertz)
    {
        Speaker entity = new();
        entity.Add(source);

        Scene scene = new();
        scene.Add(entity);

        return new SceneRun(scene, stepHertz: stepHertz);
    }
}
