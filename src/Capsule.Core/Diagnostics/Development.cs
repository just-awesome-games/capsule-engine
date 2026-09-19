using System.Diagnostics.CodeAnalysis;

namespace Capsule.Diagnostics;

/// <summary>
/// Capsule's development plane, switched by one build axis. <c>CapsuleShipping</c> is <c>true</c> for a
/// publish and <c>false</c> for every other build. The engine ships prebuilt, so its development code
/// gates on <see cref="IsSupported"/> and a trimmed publish removes what that switch turns off. A game's
/// code is compiled per build, so it gates a call with <c>[Conditional(Development.Symbol)]</c>, which
/// removes the call and its arguments from a publish, or a block with <c>#if CAPSULE_DEVELOPMENT</c>. A
/// game may read <see cref="IsSupported"/> at boot to wire its development tools. Simulation code must
/// not read it, because that makes a development run differ from a shipping one. A directory holding a
/// <c>.capsuleignore</c> file is the third layer, and everything under it is in every ordinary build and
/// no publish. Debug and Release are a separate axis: <c>DEBUG</c>, which <see cref="Log.Debug"/>
/// answers to, is off in a Release build whether or not it ships.
/// </summary>
public static class Development
{
    /// <summary>
    /// The compile symbol Capsule's build defines for every project it reaches in a non-shipping
    /// build and leaves undefined under <c>CapsuleShipping</c>.
    /// </summary>
    public const string Symbol = "CAPSULE_DEVELOPMENT";

    /// <summary>
    /// Whether this process's development plane is on, from the <c>Capsule.Development</c> runtime
    /// feature switch, which is on unless the build set it. It is read once at type initialisation and
    /// constant for the process.
    /// </summary>
    [FeatureSwitchDefinition("Capsule.Development")]
    public static bool IsSupported { get; } =
        AppContext.TryGetSwitch("Capsule.Development", out bool on) ? on : true;
}
