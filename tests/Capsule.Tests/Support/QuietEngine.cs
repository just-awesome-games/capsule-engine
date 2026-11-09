using Capsule.Input;
using Capsule.Localization;
using Capsule.Runtime;
using Capsule.Scenes;

namespace Capsule.Tests;

// The engine a run spec starts from: its scenes with no entity types, a fixed step, no crash log and
// no logging.
internal static class QuietEngine
{
    internal static EngineBuilder Configure(
        SceneRegistration[] scenes,
        int hertz = 10,
        InputDriverRegistry? drivers = null,
        ScenePayloadRegistry? payloads = null,
        StringTable? strings = null) =>
        CapsuleEngine.Configure("Quiet Game", new SceneRegistry(new EntityRegistry([]), scenes), drivers, payloads, strings)
            .WithFixedStep(hertz)
            .WithoutCrashLog()
            .WithoutLogging();
}
