using Capsule.Scenes;

namespace Capsule.Tests.Scenes.Entities;

// An entity's name is the one it asked for, or its type's, numbered by its scene in join order. A number is
// never reused in that scene, and a name never changes while others come and go.
public sealed class EntityNameTests
{
    // The named Probe counts with the unnamed ones, and the second Probe keeps its number once the first
    // has left.
    [Fact]
    public void ANameJoiningAgain_IsNumberedInJoinOrder_AndANumberIsNeverReused()
    {
        Scene scene = new();
        Probe first = new();
        Probe second = new();
        scene.Add(first);
        scene.Add(second);
        using SimulationHost host = new(scene);

        scene.Remove(first);
        host.Step();
        Probe third = new() { Name = "Probe" };
        scene.Add(third);
        host.Step();

        Assert.Equal("Probe", first.Name);
        Assert.Equal("Probe (1)", second.Name);
        Assert.Equal("Probe (2)", third.Name);
    }

    [Fact]
    public void ANameSetBeforeJoining_ReadsAsSet_AndOneSetInTheScene_IsNumberedAtOnce()
    {
        Scene scene = new();
        Probe avatar = new() { Name = "Avatar" };
        Probe renamed = new();
        Assert.Equal("Avatar", avatar.Name);
        scene.Add(avatar);
        scene.Add(renamed);
        using SimulationHost host = new(scene);

        renamed.Name = "Avatar";

        Assert.Equal("Avatar", avatar.Name);
        Assert.Equal("Avatar (1)", renamed.Name);
    }

    // The pool builds one shot, which is taken, returned as it leaves, and taken again.
    [Fact]
    public void APooledEntityTakenAgain_JoinsUnderANewNumber()
    {
        Scene scene = new();
        using SimulationHost host = new(scene);
        EntityPool<Shot> pool = scene.Pool<Shot>();

        Shot shot = pool.Take();
        scene.Add(shot);
        host.Step();
        Assert.Equal("Shot", shot.Name);

        scene.Remove(shot);
        host.Step();
        Assert.Same(shot, pool.Take());
        scene.Add(shot);
        host.Step();

        Assert.Equal("Shot (1)", shot.Name);
    }

    // A number in parentheses ending a name is the scene's to give. Anything else in parentheses is the game's.
    [Theory]
    [InlineData("Avatar (1)", true)]
    [InlineData("Avatar (12)", true)]
    [InlineData("Avatar (one)", false)]
    [InlineData("Avatar ()", false)]
    [InlineData("Avatar 1", false)]
    public void ANameEndingInANumberInParentheses_IsRefused(string name, bool refused)
    {
        Probe probe = new();

        Exception? thrown = Record.Exception(() => probe.Name = name);

        Assert.Equal(refused, thrown is ArgumentException);
        Assert.Equal(refused ? "Probe" : name, probe.Name);
    }

    // Scene and Camera name the scene's own panel and its camera's, which an entity never shares.
    [Fact]
    public void AnEntityAskingForSceneOrCamera_IsNumberedFromOne()
    {
        Scene scene = new();
        Probe camera = new() { Name = "Camera" };
        Probe named = new() { Name = "Scene" };
        scene.Add(camera);
        scene.Add(named);
        using SimulationHost host = new(scene);

        Assert.Equal("Camera (1)", camera.Name);
        Assert.Equal("Scene (1)", named.Name);
    }

    private sealed class Probe : Entity;

    private sealed class Shot : Entity;
}
