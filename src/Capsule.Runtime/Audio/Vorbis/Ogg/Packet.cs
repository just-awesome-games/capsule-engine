#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;

namespace Capsule.Runtime.Audio.Vorbis.Ogg
{
    // Capsule: upstream's DataPacket bit reader is merged into its one remaining subclass. The
    // forward-only reader that held the other subclass is gone because shipped content is seekable.
    internal sealed class Packet
    {
        // size with 1-2 packet segments (> 2 packet segments should be very uncommon):
        //   x86:  68 bytes
        //   x64: 104 bytes

        // Capsule: PacketProvider owns exactly one Packet for its whole life instead of
        // constructing one per packet (see PacketProvider.CreatePacket's `// Capsule:` note on
        // why that is safe). _dataParts is reused and cleared per packet instead of being replaced
        // with a new List<int> every time.

        // this is the list of pages & packets in packed 24:8 format
        // in theory, this is good for up to 1016 GiB of Ogg file
        // in practice, probably closer to 300 days @ 160k bps
        private readonly List<int> _dataParts = new(2);
        private PacketProvider _packetReader;                    // IntPtr
        Memory<byte> _data;
        int _dataIndex;                                         // 4
        int _dataOfs;                                           // 4
        ulong _bitBucket;
        int _bitCount;
        byte _overflowBits;
        PacketFlags _packetFlags;

        // <summary>
        // Gets the granule position of the packet, if known.
        // </summary>
        public long? GranulePosition { get; set; }

        // Capsule: reinitializes this instance for a new packet instead of constructing a new one.
        // A freshly constructed Packet starts with _dataIndex and _dataOfs at zero.
        // This reused instance sets them back to zero here too. Without this reset, a previous
        // packet's leftover _dataIndex could already equal or exceed this packet's part count.
        // ReadNextByte would then report end-of-data immediately and truncate the decode.
        internal void Reinitialize(int firstPart, PacketProvider packetReader, Memory<byte> initialData)
        {
            _dataParts.Clear();
            _dataParts.Add(firstPart);
            _packetReader = packetReader;
            _data = initialData;
            _dataIndex = 0;
            _dataOfs = 0;
            ResetForReuse();
        }

        // Capsule: a packet continued across pages. part is the next continuation page's packed
        // pageIndex (see PacketProvider.CreatePacket). This runs once per continuation page
        // discovered, so the reusable list grows in place instead of a fresh List<int> per packet.
        internal void AddDataPart(int part)
        {
            _dataParts.Add(part);
        }


        // <summary>
        // Reads the next byte in the packet.
        // </summary>
        // <returns>The next byte in the packet, or <c>-1</c> if no more data is available.</returns>
        private int ReadNextByte()
        {
            if (_dataIndex == _dataParts.Count) return -1;

            var b = _data.Span[_dataOfs];

            if (++_dataOfs == _data.Length)
            {
                _dataOfs = 0;
                if (++_dataIndex < _dataParts.Count)
                {
                    _data = _packetReader.GetPacketData(_dataParts[_dataIndex]);
                }
                else
                {
                    _data = Memory<byte>.Empty;
                }
            }

            return b;
        }

        // <summary>
        // Resets the read buffers to the beginning of the packet.
        // </summary>
        public void Reset()
        {
            _dataIndex = 0;
            _dataOfs = 0;
            if (_dataParts.Count > 0)
            {
                _data = _packetReader.GetPacketData(_dataParts[0]);
            }

            _bitBucket = 0;
            _bitCount = 0;
            _overflowBits = 0;
        }

        // <summary>
        // Frees the buffers and caching for the packet instance.
        // </summary>
        public void Done()
        {
            _packetReader?.InvalidatePacketCache(this);
        }

        // <summary>
        // Defines flags to apply to the current packet
        // </summary>
        [Flags]
        // for now, let's use a byte... if we find we need more space, we can always expand it...
        private enum PacketFlags : byte
        {
            // <summary>
            // Packet is first since reader had to resync with stream.
            // </summary>
            IsResync = 0x01,
            // <summary>
            // Packet is the last in the logical stream.
            // </summary>
            IsEndOfStream = 0x02,
            // <summary>
            // Packet does not have all its data available.
            // </summary>
            IsShort = 0x04,
        }

        // <summary>
        // Gets whether this packet occurs immediately following a loss of sync in the stream.
        // </summary>
        public bool IsResync
        {
            get => GetFlag(PacketFlags.IsResync);
            set => SetFlag(PacketFlags.IsResync, value);
        }

