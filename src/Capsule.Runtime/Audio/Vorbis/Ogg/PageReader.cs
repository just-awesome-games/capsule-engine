#nullable disable
#pragma warning disable
using Capsule.Runtime.Audio.Vorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Capsule.Runtime.Audio.Vorbis.Ogg
{
    class PageReader : PageReaderBase, IPageData
    {
        internal static Func<IPageData, int, IStreamPageReader> CreateStreamPageReader { get; set; } = (pr, ss) => new StreamPageReader(pr, ss);

        private readonly Dictionary<int, IStreamPageReader> _streamReaders = new Dictionary<int, IStreamPageReader>();
        private readonly Func<Contracts.IPacketProvider, bool> _newStreamCallback;
        private readonly object _readLock = new object();

        private long _nextPageOffset;
        private ushort _pageSize;
        Memory<byte>[] _packets;

        // Capsule: reused across every ReadPageAt call instead of `new byte[282]` per call. The
        // header is fully consumed synchronously within ReadPageAt and never retained afterward.
        private readonly byte[] _headerScratch = new byte[282];

        // Capsule: GetPackets's known-page re-fetch (offset != PageOffset, so past the short
        // circuit) is keyed by page offset rather than the discovery ring's round-robin. A short
        // file loops over the same handful of pages, and StreamPageReader's own cache only holds
        // the single most-recently-touched page. Every loop wrap re-visits pages the round-robin
        // ring had already recycled for something else in between, and it never caught up (see the
        // brief's patch 5 for the discovery-ring rationale, which this does not share). Caching a
        // few pages by their offset makes a revisit free instead of pooled-but-still-refetched, and
        // covers the same handful of pages a loop keeps returning to.
        // Capsule: 16 covers every page loop.ogg (5 data pages) or a similarly short ambient loop
        // ever needs live at once. A loop region spanning more distinct pages than this falls back
        // to re-fetching the least-recently-added one every wrap, same as before this cache existed.
        private const int KnownPageCacheSize = 16;
        private readonly long[] _knownPageOffset = InitOffsets();
        private readonly byte[][] _knownPageBuffer = new byte[KnownPageCacheSize][];
        private readonly Memory<byte>[][] _knownPagePackets = new Memory<byte>[KnownPageCacheSize][];
        private readonly short[] _knownPagePacketCount = new short[KnownPageCacheSize];
        private int _knownPageNextEvict;

        private static long[] InitOffsets()
        {
            var offsets = new long[KnownPageCacheSize];
            Array.Fill(offsets, -1L);
            return offsets;
        }

        public PageReader(Stream stream, bool closeOnDispose, Func<Contracts.IPacketProvider, bool> newStreamCallback)
            : base(stream, closeOnDispose)
        {
            _newStreamCallback = newStreamCallback;
        }

        private ushort ParsePageHeader(byte[] pageBuf, int? streamSerial, bool? isResync)
        {
            var segCnt = pageBuf[26];
            var dataLen = 0;
            var pktCnt = 0;
            var isContinued = false;

            var size = 0;
            for (int i = 0, idx = 27; i < segCnt; i++, idx++)
            {
                var seg = pageBuf[idx];
                size += seg;
                dataLen += seg;
                if (seg < 255)
                {
                    if (size > 0)
                    {
                        ++pktCnt;
                    }
                    size = 0;
                }
            }
            if (size > 0)
            {
                isContinued = pageBuf[segCnt + 26] == 255;
                ++pktCnt;
            }

            StreamSerial = streamSerial ?? BitConverter.ToInt32(pageBuf, 14);
            SequenceNumber = BitConverter.ToInt32(pageBuf, 18);
            PageFlags = (PageFlags)pageBuf[5];
            GranulePosition = BitConverter.ToInt64(pageBuf, 6);
            PacketCount = (short)pktCnt;
            IsResync = isResync;
            IsContinued = isContinued;
            PageOverhead = 27 + segCnt;
            return (ushort)(PageOverhead + dataLen);
        }

        // Capsule: instance method (was static) so it can rent from the page ring instead of
        // `new Memory<byte>[packetCount]` per page. See RentPacketsArray.
        private Memory<byte>[] ReadPackets(int packetCount, Span<byte> segments, Memory<byte> dataBuffer)
        {
            return ReadPacketsInto(RentPacketsArray(packetCount), segments, dataBuffer);
        }

        // Capsule: split out of ReadPackets so GetPackets's known-page cache (see
        // _knownPagePackets) can fill its own slot's array instead of the discovery ring's.
        private static Memory<byte>[] ReadPacketsInto(Memory<byte>[] list, Span<byte> segments, Memory<byte> dataBuffer)
        {
            var listIdx = 0;
            var dataIdx = 0;
            var size = 0;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                size += seg;
                if (seg < 255)
                {
                    if (size > 0)
                    {
                        list[listIdx++] = dataBuffer.Slice(dataIdx, size);
                        dataIdx += size;
                    }
                    size = 0;
                }
            }
            if (size > 0)
            {
                list[listIdx] = dataBuffer.Slice(dataIdx, size);
            }

            return list;
        }

        public override void Lock()
        {
            Monitor.Enter(_readLock);
        }

        protected override bool CheckLock()
        {
            return Monitor.IsEntered(_readLock);
        }

        public override bool Release()
        {
            if (Monitor.IsEntered(_readLock))
            {
                Monitor.Exit(_readLock);
                return true;
            }
            return false;
        }

        protected override void SaveNextPageSearch()
        {
            _nextPageOffset = StreamPosition;
        }

        protected override void PrepareStreamForNextPage()
        {
            SeekStream(_nextPageOffset);
        }

        protected override bool AddPage(int streamSerial, byte[] pageBuf, int pageLength, bool isResync)
        {
            // Capsule: pageBuf may be larger than pageLength (a ring-rented buffer grown for an
            // earlier, bigger page). Every offset below is computed from pageLength, never
            // pageBuf.Length.
            PageOffset = StreamPosition - pageLength;
            ParsePageHeader(pageBuf, streamSerial, isResync);

            // if the page doesn't have any packets, we can't use it
            if (PacketCount == 0) return false;

            _packets = ReadPackets(PacketCount, new Span<byte>(pageBuf, 27, pageBuf[26]), new Memory<byte>(pageBuf, 27 + pageBuf[26], pageLength - 27 - pageBuf[26]));

            if (_streamReaders.TryGetValue(streamSerial, out var spr))
            {
                spr.AddPage();

                // if we've read the last page, remove from our list so cleanup can happen.
                // this is safe because the instance still has access to us for reading.
                if ((PageFlags & PageFlags.EndOfStream) == PageFlags.EndOfStream)
                {
                    _streamReaders.Remove(StreamSerial);
                }
            }
            else
            {
                var streamReader = CreateStreamPageReader(this, StreamSerial);
                streamReader.AddPage();
                _streamReaders.Add(StreamSerial, streamReader);
                if (!_newStreamCallback(streamReader.PacketProvider))
                {
                    _streamReaders.Remove(StreamSerial);
                    return false;
                }
            }
            return true;
        }

        public override bool ReadPageAt(long offset)
        {
            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock()) throw new InvalidOperationException("Must be locked prior to reading!");

            // this should be safe; we've already checked the page by now

            if (offset == PageOffset)
            {
                // short circuit for when we've already loaded the page
                return true;
            }

            var hdrBuf = _headerScratch;

            SeekStream(offset);
            var cnt = EnsureRead(hdrBuf, 0, 27);

            PageOffset = offset;
            if (VerifyHeader(hdrBuf, 0, ref cnt))
            {
                // don't read the whole page yet; if our caller is seeking, they won't need packets anyway
                _packets = null;
                _pageSize = ParsePageHeader(hdrBuf, null, null);
                return true;
            }
            return false;
        }

        protected override void SetEndOfStreams()
        {
            foreach (var kvp in _streamReaders)
            {
                kvp.Value.SetEndOfStream();
            }
            _streamReaders.Clear();
        }


        #region IPacketData

        public long PageOffset { get; private set; }

        public int StreamSerial { get; private set; }

        public int SequenceNumber { get; private set; }

        public PageFlags PageFlags { get; private set; }

        public long GranulePosition { get; private set; }

        public short PacketCount { get; private set; }

        public bool? IsResync { get; private set; }

        public bool IsContinued { get; private set; }

        public int PageOverhead { get; private set; }

        public Memory<byte>[] GetPackets()
        {
            if (!CheckLock()) throw new InvalidOperationException("Must be locked!");

            if (_packets == null)
            {
                int slot = -1;
                for (int i = 0; i < KnownPageCacheSize; i++)
                {
                    if (_knownPageOffset[i] == PageOffset)
                    {
                        slot = i;
                        break;
                    }
                }

                if (slot >= 0 && _knownPagePacketCount[slot] == PacketCount)
                {
                    // Capsule: this exact page was already read into this slot. The file's bytes at
                    // an offset never change, so its packets are reused with no I/O and no rent.
                    _packets = _knownPagePackets[slot];
                }
                else
                {
                    if (slot < 0)
                    {
                        slot = _knownPageNextEvict;
                        _knownPageNextEvict = (_knownPageNextEvict + 1) % KnownPageCacheSize;
                        _knownPageOffset[slot] = PageOffset;
                    }

                    var pageBuf = _knownPageBuffer[slot];
                    if (pageBuf == null || pageBuf.Length < _pageSize)
                    {
                        pageBuf = new byte[_pageSize];
                        _knownPageBuffer[slot] = pageBuf;
                    }
                    SeekStream(PageOffset);
                    EnsureRead(pageBuf, 0, _pageSize);

                    var packetsArr = _knownPagePackets[slot];
                    if (packetsArr == null || packetsArr.Length < PacketCount)
                    {
                        packetsArr = new Memory<byte>[PacketCount];
                        _knownPagePackets[slot] = packetsArr;
                    }
                    _packets = ReadPacketsInto(packetsArr, new Span<byte>(pageBuf, 27, pageBuf[26]), new Memory<byte>(pageBuf, 27 + pageBuf[26], _pageSize - 27 - pageBuf[26]));
                    _knownPagePacketCount[slot] = PacketCount;
                }
            }

            return _packets;
        }

        #endregion
    }
}
