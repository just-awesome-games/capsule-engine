#nullable disable
#pragma warning disable
namespace Capsule.Runtime.Audio.Vorbis.Contracts.Ogg
{
    interface IForwardOnlyPacketProvider : IPacketProvider
    {
        bool AddPage(byte[] buf, bool isResync);
        void SetEndOfStream();
    }
}
