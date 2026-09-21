#nullable disable
#pragma warning disable
using Capsule.Runtime.Audio.Vorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;

namespace Capsule.Runtime.Audio.Vorbis.Ogg
{
    internal class Packet : DataPacket
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
        private IPacketReader _packetReader;                    // IntPtr
        int _dataCount;
        Memory<byte> _data;
        int _dataIndex;                                         // 4
        int _dataOfs;                                           // 4

        // Capsule: reinitializes this instance for a new packet instead of constructing a new one.
        // A freshly constructed Packet starts with _dataCount, _dataIndex and _dataOfs all zero.
        // This reused instance sets them back to zero here too. Without this reset, a previous
        // packet's leftover _dataIndex could already equal or exceed this packet's part count.
        // ReadNextByte would then report end-of-data immediately and truncate the decode.
        internal void Reinitialize(int firstPart, IPacketReader packetReader, Memory<byte> initialData)
        {
            _dataParts.Clear();
            _dataParts.Add(firstPart);
            _packetReader = packetReader;
            _data = initialData;
            _dataCount = 0;
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

        protected override int TotalBits => (_dataCount + _data.Length) * 8;

        protected override int ReadNextByte()
        {
            if (_dataIndex == _dataParts.Count) return -1;

            var b = _data.Span[_dataOfs];

            if (++_dataOfs == _data.Length)
            {
                _dataOfs = 0;
                _dataCount += _data.Length;
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

        public override void Reset()
        {
            _dataIndex = 0;
            _dataOfs = 0;
            if (_dataParts.Count > 0)
            {
                _data = _packetReader.GetPacketData(_dataParts[0]);
            }

            base.Reset();
        }

        public override void Done()
        {
            _packetReader?.InvalidatePacketCache(this);

            base.Done();
        }
    }
}
