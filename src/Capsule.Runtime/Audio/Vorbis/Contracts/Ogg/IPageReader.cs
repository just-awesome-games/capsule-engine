#nullable disable
#pragma warning disable
using System;

namespace Capsule.Runtime.Audio.Vorbis.Contracts.Ogg
{
    interface IPageReader : IDisposable
    {
        void Lock();
        bool Release();

        long ContainerBits { get; }
        long WasteBits { get; }

        bool ReadNextPage();

        bool ReadPageAt(long offset);

        // Capsule: lets PacketProvider protect the discovery ring's slot for a packet's first page
        // while it walks the rest of a continuation. See Ogg/PageReaderBase.cs's ring invariant.
        void PinDiscoveredPage();

        void UnpinPage();
    }
}
