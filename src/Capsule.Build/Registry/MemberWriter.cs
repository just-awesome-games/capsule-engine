using System.Text;

namespace Capsule.Build.Registry;

/// <summary>Writes one member of a folder's class at <paramref name="indent"/>, under <paramref name="identifier"/>.</summary>
internal delegate void MemberWriter(StringBuilder code, string indent, string identifier);

/// <summary>Writes the member <paramref name="source"/> declares as <paramref name="model"/>, the typed facts its step read.</summary>
internal delegate void MemberWriter<in T>(StringBuilder code, string indent, string identifier, Source source, T model);
