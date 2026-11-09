#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Capsule.Runtime.Audio.Vorbis.Ogg
{
    abstract class PageReaderBase
    {
        private readonly Crc _crc = new Crc();
        private readonly HashSet<int> _ignoredSerials = new HashSet<int>();
        private readonly byte[] _headerBuf = new byte[282]; // 27 + 255: a header and the longest segment table

        private Stream _stream;

        // Capsule: page bytes are rented from a small ring instead of `new byte[pageLength]` per
        // page. A packet continued across pages holds a slice into the first page it spans for as
        // long as the packet is being assembled (Ogg/PacketProvider.cs CreatePacket captures that
        // slice before walking the rest of the continuation, and every later part is re-fetched
        // fresh instead of reusing a captured reference). The ring's invariant is that a slot is
        // never reused while the packet being assembled still references it. InitialPageRingSize is
        // the ring's starting depth for the common case. PinDiscoveredPage/UnpinPage let
        // CreatePacket protect the first page's slot for the rest of its own call. When the next
        // slot in rotation would be the pinned one, the ring grows by one slot instead of reusing
        // it. Growth happens once per longest continuation ever seen, and steady state stays at
        // zero. A stream whose packets keep growing the ring costs memory, never correctness.
        private const int InitialPageRingSize = 8;
        private readonly List<byte[]> _pageBufferRing = new(InitialPageRingSize);
        // Capsule: paired 1:1 with _pageBufferRing, same slot index, same growth. This holds
        // PageReader.GetPackets's Memory<byte>[] per page. A page's packet list lives exactly as
        // long as its bytes.
        private readonly List<Memory<byte>[]> _packetsRing = new(InitialPageRingSize);
        private int _pageRingSlot = -1;
        // Capsule: the slot CreatePacket asked to protect, or -1 when nothing is pinned. Only one
        // slot is ever pinned at a time. Only the first page of a packet being assembled is
        // captured and reused without a fresh fetch (see the ring's invariant above); that is the
        // one slot that needs protecting.
        private int _pinnedSlot = -1;

        protected PageReaderBase(Stream stream)
        {
            _stream = stream;

            for (int i = 0; i < InitialPageRingSize; i++)
            {
                _pageBufferRing.Add(null);
                _packetsRing.Add(null);
            }
        }

        // Capsule: pins the slot most recently returned by RentPageBuffer, protecting it from reuse
        // until UnpinPage is called. CreatePacket calls this right after capturing the first page's
        // data and unpins before it returns (see PacketProvider.cs). The pin spans exactly one
        // CreatePacket call.
        public void PinDiscoveredPage()
        {
            _pinnedSlot = _pageRingSlot;
        }

        public void UnpinPage()
        {
            _pinnedSlot = -1;
        }

        // Capsule: the length a pooled page buffer grows to when a page outgrows it. Growing only to
        // the page's length reallocated a slot whenever it met a page larger than any it had held, and
        // a stream with more pages than a pool has slots kept meeting one for as long as it looped.
        // Pages of one encoder fall in one or two powers of two. A slot grows once or twice.
        protected static int GrowSize(int size) => (int)BitOperations.RoundUpToPowerOf2((uint)size);

        // Capsule: a pooled packet array's length. A page's segment table has at most 255 entries,
        // and every packet a page ends takes at least one. One allocation per slot holds any page.
        protected const int MaxPacketsPerPage = 255;

        // Capsule: grows (never shrinks) the next ring slot to at least size bytes, advances the
        // ring, and returns the pooled buffer. The caller must track the page's real length itself
        // rather than trust buffer.Length. A rented buffer can be larger than size. When the next
        // slot in rotation is the pinned one (see PinDiscoveredPage), a fresh slot is appended
        // instead. A pinned page's bytes are never overwritten this way.
        protected byte[] RentPageBuffer(int size)
        {
            int next = (_pageRingSlot + 1) % _pageBufferRing.Count;
            if (next == _pinnedSlot)
            {
                _pageBufferRing.Add(null);
                _packetsRing.Add(null);
                next = _pageBufferRing.Count - 1;
            }
            _pageRingSlot = next;

            byte[] buffer = _pageBufferRing[_pageRingSlot];
            if (buffer == null || buffer.Length < size)
            {
                buffer = new byte[GrowSize(size)];
                _pageBufferRing[_pageRingSlot] = buffer;
            }
            return buffer;
        }

        // Capsule: paired with the buffer at the current ring slot. Call after RentPageBuffer with
        // no other rental in between. Allocated once per slot instead of `new Memory<byte>[count]`
        // per page.
        protected Memory<byte>[] RentPacketsArray()
        {
            return _packetsRing[_pageRingSlot] ??= new Memory<byte>[MaxPacketsPerPage];
        }

        protected long StreamPosition => _stream?.Position ?? throw new ObjectDisposedException(nameof(PageReaderBase));

        private bool VerifyPage(byte[] headerBuf, int index, int cnt, out byte[] pageBuf, out int pageLength, out int bytesRead)
        {
            var segCnt = headerBuf[index + 26];
            if (cnt - index < 27 + segCnt)
            {
                pageBuf = null;
                pageLength = 0;
                bytesRead = 0;
                return false;
            }

            var dataLen = 0;
            int i;
            for (i = 0; i < segCnt; i++)
            {
                dataLen += headerBuf[index + i + 27];
            }

            // Capsule: rented from the page ring (see RentPageBuffer) instead of `new
            // byte[pageLength]` per page. The rented buffer may be larger than pageLength.
            // Everything below uses pageLength explicitly instead of pageBuf.Length.
            pageLength = dataLen + segCnt + 27;
            pageBuf = RentPageBuffer(pageLength);
            Buffer.BlockCopy(headerBuf, index, pageBuf, 0, segCnt + 27);
            bytesRead = EnsureRead(pageBuf, segCnt + 27, dataLen);
            if (bytesRead != dataLen) return false;

            _crc.Reset();
            for (i = 0; i < 22; i++)
            {
                _crc.Update(pageBuf[i]);
            }
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            _crc.Update(0);
            for (i += 4; i < pageLength; i++)
            {
                _crc.Update(pageBuf[i]);
            }
            return _crc.Test(BitConverter.ToUInt32(pageBuf, 22));
        }

        private bool AddPage(byte[] pageBuf, int pageLength, bool isResync)
        {
            var streamSerial = BitConverter.ToInt32(pageBuf, 14);
            if (!_ignoredSerials.Contains(streamSerial))
            {
                if (AddPage(streamSerial, pageBuf, pageLength, isResync))
                {
                    return true;
                }
                _ignoredSerials.Add(streamSerial);
            }
            return false;
        }

        // Network streams don't always return the requested size immediately. This
        // method fills the buffer if it is possible.
        // Note that it will loop until getting a certain count of zero reads (default: 10).
        // This means in most cases, the network stream probably died by the time we return
        // a short read.
        protected int EnsureRead(byte[] buf, int index, int count, int maxTries = 10)
        {
            var read = 0;
            var tries = 0;
            do
            {
                var cnt = _stream.Read(buf, index + read, count - read);
                if (cnt == 0 && ++tries == maxTries)
                {
                    break;
                }
                read += cnt;
            } while (read < count);
            return read;
        }

        // <summary>
        // Verifies the sync sequence and loads the rest of the header.
        // </summary>
        // <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        protected bool VerifyHeader(byte[] buffer, int index, ref int cnt)
        {
            if (buffer[index] == 0x4f && buffer[index + 1] == 0x67 && buffer[index + 2] == 0x67 && buffer[index + 3] == 0x53)
            {
                if (cnt < 27)
                {
                    cnt += EnsureRead(buffer, 27 - cnt + index, 27 - cnt);
                }

                if (cnt >= 27)
                {
                    var segCnt = buffer[index + 26];
                    cnt += EnsureRead(buffer, index + 27, segCnt);
                    if (cnt == index + 27 + segCnt)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // <summary>
        // Seeks the underlying stream to the requested position.
        // </summary>
        // <param name="offset">A byte offset relative to the origin parameter.</param>
        // <returns>The new position of the stream.</returns>
        // <exception cref="InvalidOperationException">The stream is not seekable.</exception>
        protected long SeekStream(long offset)
        {
            return _stream.Seek(offset, SeekOrigin.Begin);
        }

        virtual protected void PrepareStreamForNextPage() { }

        virtual protected void SaveNextPageSearch() { }

        abstract protected bool AddPage(int streamSerial, byte[] pageBuf, int pageLength, bool isResync);

        abstract protected void SetEndOfStreams();

        // Capsule: a page that fails to verify is searched for again from the byte after the one it began at,
        // by seeking, which every stream Capsule opens allows. A page is read only from its first byte.
        // Its offset and the next search's start are then exact however much damage came before it.
        public bool ReadNextPage()
        {
            var isResync = false;

            PrepareStreamForNextPage();
            while (true)
            {
                var start = StreamPosition;
                var cnt = EnsureRead(_headerBuf, 0, 27);
                if (cnt == 0)
                {
                    SetEndOfStreams();
                    return false;
                }

                if (VerifyHeader(_headerBuf, 0, ref cnt) && VerifyPage(_headerBuf, 0, cnt, out var pageBuf, out var pageLength, out _))
                {
                    // we need to let our inheritors have a chance to save state for next time
                    SaveNextPageSearch();

                    // pass it to our inheritor
                    if (AddPage(pageBuf, pageLength, isResync))
                    {
                        return true;
                    }

                    // otherwise, the whole page is useless, and the next one follows it
                    continue;
                }

                isResync = true;
                SeekStream(start + NextCapture(_headerBuf, cnt));
            }
        }

        // Capsule: where the next capture pattern can begin among the count bytes read from a failed
        // start: at the first "OggS" past its first byte, else at the last three, which may begin one.
        private static int NextCapture(byte[] buffer, int count)
        {
            var found = buffer.AsSpan(1, Math.Max(0, count - 1)).IndexOf("OggS"u8);
            return found >= 0 ? found + 1 : Math.Max(1, count - 3);
        }

        abstract public bool ReadPageAt(long offset);

        public void Dispose()
        {
            SetEndOfStreams();

            _stream?.Dispose();
            _stream = null;
        }
    }
}
