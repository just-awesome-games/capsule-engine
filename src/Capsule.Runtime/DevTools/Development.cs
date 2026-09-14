using System.Diagnostics.CodeAnalysis;

namespace Capsule.Runtime.DevTools;

internal static class Development
{
    [FeatureSwitchDefinition("Capsule.Development")]
    internal static bool IsSupported =>
        AppContext.TryGetSwitch("Capsule.Development", out bool on) ? on : true;
}
