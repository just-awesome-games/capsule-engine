#nullable disable
#pragma warning disable
using System;

namespace Capsule.Runtime.Audio.Vorbis.Contracts.Ogg
{
    interface IPageData : IPageReader
    {
        long PageOffset { get; }
        int StreamSerial { get; }
        int SequenceNumber { get; }
        PageFlags PageFlags { get; }
        long GranulePosition { get; }
        short PacketCount { get; }
        bool? IsResync { get; }
        bool IsContinued { get; }
        int PageOverhead { get; }

        Memory<byte>[] GetPackets();
    }
}
