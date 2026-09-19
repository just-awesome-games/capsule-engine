namespace System.Runtime.CompilerServices;

// netstandard2.0 declares no IsExternalInit. The compiler requires it for init accessors, and so
// for every record type this assembly declares.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class IsExternalInit;
