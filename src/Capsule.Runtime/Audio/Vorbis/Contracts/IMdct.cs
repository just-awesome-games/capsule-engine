#nullable disable
#pragma warning disable
namespace Capsule.Runtime.Audio.Vorbis.Contracts
{
    interface IMdct
    {
        void Reverse(float[] samples, int sampleCount);
    }
}
