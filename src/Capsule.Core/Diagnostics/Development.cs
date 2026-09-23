using System.Diagnostics.CodeAnalysis;

namespace Capsule.Diagnostics;

/// <summary>Capsule's development plane, switched by one build axis.</summary>
/// <remarks>
/// <c>CapsuleShipping</c> is <c>true</c> for a publish and <c>false</c> for every other build. The
/// engine ships prebuilt. Its development code gates on <see cref="IsSupported"/>, and a trimmed
/// publish removes what that switch turns off. A game's code is compiled per build. It gates a call
/// with <c>[Conditional(Development.Symbol)]</c>, which removes the call and its arguments from a
/// publish, or a block with <c>#if CAPSULE_DEVELOPMENT</c>. A game may read
/// <see cref="IsSupported"/> at boot to wire its development tools. Simulation code must not read
/// it. A development run would then differ from a shipping one. A directory holding a
/// <c>.capsuleignore</c> file is the third layer. Everything under it is in every ordinary build
/// and in no publish. Debug and Release are a separate axis: <c>DEBUG</c>, which
/// <see cref="Log.Debug"/> answers to, is off in a Release build whether or not it ships.
/// </remarks>
public static class Development
{
    /// <summary>
    /// The compile symbol Capsule's build defines for every project it reaches in a non-shipping
    /// build and leaves undefined under <c>CapsuleShipping</c>.
    /// </summary>
    public const string Symbol = "CAPSULE_DEVELOPMENT";

    /// <summary>
    /// Whether this process's development plane is on, from the <c>Capsule.Development</c> runtime
    /// feature switch, which is on unless the build turns it off. It is read once at type
    /// initialisation and is constant for the process.
    /// </summary>
    [FeatureSwitchDefinition("Capsule.Development")]
    public static bool IsSupported { get; } =
        AppContext.TryGetSwitch("Capsule.Development", out bool on) ? on : true;
}
