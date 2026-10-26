using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

// Where a shader's shipped file has to be for the store to load it.
public sealed class ShaderStoreTests
{
    // A shader's name is its source's path under Assets/, and its file carries the format the host
    // draws with before the container's extension, as the build ships it.
    [Theory]
    [InlineData("shaders/duotone", "assets/shaders/duotone.glsl.shader")]
    [InlineData("shaders/effects/stone", "assets/shaders/effects/stone.glsl.shader")]
    public void AShader_LocatesItsContainerAtItsPath(string name, string expected) =>
        Assert.Equal(expected, ShaderStore.RelativePathOf(new Shader(name), "glsl"));
}
