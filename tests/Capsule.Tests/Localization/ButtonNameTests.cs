using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Localization;

// What a label or Run.GetText shows for a button or an action, and how a label keeps it current. The run
// reads the game's names from StringTableFixtures.Buttons.
public sealed class ButtonNameTests
{
    private static readonly InputAction Act = new("button-name-test");

    // A button, each enum that converts to one and an action fill a placeholder as the button's name.
    // GetText fills them the same way.
    [Theory]
    [InlineData("InputButton", "Press A")]
    [InlineData("PadButton", "Press A")]
    [InlineData("Key", "Press F1")]
    [InlineData("MouseButton", "Press Middle Mouse Button")]
    [InlineData("StickDirection", "Press Right Stick Left")]
    [InlineData("InputAction", "Press A")]
    public void AButtonOrActionArgument_ShowsTheButtonsName(string shape, string shown)
    {
        Run run = GameRun();
        run.Input.Bind(Act, PadButton.South);
        using SimulationHost host = Hosted(run, out Label label);
        host.Step(DeviceSnapshot.Empty.With(PadButton.South));

        string text = shape switch
        {
            "InputButton" => Both(run, label, (InputButton)PadButton.South),
            "PadButton" => Both(run, label, PadButton.South),
            "Key" => Both(run, label, Key.F1),
            "MouseButton" => Both(run, label, MouseButton.Middle),
            "StickDirection" => Both(run, label, StickDirection.RightStickLeft),
            _ => Both(run, label, Act),
        };

        Assert.Equal(shown, text);
    }

    // A key shows the character the layout prints, then a button shows the game's name in the run's
    // language, the source's where a translation lacks it, then the engine's English for the pad family.
    [Fact]
    public void AButton_ShowsItsKeyCharacter_ElseTheGamesName_ElseTheEnglishForThePadFamily()
    {
        Run run = GameRun();
        using SimulationHost host = Hosted(run, out Label label);
        label.SetText(Key.LeftShift);

        Assert.Equal("W", run.GetText(Key.W));
        run.InputState.UseKeyLayout(Azerty());
        Assert.Equal("Z", run.GetText(Key.W));
        Assert.Equal("Shift Left", label.Text);
        Assert.Equal("A", run.GetText(PadButton.South));

        host.Step(DeviceSnapshot.Empty.WithPadFamily(PadFamily.PlayStation));
        Assert.Equal("Cross", run.GetText(PadButton.South));
        Assert.Equal("R1", run.GetText(PadButton.RightShoulder));

        run.Language = "de";
        host.Step(DeviceSnapshot.Empty.WithPadFamily(PadFamily.PlayStation));
        Assert.Equal("Kreuz", run.GetText(PadButton.South));
        Assert.Equal("Umschalt links", label.Text);

        host.Step(DeviceSnapshot.Empty.WithPadFamily(PadFamily.Nintendo));
        Assert.Equal("B", run.GetText(PadButton.South));
        Assert.Equal("+", run.GetText(PadButton.Start));

        host.Step(DeviceSnapshot.Empty.WithPadFamily(PadFamily.Xbox));
        Assert.Equal("Menu", run.GetText(PadButton.Start));
    }

    // An action shows its button on the device used last, else its first button anywhere, else the game's
    // none text, which a translation lacking it shows in the source language, else the engine's.
    [Fact]
    public void AnAction_ShowsItsButtonOnTheActiveDevice_ElseItsFirstAnywhere_ElseTheNoneName()
    {
        Run run = GameRun();
        run.Input.Bind(Act, Key.Space, PadButton.South);
        using SimulationHost host = Hosted(run, out _);

        host.Step(DeviceSnapshot.Of(Key.Space));
        Assert.Equal("Space", run.GetText(Act));
        Assert.Equal(Key.Space, host.Input.ButtonFor(Act));

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal("A", run.GetText(Act));
        Assert.Equal(PadButton.South, host.Input.ButtonFor(Act));

        run.Input.Rebind(Act, Key.F1);
        Assert.Equal("F1", run.GetText(Act));
        Assert.Equal(Key.F1, host.Input.ButtonFor(Act));

        run.Input.Unbind(Act);
        run.Language = "de";
        host.Step();
        Assert.Equal("Free", run.GetText(Act));
        Assert.Equal(InputButton.None, host.Input.ButtonFor(Act));

        Run plain = new() { Strings = StringTableFixtures.Table };
        Assert.Equal("Unbound", plain.GetText(Act));
    }