        // <summary>
        // Gets whether this packet did not read its full data.
        // </summary>
        public bool IsShort
        {
            get => GetFlag(PacketFlags.IsShort);
            private set => SetFlag(PacketFlags.IsShort, value);
        }

        // <summary>
        // Gets whether the packet is the last packet of the stream.
        // </summary>
        public bool IsEndOfStream
        {
            get => GetFlag(PacketFlags.IsEndOfStream);
            set => SetFlag(PacketFlags.IsEndOfStream, value);
        }

        // Capsule: a bitwise test instead of Enum.HasFlag. HasFlag boxes its argument on every
        // call. This getter runs on every packet, several times per packet (IsResync, IsShort,
        // IsEndOfStream checks), measured at 144 B per packet on loop.ogg from that boxing alone.
        bool GetFlag(PacketFlags flag) => (_packetFlags & flag) == flag;

        void SetFlag(PacketFlags flag, bool value)
        {
            if (value)
            {
                _packetFlags |= flag;
            }
            else
            {
                _packetFlags &= ~flag;
            }
        }

        // Capsule: a pooled Packet is reused across every packet a provider
        // reads. It needs a way to drop the previous use's flags and derived state along with
        // its bit-read position. Reset() alone leaves _packetFlags and GranulePosition holding the prior packet's values.
        internal void ResetForReuse()
        {
            _packetFlags = 0;
            GranulePosition = null;
            Reset();
        }

        public bool ReadBit() => ReadBits(1) == 1;

        public ulong ReadBits(int count)
        {
            // short-circuit 0
            if (count == 0) return 0UL;

            var value = TryPeekBits(count, out _);

            SkipBits(count);

            return value;
        }

        // <summary>
        // Attempts to read the specified number of bits from the packet.  Does not advance the read position.
        // </summary>
        // <param name="count">The number of bits to read.</param>
        // <param name="bitsRead">Outputs the actual number of bits read.</param>
        // <returns>The value of the bits read.</returns>
        public ulong TryPeekBits(int count, out int bitsRead)
        {
            if (count < 0 || count > 64) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
            {
                bitsRead = 0;
                return 0UL;
            }

            ulong value;
            while (_bitCount < count)
            {
                var val = ReadNextByte();
                if (val == -1)
                {
                    bitsRead = _bitCount;
                    value = _bitBucket;
                    return value;
                }
                _bitBucket = (ulong)(val & 0xFF) << _bitCount | _bitBucket;
                _bitCount += 8;

                if (_bitCount > 64)
                {
                    _overflowBits = (byte)(val >> (72 - _bitCount));
                }
            }

            value = _bitBucket;

            if (count < 64)
            {
                value &= (1UL << count) - 1;
            }

            bitsRead = count;
            return value;
        }

        // <summary>
        // Advances the read position by the the specified number of bits.
        // </summary>
        // <param name="count">The number of bits to skip reading.</param>
        public void SkipBits(int count)
        {
            if (count > 0)
            {
                if (_bitCount > count)
                {
                    // we still have bits left over...
                    if (count > 63)
                    {
                        _bitBucket = 0;
                    }
                    else
                    {
                        _bitBucket >>= count;
                    }
                    if (_bitCount > 64)
                    {
                        var overflowCount = _bitCount - 64;
                        _bitBucket |= (ulong)_overflowBits << (_bitCount - count - overflowCount);

                        if (overflowCount > count)
                        {
                            // ugh, we have to keep bits in overflow
                            _overflowBits >>= count;
                        }
                    }

                    _bitCount -= count;
                }
                else if (_bitCount == count)
                {
                    _bitBucket = 0UL;
                    _bitCount = 0;
                }
                else //  _bitCount < count
                {
                    // we have to move more bits than we have available...
                    count -= _bitCount;
                    _bitCount = 0;
                    _bitBucket = 0;

                    while (count > 8)
                    {
                        if (ReadNextByte() == -1)
                        {
                            count = 0;
                            IsShort = true;
                            break;
                        }
                        count -= 8;
                    }

                    if (count > 0)
                    {
                        var temp = ReadNextByte();
                        if (temp == -1)
                        {
                            IsShort = true;
                        }
                        else
                        {
                            _bitBucket = (ulong)(temp >> count);
                            _bitCount = 8 - count;
                        }
                    }
                }
            }
        }
    }
}
