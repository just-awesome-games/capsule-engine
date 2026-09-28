namespace Capsule.Build.Shaders;

/// <summary>
/// The package folders of the two tools a shader compiles through, each holding a binary per host
/// under <c>binaries/</c>. The build targets download both and name them.
/// </summary>
internal readonly record struct ShaderTools(string Dxc, string SpirvCross);