    // A label naming a button shows the bindings and the layout as they stand when it is read, and the
    // device and pad family as the last step left them. A label naming no button keeps its decoded text
    // across all but the language.
    [Fact]
    public void ALabelNamingAButton_FollowsEachChangeWhenRead_AndAPlainKeyedLabelIgnoresInputChanges()
    {
        Run run = GameRun();
        run.Input.Bind(Act, Key.Q, PadButton.South);
        Label plain = new(FontFixtures.Font(), StringTableFixtures.Buttons.Find("buttons.none")!.Value);
        using SimulationHost host = Hosted(run, out Label label, plain);
        label.SetText(StringTableFixtures.Hint, Act);
        Assert.Equal("Press Q", label.Text);

        run.Input.Rebind(Act, Key.W, PadButton.South);
        Assert.Equal("Press W", label.Text);

        host.Step();
        string kept = plain.Text;
        run.InputState.UseKeyLayout(Azerty());
        Assert.Equal("Press Z", label.Text);

        run.Input.Rebind(Act, Key.LeftShift, PadButton.South);
        Assert.Equal("Press Shift Left", label.Text);

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal("Press A", label.Text);

        host.Step(DeviceSnapshot.Empty.With(PadButton.South).WithPadFamily(PadFamily.PlayStation));
        Assert.Equal("Press Cross", label.Text);
        Assert.Same(kept, plain.Text);

        run.Language = "de";
        host.Step(DeviceSnapshot.Empty.With(PadButton.South).WithPadFamily(PadFamily.PlayStation));
        Assert.Equal("Drücke Kreuz", label.Text);
    }

    // A label read before a rebind, one never read and GetText show the same button after a rebind made
    // between steps and after one made inside a step.
    [Fact]
    public void ACachedLabelAndAnUnreadLabel_AgreeAfterARebind_BetweenStepsAndInsideOne()
    {
        Run run = GameRun();
        run.Input.Bind(Act, Key.F1);
        Label unread = new(FontFixtures.Font());
        using SimulationHost host = Hosted(run, out Label cached, unread);
        cached.SetText(StringTableFixtures.Hint, Act);
        unread.SetText(StringTableFixtures.Hint, Act);
        host.Step();
        Assert.Equal("Press F1", cached.Text);

        run.Input.Rebind(Act, Key.F2);

        Assert.Equal("Press F2", cached.Text);
        Assert.Equal(cached.Text, unread.Text);
        Assert.Equal(cached.Text, run.GetText(StringTableFixtures.Hint, Act));

        string? inside = null;
        host.Scene.Add(new Rebinder(() =>
        {
            run.Input.Rebind(Act, Key.F3);
            inside = cached.Text;
        }));
        host.Step();

        Assert.Equal("Press F3", inside);
        Assert.Equal("Press F3", cached.Text);
        Assert.Equal(cached.Text, run.GetText(StringTableFixtures.Hint, Act));
    }

    // A run built by hand has no table of its own. A button in a text takes the names of that text's table.
    // Two tables in one run each show their own, and a bare button shows the engine's English.
    [Fact]
    public void ARunWithNoTable_NamesAButtonFromTheTablesOfTheTextHoldingIt()
    {
        Run run = new();

        Assert.Equal("Press Shift Left", run.GetText(StringTableFixtures.Hint, Key.LeftShift));
        Assert.Equal("Score: Left Shift", run.GetText(StringTableFixtures.Score, Key.LeftShift));
        Assert.Equal("Left Shift", run.GetText(Key.LeftShift));
    }

    // Once each device has shown its name, switching between them decodes into the label's own buffers.
    [Fact]
    public void ALabelNamingAnAction_AllocatesNothingPerStep_AcrossDeviceSwitches()
    {
        Run run = GameRun();
        run.Input.Bind(Act, Key.LeftShift, PadButton.South);
        using SimulationHost host = Hosted(run, out Label label);
        label.SetText(StringTableFixtures.Hint, Act);
        DeviceSnapshot keyboard = DeviceSnapshot.Of(Key.LeftShift);
        DeviceSnapshot pad = DeviceSnapshot.Empty.With(PadButton.South);

        long bytes = StepMeasurement.Bytes(step =>
        {
            host.Step(step % 40 < 20 ? (step % 2 == 0 ? keyboard : DeviceSnapshot.Empty) : (step % 2 == 0 ? pad : DeviceSnapshot.Empty));
            _ = host.Simulation.View;
        });

        Assert.Equal(0, bytes);
    }

    private static Run GameRun() => new() { Strings = StringTableFixtures.Buttons };

    // Fills the hint's placeholder on the label and through GetText, asserts both agree, and returns the text.
    private static string Both<T>(Run run, Label label, T argument)
    {
        label.SetText(StringTableFixtures.Hint, argument);
        Assert.Equal(run.GetText(StringTableFixtures.Hint, argument), label.Text);

        return label.Text;
    }

    private static SimulationHost Hosted(Run run, out Label label, params Label[] others)
    {
        label = new Label(FontFixtures.Font());
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, label));
        foreach (Label other in others)
        {
            scene.Add(new SceneFixtures.Holder(Vector2.Zero, other));
        }

        return new SimulationHost(scene, run: run);
    }

    // Runs an action once, on its first step.
    private sealed class Rebinder(Action act) : Entity(Vector2.Zero)
    {
        private Action? _act = act;

        protected internal override void OnStep(in StepContext context)
        {
            _act?.Invoke();
            _act = null;
        }
    }

    // AZERTY prints Z on W's position.
    private static string?[] Azerty()
    {
        string?[] layout = new string?[(int)Key.NumpadDecimal + 1];
        layout[(int)Key.W] = "Z";

        return layout;
    }
}
