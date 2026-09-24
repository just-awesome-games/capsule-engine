using System.ComponentModel;

namespace Capsule.Rendering;

/// <summary>
/// A fragment shader the game authors as <c>{name}.fx</c> under <c>Assets/</c>, named by the build as
/// <c>CapsuleAssets.{Folders}.{Name}Shader</c>. A <see cref="Material"/> binds it to renderers.
/// </summary>
/// <remarks>
/// It carries the parameters the build read from the compiled shader, which is how
/// <see cref="Material.Set(string, float)"/> refuses a misspelt name where it is written. A headless
/// run loads and compiles nothing.
/// </remarks>
public sealed class Shader
{
    private readonly ShaderParameter[] _parameters;

    /// <summary>A shader shipped at <c>assets/{name}.mgfx</c>. Called by generated code.</summary>
    /// <param name="name">The source's path under <c>Assets/</c>, forward slashes and no extension.</param>
    /// <param name="parameters">Every parameter the game's source declares, each name once.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Shader(string name, params ShaderParameter[] parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parameters);

        for (int i = 0; i < parameters.Length; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(parameters[i].Name, nameof(parameters));
            if (!Enum.IsDefined(parameters[i].Kind))
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), parameters[i].Kind, $"Parameter '{parameters[i].Name}' has no known kind.");
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(parameters[j].Name, parameters[i].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Shader '{name}' declares parameter '{parameters[i].Name}' twice.", nameof(parameters));
                }
            }
        }

        Name = name;
        _parameters = [.. parameters];
    }

    // The key the shader ships under, and what the debug overlay shows for it.
    internal string Name { get; }

    internal ReadOnlySpan<ShaderParameter> Parameters => _parameters;

    // The parameter's index in Parameters, or -1. A short linear scan, since a shader declares a
    // handful of parameters and the scan allocates nothing.
    internal int IndexOf(string name)
    {
        for (int i = 0; i < _parameters.Length; i++)
        {
            if (string.Equals(_parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
