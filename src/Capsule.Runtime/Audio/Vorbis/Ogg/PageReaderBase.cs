#nullable disable
#pragma warning disable
using Capsule.Runtime.Audio.Vorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;
using System.IO;

namespace Capsule.Runtime.Audio.Vorbis.Ogg
{
    abstract class PageReaderBase : IPageReader
    {
        internal static Func<ICrc> CreateCrc { get; set; } = () => new Crc();

        private readonly ICrc _crc = CreateCrc();
        private readonly HashSet<int> _ignoredSerials = new HashSet<int>();
        private readonly byte[] _headerBuf = new byte[305]; // 27 - 4 + 27 + 255 (found sync at end of first buffer, and found page has full segment count)
        private byte[] _overflowBuf;
        private int _overflowBufIndex;

        private Stream _stream;
        private bool _closeOnDispose;

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

        protected PageReaderBase(Stream stream, bool closeOnDispose)
        {
            _stream = stream;
            _closeOnDispose = closeOnDispose;

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

        // Capsule: grows (never shrinks) the next ring slot to at least size bytes, advances the
        // ring, and returns the pooled buffer. The caller must track the page's real length itself
        // rather than trust buffer.Length. A rented buffer can be larger than size. When the next
        // slot in rotation is the pinned one (see PinDiscoveredPage), a fresh slot is appended
        // instead. A pinned page's bytes are never overwritten this way. This method is virtual.
        // ForwardOnlyPageReader queues an unbounded run of pages ahead of the packet reader
        // (Ogg/ForwardOnlyPacketProvider.cs's _pageQueue) rather than consuming one before the next
        // is read. This ring cannot back that run at any fixed depth. ForwardOnlyPageReader
        // overrides this method to keep its original per-page allocation instead.
        protected virtual byte[] RentPageBuffer(int size)
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
                buffer = new byte[size];
                _pageBufferRing[_pageRingSlot] = buffer;
            }
            return buffer;
        }

        // Capsule: paired with the buffer at the current ring slot. Call after RentPageBuffer with
        // no other rental in between. Grows (never shrinks) in place instead of `new
        // Memory<byte>[count]` per page.
        protected Memory<byte>[] RentPacketsArray(int count)
        {
            Memory<byte>[] array = _packetsRing[_pageRingSlot];
            if (array == null || array.Length < count)
            {
                array = new Memory<byte>[count];
                _packetsRing[_pageRingSlot] = array;
            }
            return array;
        }

        protected long StreamPosition => _stream?.Position ?? throw new ObjectDisposedException(nameof(PageReaderBase));

        public long ContainerBits { get; private set; }

        public long WasteBits { get; private set; }

