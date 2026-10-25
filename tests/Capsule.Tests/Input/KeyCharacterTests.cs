using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Input;
using Capsule.Runtime.Scenes;
using Capsule.Runtime.Sdl;
using Capsule.Scenes;

namespace Capsule.Tests.Input;

// What InputState.KeyCharacter reads for a button: a US layout's character on a key unless a played
// run's host installed the player's layout, and null for a key that prints none or a button that is
// no key.
public sealed class KeyCharacterTests
{
    public static TheoryData<InputButton, string?> UsCharacters => new()
    {
        { Key.W, "W" },
        { Key.Digit7, "7" },
        { Key.Minus, "-" },
        { Key.LeftShift, null },
        { Key.Numpad7, null },
        { Key.F12, null },
        { Key.Space, null },
        { MouseButton.Left, null },
        { InputButton.None, null },
    };

    // A repeat read hands back the same string instead of building one.
    [Theory]
    [MemberData(nameof(UsCharacters))]
    public void AKey_ReadsAsAUsLayoutPrintsItAndAKeyPrintingNoneReadsNull(InputButton button, string? expected)
    {
        InputState input = new(new ActionBindings());

        Assert.Equal(expected, input.KeyCharacter(button));
        Assert.Same(input.KeyCharacter(button), input.KeyCharacter(button));
    }

    // A key's layout character is the one the layout prints on it. Space, a control character, Delete
    // and a keycode naming a scancode print none.
    [Theory]
    [InlineData('z', true)]
    [InlineData('²', true)]
    [InlineData(' ', false)]
    [InlineData('\r', false)]
    [InlineData('\u007f', false)]
    public void ALayoutCharacter_IsTakenOnlyForAPrintedCharacter(char character, bool prints)
    {
        Assert.Equal(prints, KeyboardSampler.PrintsCharacter(character));
        Assert.False(KeyboardSampler.PrintsCharacter(Sdl3.KeycodeScancodeMask | (uint)Scancode.Keypad7));
    }

    // AZERTY prints Z on W's position and A on Q's, and nothing on Shift.
    [Fact]
    public void AnInstalledLayout_GivesTheCharactersItPrints()
    {
        InputState input = new(new ActionBindings());
        input.UseKeyLayout(Azerty());

        Assert.Equal("Z", input.KeyCharacter(Key.W));
        Assert.Equal("A", input.KeyCharacter(Key.Q));
        Assert.Null(input.KeyCharacter(Key.LeftShift));
    }

    // A replay reads as its recording did on any machine. A driven run keeps the US characters.
    [Theory]
    [InlineData(false, "Z")]
    [InlineData(true, "W")]
    public void ADrivenRun_IgnoresAnInstalledLayout(bool driven, string expected)
    {
        CharacterRecorder scene = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(CharacterRecorder), null), (in SceneTransition _) => scene, new Run());
        IInputDriver? driver = driven ? new InputScript().Wait(1).Build() : null;
        FixedStepScheduler scheduler = new(0.1, 1, new ActionBindings(), driver, host);
        scheduler.UseKeyLayout(Azerty());

        scheduler.Advance(0.1, DeviceSnapshot.Empty, host);

        Assert.Equal([expected], scene.Characters);
    }

    private static string?[] Azerty()
    {
        string?[] layout = new string?[(int)Key.NumpadDecimal + 1];
        layout[(int)Key.W] = "Z";
        layout[(int)Key.Q] = "A";

        return layout;
    }

    private sealed class CharacterRecorder : Scene
    {
        internal List<string?> Characters { get; } = [];

        protected override void OnStep(in StepContext context) => Characters.Add(context.Input.KeyCharacter(Key.W));
    }
}
