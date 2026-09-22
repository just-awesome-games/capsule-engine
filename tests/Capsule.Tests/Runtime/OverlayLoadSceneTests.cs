using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The Load Scene page lists a shipped scene document no class claims, beside the registered classes,
// and loads it the same way a claimed one loads: by name.
public sealed class OverlayLoadSceneTests
{
    private const string UnclaimedDocument = "attic";

    [Fact]
    public void TheLoadScenePage_ListsAnUnclaimedDocumentByKey_AndLoadsItAsANamedTransition()
    {
        List<SceneTransition> resolved = [];
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(ReadoutScene), null),
            (in SceneTransition target) =>
            {
                resolved.Add(target);

                return target.Kind switch
                {
                    SceneTransitionKind.Named when target.DocumentName == UnclaimedDocument => new PlainScene(),
                    SceneTransitionKind.Scene when target.SceneType == typeof(ReadoutScene) => new ReadoutScene(),
                    _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
                };
            },
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: RegistryWithUnclaimedDocument());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal([UnclaimedDocument, "NamedScene", "PayloadScene", "PlainScene"], Rows(overlay));

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.IsType<PlainScene>(host.Scene);
        SceneTransition named = resolved[^1];
        Assert.Equal(SceneTransitionKind.Named, named.Kind);
        Assert.Equal(UnclaimedDocument, named.DocumentName);
        Assert.Null(named.Payload);
    }

    private static SceneRegistry RegistryWithUnclaimedDocument() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(PlainScene), static _ => new PlainScene()),
                SceneRegistration.Plain(typeof(PayloadScene), static _ => new PayloadScene()),
                SceneRegistration.FromDocument(typeof(NamedScene), NamedDocument, static _ => new NamedScene()),
                SceneRegistration.DocumentOnly(UnclaimedDocument, static content => new Scene(content!.Value)),
            ]);

    // A game that ships documents but declares no scene class still gets a Load Scene row and page.
    [Fact]
    public void TheRootPage_OffersLoadScene_ForAGameWithNoRegistrationsButAShippedDocument()
    {
        SceneRegistry registry = new(
            new EntityRegistry([]),
            [SceneRegistration.DocumentOnly(UnclaimedDocument, static content => new Scene(content!.Value))]);
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(ReadoutScene), null),
            (in SceneTransition target) => target.Kind switch
            {
                SceneTransitionKind.Named when target.DocumentName == UnclaimedDocument => new PlainScene(),
                SceneTransitionKind.Scene when target.SceneType == typeof(ReadoutScene) => new ReadoutScene(),
                _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
            },
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: registry);

        Open(overlay, scheduler, host);

        Assert.Contains("Load Scene", Rows(overlay));

        Press(overlay, scheduler, host, Key.L);

        Assert.Equal([UnclaimedDocument], Rows(overlay));
    }
}
