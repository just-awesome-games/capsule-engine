using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Rendering;

// How a texture is filtered, as its config and the shipped map spell it and as a sampler is chosen.
[JsonConverter(typeof(LowerCaseEnumConverter<TextureSampling>))]
internal enum TextureSampling
{
    // Interpolates neighbouring texels.
    Linear,

    // Uses the nearest texel.
    Point,
}
