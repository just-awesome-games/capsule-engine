using Capsule.Persistence;
using Capsule.Scenes;

namespace Capsule.Tests.Persistence;

public sealed class SaveStoreTests
{
    private static readonly SaveKey<Settings> SettingsKey = new("settings", SaveTestJsonContext.Default.Settings);

    private static readonly SaveKey<Settings> DefaultedSettings =
        new("settings", SaveTestJsonContext.Default.Settings, new Settings { Volume = 7, Name = "fallback" });

    private static readonly SaveKey<int> Slot = new("slot-1", SaveTestJsonContext.Default.Int32);

    [Fact]
    public void ADocument_IsWrittenReadListedAndDeleted()
    {
        using SimulationHost host = new(new Idle());
        SaveStore saves = host.Run.Saves;

        saves.Write(Slot, 3);
        saves.Write(SettingsKey, new Settings { Volume = 5, Name = "a" });

        Assert.True(saves.Exists(Slot));
        Assert.Equal(3, saves.Read(Slot));
        Assert.Equal(5, saves.Read(SettingsKey).Volume);
        Assert.Equal(["settings", "slot-1"], saves.Names.ToArray());

        // A stamp is the host's; nothing under SimulationHost ever persists, so nothing is stamped.
        host.Step();
        Assert.Null(saves.Metadata(Slot));

        Assert.True(saves.Delete(Slot));
        Assert.False(saves.Delete(Slot));
        Assert.False(saves.Exists(Slot));
        Assert.Equal(["settings"], saves.Names.ToArray());
        Assert.False(saves.TryRead(Slot, out _));
    }

    [Fact]
    public void AnAbsentDocument_ReadsAsTheFallbackOrThrowsWithoutOne()
    {
        using SimulationHost host = new(new Idle());

        Assert.Equal("fallback", host.Run.Saves.Read(DefaultedSettings).Name);
        Assert.Throws<KeyNotFoundException>(() => host.Run.Saves.Read(SettingsKey));
    }

    // The store serializes at the write and deserializes at every read, so neither the value the
    // game keeps mutating nor the key's fallback instance is ever the save.
    [Fact]
    public void AValueMutatedAfterTheWrite_ReadsBackAsWritten_AndAFallbackIsNeverSharedOut()
    {
        using SimulationHost host = new(new Idle());
        Settings settings = new() { Volume = 1, Name = "before" };

        host.Run.Saves.Read(DefaultedSettings).Volume = 42;
        Assert.Equal(7, host.Run.Saves.Read(DefaultedSettings).Volume);

        host.Run.Saves.Write(SettingsKey, settings);
        settings.Volume = 99;
        settings.Name = "after";

        Settings read = host.Run.Saves.Read(SettingsKey);
        Assert.Equal(1, read.Volume);
        Assert.Equal("before", read.Name);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("x.")]
    [InlineData("")]
    [InlineData("   ")]
    public void AKey_RefusesANameThatIsNoSafeFileName(string name)
    {
        Assert.Throws<ArgumentException>(() => new SaveKey<int>(name, SaveTestJsonContext.Default.Int32));
    }

    private sealed class Idle : Scene;
}