        private bool VerifyPage(byte[] headerBuf, int index, int cnt, out byte[] pageBuf, out int pageLength, out int bytesRead)
        {
            var segCnt = headerBuf[index + 26];
            if (cnt - index < index + 27 + segCnt)
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
                    ContainerBits += 8 * (27 + pageBuf[26]);
                    return true;
                }
                _ignoredSerials.Add(streamSerial);
            }
            return false;
        }

        private void EnqueueData(byte[] buf, int count)
        {
            if (_overflowBuf != null)
            {
                var newBuf = new byte[_overflowBuf.Length - _overflowBufIndex + count];
                Buffer.BlockCopy(_overflowBuf, _overflowBufIndex, newBuf, 0, newBuf.Length - count);
                var index = buf.Length - count;
                Buffer.BlockCopy(buf, index, newBuf, newBuf.Length - count, count);
                _overflowBufIndex = 0;
            }
            else
            {
                _overflowBuf = buf;
                _overflowBufIndex = buf.Length - count;
            }
        }

        private void ClearEnqueuedData(int count)
        {
            if (_overflowBuf != null && (_overflowBufIndex += count) >= _overflowBuf.Length)
            {
                _overflowBuf = null;
            }
        }

        private int FillHeader(byte[] buf, int index, int count, int maxTries = 10)
        {
            var copyCount = 0;
            if (_overflowBuf != null)
            {
                copyCount = Math.Min(_overflowBuf.Length - _overflowBufIndex, count);
                Buffer.BlockCopy(_overflowBuf, _overflowBufIndex, buf, index, copyCount);
                index += copyCount;
                count -= copyCount;
                if ((_overflowBufIndex += copyCount) == _overflowBuf.Length)
                {
                    _overflowBuf = null;
                }
            }
            if (count > 0)
            {
                copyCount += EnsureRead(buf, index, count, maxTries);
            }
            return copyCount;
        }

        private bool VerifyHeader(byte[] buffer, int index, ref int cnt, bool isFromReadNextPage)
        {
            if (buffer[index] == 0x4f && buffer[index + 1] == 0x67 && buffer[index + 2] == 0x67 && buffer[index + 3] == 0x53)
            {
                if (cnt < 27)
                {
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer, 27 - cnt + index, 27 - cnt);
                    }
                    else
                    {
                        cnt += EnsureRead(buffer, 27 - cnt + index, 27 - cnt);
                    }
                }

                if (cnt >= 27)
                {
                    var segCnt = buffer[index + 26];
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer, index + 27, segCnt);
                    }
                    else
                    {
                        cnt += EnsureRead(buffer, index + 27, segCnt);
                    }
                    if (cnt == index + 27 + segCnt)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Network streams don't always return the requested size immediately, so this
        // method is used to ensure we fill the buffer if it is possible.
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
            return VerifyHeader(buffer, index, ref cnt, false);
        }

        // <summary>
        // Seeks the underlying stream to the requested position.
        // </summary>
        // <param name="offset">A byte offset relative to the origin parameter.</param>
        // <returns>The new position of the stream.</returns>
        // <exception cref="InvalidOperationException">The stream is not seekable.</exception>
        protected long SeekStream(long offset)
        {
            // make sure we're locked; seeking won't matter if we aren't
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            return _stream.Seek(offset, SeekOrigin.Begin);
        }

        virtual protected void PrepareStreamForNextPage() { }

        virtual protected void SaveNextPageSearch() { }

        abstract protected bool AddPage(int streamSerial, byte[] pageBuf, int pageLength, bool isResync);

        abstract protected void SetEndOfStreams();

        virtual public void Lock() { }

        virtual protected bool CheckLock() => true;

        virtual public bool Release() => false;

        public bool ReadNextPage()
        {
            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            var isResync = false;

            var ofs = 0;
            int cnt;
            PrepareStreamForNextPage();
            while ((cnt = FillHeader(_headerBuf, ofs, 27 - ofs)) > 0)
            {
                cnt += ofs;
                for (var i = 0; i < cnt - 4; i++)
                {
                    if (VerifyHeader(_headerBuf, i, ref cnt, true))
                    {
                        if (VerifyPage(_headerBuf, i, cnt, out var pageBuf, out var pageLength, out var bytesRead))
                        {
                            // one way or the other, we have to clear out the page's bytes from the queue (if queued)
                            ClearEnqueuedData(bytesRead);

                            // also, we need to let our inheritors have a chance to save state for next time
                            SaveNextPageSearch();

                            // pass it to our inheritor
                            if (AddPage(pageBuf, pageLength, isResync))
                            {
                                return true;
                            }

                            // otherwise, the whole page is useless...

                            // save off that we've burned that many bits
                            WasteBits += pageLength * 8;

                            // set up to load the next page, then loop
                            ofs = 0;
                            cnt = 0;
                            break;
                        }
                        else if (pageBuf != null)
                        {
                            // Capsule: pageBuf here is a ring-rented buffer (see RentPageBuffer).
                            // A bad-CRC page's tail bytes go into _overflowBuf and can outlive more
                            // page attempts than the ring's depth. A later rental could then
                            // overwrite them mid-resync. This path only runs on a corrupted stream
                            // (a good page never fails VerifyPage). It copies out here instead of
                            // extending the ring's lifetime guarantee to cover that case.
                            EnqueueData(pageBuf.AsSpan(pageLength - bytesRead, bytesRead).ToArray(), bytesRead);
                        }
                    }
                    WasteBits += 8;
                    isResync = true;
                }

                if (cnt >= 3)
                {
                    _headerBuf[0] = _headerBuf[cnt - 3];
                    _headerBuf[1] = _headerBuf[cnt - 2];
                    _headerBuf[2] = _headerBuf[cnt - 1];
                    ofs = 3;
                }
            }

            if (cnt == 0)
            {
                SetEndOfStreams();
            }

            return false;
        }

        abstract public bool ReadPageAt(long offset);

        public void Dispose()
        {
            SetEndOfStreams();

            if (_closeOnDispose)
            {
                _stream?.Dispose();
            }
            _stream = null;
        }
    }
}
