#nullable disable
#pragma warning disable
using System;

namespace Capsule.Runtime.Audio.Vorbis.Contracts.Ogg
{
    interface IPacketReader
    {
        Memory<byte> GetPacketData(int pagePacketIndex);

        void InvalidatePacketCache(IPacket packet);
    }
}
