using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// The scene globals one shader declares, resolved to indices into a scene's values. Resolving by name
// happens once per scene and again only when the scene sets a name it had not. A draw reads each
// global by index.
internal sealed class GlobalSlots
{
    private readonly Shader _shader;

    // By global, in table order: its index in the shader's parameter table.
    private readonly int[] _declared;

    // By global: its index in the resolved scene's values, or -1 where the scene never set it.
    private readonly int[] _sources;

    private ShaderGlobals? _resolvedFor;
    private int _resolvedVersion;

    internal GlobalSlots(Shader shader)
    {
        _shader = shader;

        ReadOnlySpan<ShaderParameter> table = shader.Parameters;
        int count = 0;
        for (int i = 0; i < table.Length; i++)
        {
            count += table[i].Global ? 1 : 0;
        }

        _declared = new int[count];
        _sources = new int[count];
        for (int i = 0, g = 0; i < table.Length; i++)
        {
            if (table[i].Global)
            {
                _declared[g++] = i;
            }
        }
    }

    internal int Count => _declared.Length;

    // The shader's parameter table index of global g.
    internal int Parameter(int g) => _declared[g];

    // The kind global g is declared as.
    internal ShaderParameterKind Kind(int g) => _shader.Parameters[_declared[g]].Kind;

    // Each global's index in globals' values, or -1 where it set none. A mismatch is not cached.
    // Every draw throws until the scene sets the name as the kind the shader declares.
    internal ReadOnlySpan<int> Resolve(ShaderGlobals globals)
    {
        if (ReferenceEquals(globals, _resolvedFor) && globals.Version == _resolvedVersion)
        {
            return _sources;
        }

        // Slots are rewritten in place. A throw part-way leaves them half written. Forgetting the scene
        // first keeps the next resolve from reading them as its own.
        _resolvedFor = null;

        ReadOnlySpan<ShaderParameter> table = _shader.Parameters;
        for (int g = 0; g < _declared.Length; g++)
        {
            ShaderParameter declared = table[_declared[g]];
            int source = globals.IndexOf(declared.Name);
            if (source >= 0 && globals.KindAt(source) != declared.Kind)
            {
                throw new InvalidOperationException(
                    $"Scene global '{declared.Name}' was set as a {Material.Spelling(globals.KindAt(source))}, but shader '{_shader.Name}' declares it as a {Material.Spelling(declared.Kind)}. Set it with {Material.Argument(declared.Kind)}.");
            }

            _sources[g] = source;
        }

        _resolvedFor = globals;
        _resolvedVersion = globals.Version;

        return _sources;
    }
}
