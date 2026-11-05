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

    // The key the shader ships under, and what the debug overlay shows for it.
    internal string Name { get; }

    // Whether its fragment calls SampleScreen. A frame drawing it copies the picture first.
    internal bool ReadsScreen { get; }

    /// <summary>A shader shipped at <c>assets/{name}.{format}.shader</c>, one per GPU format. Called by generated code.</summary>
    /// <param name="name">The source's path under <c>Assets/</c>, forward slashes and no extension.</param>
    /// <param name="parameters">Every parameter the game's source declares, each name once.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Shader(string name, params ShaderParameter[] parameters)
        : this(name, false, parameters)
    {
    }

    /// <summary>A shader whose fragment may read the picture already drawn. Called by generated code.</summary>
    /// <param name="name">The source's path under <c>Assets/</c>, forward slashes and no extension.</param>
    /// <param name="readsScreen">Whether the fragment calls <c>SampleScreen</c>.</param>
    /// <param name="parameters">Every parameter the game's source declares, each name once.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Shader(string name, bool readsScreen, params ShaderParameter[] parameters)
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
        ReadsScreen = readsScreen;
        _parameters = [.. parameters];
    }

    internal ReadOnlySpan<ShaderParameter> Parameters => _parameters;

    // The parameter's index in Parameters, or -1.
    internal int IndexOf(string name) => ShaderValue.IndexOf(_parameters, name);
}
