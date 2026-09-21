#nullable disable
#pragma warning disable
using System;

namespace Capsule.Runtime.Audio.Vorbis.Contracts.Ogg
{
    interface IStreamPageReader
    {
        IPacketProvider PacketProvider { get; }

        void AddPage();

        Memory<byte>[] GetPagePackets(int pageIndex);

        int FindPage(long granulePos);

        bool GetPage(int pageIndex, out long granulePos, out bool isResync, out bool isContinuation, out bool isContinued, out int packetCount, out int pageOverhead);

        void SetEndOfStream();

        // Capsule: pass-through to the underlying IPageReader. See its PinDiscoveredPage/UnpinPage.
        void PinDiscoveredPage();

        void UnpinPage();

        int PageCount { get; }

        bool HasAllPages { get; }

        long? MaxGranulePosition { get; }

        int FirstDataPageIndex { get; }
    }
}
