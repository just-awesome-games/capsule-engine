namespace Capsule.AotSmoke.Logic;

/// <summary>
/// The driver names this assembly's generated registry carries, which is the list the shell's
/// <c>--driver</c> resolves through. The shell reads it to assert what a shipping build registered.
/// </summary>
public static class SmokeDrivers
{
    /// <summary>The kept driver's name, which every build registers.</summary>
    public const string Kept = "KeptDriver";

    /// <summary>The marked driver's name, which only a non-shipping build registers.</summary>
    public const string DevelopmentOnly = "DevelopmentOnlyDriver";

    /// <summary>Every name this assembly registers, in declaration order.</summary>
    public static string[] Names { get; } = Array.ConvertAll(CapsuleInputDrivers.Registrations, static registration => registration.Name);
}
