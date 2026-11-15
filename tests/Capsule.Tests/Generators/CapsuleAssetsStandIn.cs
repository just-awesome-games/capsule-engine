namespace Capsule.Tests.Generators;

internal static class CapsuleAssetsStandIn
{
    /// <summary>
    /// A stand-in for the build's <c>CapsuleAssets</c> tree, with two sounds, two textures and a scene key, for a
    /// source of block namespaces.
    /// </summary>
    internal const string Source = """
        namespace Capsule.Generated
        {
            public static class CapsuleAssets
            {
                public static class Audio
                {
                    public static global::Capsule.Audio.AudioClip HumSound => new("audio/hum", ".wav", 1d);

                    public static global::Capsule.Audio.AudioClip ChimeSound => new("audio/chime", ".wav", 1d);
                }

                public static class Textures
                {
                    public static global::Capsule.Assets.TextureHandle NorthTexture => new("textures/north", ".png");

                    public static global::Capsule.Assets.TextureHandle SouthTexture => new("textures/south", ".png");
                }

                public static class Scenes
                {
                    public static global::Capsule.Scenes.SceneKey HallScene => new("scenes/hall");
                }
            }
        }
        """;
}
