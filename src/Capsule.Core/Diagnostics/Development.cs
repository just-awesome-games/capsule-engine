using System.Diagnostics.CodeAnalysis;

namespace Capsule.Diagnostics;

/// <summary>
/// Capsule's development plane, which one build axis switches: <c>CapsuleShipping</c> is
/// <c>true</c> for a publish and <c>false</c> for every other build. The engine ships prebuilt, so
/// its development code gates on <see cref="IsSupported"/> and a trimmed publish removes what that
/// switch turns off. A game's code is compiled per build, so a game gates a call with
/// <c>[Conditional(Development.Symbol)]</c> — the call and its arguments are then absent from a
/// publish — or a block with <c>#if CAPSULE_DEVELOPMENT</c>, and may read
/// <see cref="IsSupported"/> at boot to wire its development tools. Reading it inside a step makes
/// a development run differ from a shipping one, which is what development-only code is for and
/// what simulation code must never do. A directory holding a <c>.capsuleignore</c> file is the
/// third layer: everything under it is in every ordinary build and no publish.
/// <para>
/// Debug and Release are a different axis: <c>DEBUG</c>, which <see cref="Log.Debug"/> answers
/// to, is off in a Release build whether or not it ships.
/// </para>
/// </summary>
public static class Development
{
    /// <summary>
    /// The compile symbol Capsule's build defines for every project it reaches in a non-shipping
    /// build and leaves undefined under <c>CapsuleShipping</c>; what a game's
    /// <c>[Conditional]</c> development calls answer to.
    /// </summary>
    public const string Symbol = "CAPSULE_DEVELOPMENT";

    /// <summary>
    /// Whether this process's development plane is on: the <c>Capsule.Development</c> runtime
    /// feature switch, on unless the build set it, which a publish under <c>CapsuleShipping</c>
    /// does. Constant for the process.
    /// </summary>
    [FeatureSwitchDefinition("Capsule.Development")]
    public static bool IsSupported =>
        AppContext.TryGetSwitch("Capsule.Development", out bool on) ? on : true;
}
