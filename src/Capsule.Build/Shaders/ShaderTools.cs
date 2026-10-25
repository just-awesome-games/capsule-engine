namespace Capsule.Build.Shaders;

/// <summary>
/// The package folder of the tools a shader compiles through, named for its version, which holds DXC
/// and SPIRV-Cross per host under <c>tools/</c>. The build targets download it and name it.
/// </summary>
internal readonly record struct ShaderTools(string Package);
