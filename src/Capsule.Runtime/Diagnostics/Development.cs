using System.Diagnostics.CodeAnalysis;

namespace Capsule.Runtime.Diagnostics;

internal static class Development
{
    [FeatureSwitchDefinition("Capsule.Development")]
    internal static bool IsSupported =>
        AppContext.TryGetSwitch("Capsule.Development", out bool on) ? on : true;
}
