namespace Capsule.Build.Shaders;

/// <summary>
/// The package folder of the tools a shader compiles through, named for its version, which holds DXC
/// and SPIRV-Cross per host under <c>tools/</c>, and the further formats a platform module compiles
/// each stage to. The build targets download the package and name both.
/// </summary>
internal readonly record struct ShaderTools(string Package, IReadOnlyList<ShaderFormatTool> Further)
{
    internal ShaderTools(string package)
        : this(package, [])
    {
    }
}

/// <summary>A GPU format beyond the engine's, and the tool a platform module compiles a stage to it with.</summary>
/// <param name="Format">The format's name, which its files ship under as <c>{name}.{format}.shader</c>.</param>
/// <param name="Tool">The tool's path.</param>
internal readonly record struct ShaderFormatTool(string Format, string Tool);
