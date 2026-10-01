#nullable disable
#pragma warning disable
using System;
using System.IO;

namespace Capsule.Runtime.Audio.Vorbis
{
    internal sealed class StreamDecoder
    {
        private readonly Ogg.PacketProvider _packetProvider;

        private byte _channels;
        private int _sampleRate;
        private int _block0Size;
        private int _block1Size;
        private Mode[] _modes;
        private int _modeFieldBits;

        private long _currentPosition;
        private bool _hasPosition;
        private bool _eosFound;

        private float[][] _nextPacketBuf;
        private float[][] _prevPacketBuf;

        // Capsule: replaces null-ing _prevPacketBuf/_nextPacketBuf as the "no valid buffered
        // decode" signal (first packet since a reset, and a fully drained end of stream). Nulling
        // them forced DecodeNextPacket's `_nextPacketBuf == null` check to allocate a fresh
        // `float[_channels][]` pair on the next packet decoded, once per loop wrap for a looping
        // voice, since ResetDecoder runs on every SeekTo. The pooled pair now persists for the
        // decoder's life. Only this flag resets.
        private bool _hasPrevPacketBuf;

        private int _prevPacketStart;
        private int _prevPacketEnd;
        private int _prevPacketStop;

        // Capsule: an instance method group used as a delegate argument allocates a fresh delegate
        // object at every call site. It captures `this` and so cannot be cached by the compiler the
        // way a static method group is. Passing `GetPacketGranules` directly instead of this cached
        // field is what let SeekTo (called on every loop wrap, via PacketProvider.SeekTo's
        // GetPacketGranuleCount parameter) allocate on an otherwise fully warm reader.
        private readonly Ogg.GetPacketGranuleCount _getPacketGranules;

        public StreamDecoder(Ogg.PacketProvider packetProvider)
        {
            _packetProvider = packetProvider;
            _getPacketGranules = GetPacketGranules;

            var packet = _packetProvider.PeekNextPacket();
            if (!ProcessHeaderPackets(packet))
            {
                packet.Reset();
                throw new ArgumentException("Could not find Vorbis data to decode.");
            }
        }

        private bool ProcessHeaderPackets(Ogg.Packet packet)
        {
            if (!ProcessHeaderPacket(packet, LoadStreamHeader, _ => _packetProvider.GetNextPacket().Done()))
            {
                return false;
            }

            // Capsule: the comment header is checked by its signature only. The build reads its tags.
            if (!ProcessHeaderPacket(_packetProvider.GetNextPacket(), pkt => ValidateHeader(pkt, PacketSignatureComments), pkt => pkt.Done()))
            {
                return false;
            }

            if (!ProcessHeaderPacket(_packetProvider.GetNextPacket(), LoadBooks, pkt => pkt.Done()))
            {
                return false;
            }

            _currentPosition = 0;
            ResetDecoder();
            return true;
        }

        private static bool ProcessHeaderPacket(Ogg.Packet packet, Func<Ogg.Packet, bool> processAction, Action<Ogg.Packet> doneAction)
        {
            if (packet != null)
            {
                try
                {
                    return processAction(packet);
                }
                finally
                {
                    doneAction(packet);
                }
            }
            return false;
        }

        static private readonly byte[] PacketSignatureStream = { 0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00 };
        static private readonly byte[] PacketSignatureComments = { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };
        static private readonly byte[] PacketSignatureBooks = { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };

        static private bool ValidateHeader(Ogg.Packet packet, byte[] expected)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                if (expected[i] != packet.ReadBits(8))
                {
                    return false;
                }
            }
            return true;
        }

        private bool LoadStreamHeader(Ogg.Packet packet)
        {
            if (!ValidateHeader(packet, PacketSignatureStream))
            {
                return false;
            }

            _channels = (byte)packet.ReadBits(8);
            _sampleRate = (int)packet.ReadBits(32);
            packet.SkipBits(96); // upper, nominal and lower bitrate

            _block0Size = 1 << (int)packet.ReadBits(4);
            _block1Size = 1 << (int)packet.ReadBits(4);

            return true;
        }

        private bool LoadBooks(Ogg.Packet packet)
        {
            if (!ValidateHeader(packet, PacketSignatureBooks))
            {
                return false;
            }

            var mdct = new Mdct();
            var huffman = new Huffman();

            // read the books
            var books = new Codebook[packet.ReadBits(8) + 1];
            for (var i = 0; i < books.Length; i++)
            {
                books[i] = new Codebook();
                books[i].Init(packet, huffman);
            }

            // Vorbis never used this feature, so we just skip the appropriate number of bits
            var times = (int)packet.ReadBits(6) + 1;
            packet.SkipBits(16 * times);

            // read the floors
            var floors = new IFloor[packet.ReadBits(6) + 1];
            for (var i = 0; i < floors.Length; i++)
            {
                floors[i] = CreateFloor(packet);
                floors[i].Init(packet, _channels, _block0Size, _block1Size, books);
            }

            // read the residues
            var residues = new Residue0[packet.ReadBits(6) + 1];
            for (var i = 0; i < residues.Length; i++)
            {
                residues[i] = CreateResidue(packet);
                residues[i].Init(packet, _channels, books);
            }

            // read the mappings
            var mappings = new Mapping[packet.ReadBits(6) + 1];
            for (var i = 0; i < mappings.Length; i++)
            {
                mappings[i] = CreateMapping(packet);
                mappings[i].Init(packet, _channels, floors, residues, mdct);
            }

            // read the modes
            _modes = new Mode[packet.ReadBits(6) + 1];
            for (var i = 0; i < _modes.Length; i++)
            {
                _modes[i] = new Mode();
                _modes[i].Init(packet, _channels, _block0Size, _block1Size, mappings);
            }

            // verify the closing bit
            if (!packet.ReadBit()) throw new InvalidDataException("Book packet did not end on correct bit!");

            // save off the number of bits to read to determine packet mode
            _modeFieldBits = Utils.ilog(_modes.Length - 1);

            return true;
        }

        private static IFloor CreateFloor(Ogg.Packet packet) => packet.ReadBits(16) switch
        {
            0 => new Floor0(),
            1 => new Floor1(),
            _ => throw new InvalidDataException("Invalid floor type!"),
        };

        private static Residue0 CreateResidue(Ogg.Packet packet) => packet.ReadBits(16) switch
        {
            0 => new Residue0(),
            1 => new Residue1(),
            2 => new Residue2(),
            _ => throw new InvalidDataException("Invalid residue type!"),
        };

        private static Mapping CreateMapping(Ogg.Packet packet) =>
            packet.ReadBits(16) == 0 ? new Mapping() : throw new InvalidDataException("Invalid mapping type!");



        private void ResetDecoder()
        {
            _hasPrevPacketBuf = false;
            _prevPacketStart = 0;
            _prevPacketEnd = 0;
            _prevPacketStop = 0;
            _eosFound = false;
            _hasPosition = false;
        }



        // Reads whole frames of interleaved samples and returns the count written.
        public int Read(Span<float> buffer)
        {
            var offset = 0;
            var count = buffer.Length - buffer.Length % _channels;
            if (count == 0)
            {
                return 0;
            }

            // save off value to track when we're done with the request
            var idx = offset;
            var tgt = offset + count;

            // try to fill the buffer; drain the last buffer if EOS, resync, bad packet, or parameter change
            while (idx < tgt)
            {
                // if we don't have any more valid data in the current packet, read in the next packet
                if (_prevPacketStart == _prevPacketEnd)
                {
                    if (_eosFound)
                    {
                        _hasPrevPacketBuf = false;

                        // no more samples, so just return
                        break;
                    }

                    if (!ReadNextPacket((idx - offset) / _channels, out var samplePosition))
                    {
                        // drain the current packet (the windowing will fade it out)
                        _prevPacketEnd = _prevPacketStop;
                    }

                    // if we need to pick up a position, and the packet had one, apply the position now
                    if (samplePosition.HasValue && !_hasPosition)
                    {
                        _hasPosition = true;
                        _currentPosition = samplePosition.Value - (_prevPacketEnd - _prevPacketStart) - (idx - offset) / _channels;
                    }
                }

                // we read out the valid samples from the previous packet
                var copyLen = Math.Min((tgt - idx) / _channels, _prevPacketEnd - _prevPacketStart);
                if (copyLen > 0)
                {
                    idx += ClippingCopyBuffer(buffer, idx, copyLen);
                }
            }

            // update the count of floats written
            count = idx - offset;

            // update the position
            _currentPosition += count / _channels;

            // return count of floats written
            return count;
        }

        private int ClippingCopyBuffer(Span<float> target, int targetIndex, int count)
        {
            var idx = targetIndex;
            for (; count > 0; _prevPacketStart++, count--)
            {
                for (var ch = 0; ch < _channels; ch++)
                {
                    target[idx++] = Utils.ClipValue(_prevPacketBuf[ch][_prevPacketStart]);
                }
            }
            return idx - targetIndex;
        }

        private bool ReadNextPacket(int bufferedSamples, out long? samplePosition)
        {
            // decode the next packet now so we can start overlapping with it
            var curPacket = DecodeNextPacket(out var startIndex, out var validLen, out var totalLen, out var isEndOfStream, out samplePosition);
            _eosFound |= isEndOfStream;
            if (curPacket == null)
            {
                return false;
            }

            // if we get a max sample position, back off our valid length to match
            if (samplePosition.HasValue && isEndOfStream)
            {
                var actualEnd = _currentPosition + bufferedSamples + validLen - startIndex;
                var diff = (int)(samplePosition.Value - actualEnd);
                if (diff < 0)
                {
                    validLen += diff;
                }
            }

            // start overlapping (if we don't have an previous packet data, just loop and the previous packet logic will handle things appropriately)
            if (_prevPacketEnd > 0)
            {
                // overlap the first samples in the packet with the previous packet, then loop
                OverlapBuffers(_prevPacketBuf, curPacket, _prevPacketStart, _prevPacketStop, startIndex, _channels);
                _prevPacketStart = startIndex;
            }
            else if (!_hasPrevPacketBuf)
            {
                // first packet, so it doesn't have any good data before the valid length
                _prevPacketStart = validLen;
            }

            // keep the old buffer so the GC doesn't have to reallocate every packet
            _nextPacketBuf = _prevPacketBuf;

            // save off our current packet's data for the next pass
            _prevPacketEnd = validLen;
            _prevPacketStop = totalLen;
            _prevPacketBuf = curPacket;
            _hasPrevPacketBuf = true;
            return true;
        }

        private float[][] DecodeNextPacket(out int packetStartindex, out int packetValidLength, out int packetTotalLength, out bool isEndOfStream, out long? samplePosition)
        {
            Ogg.Packet packet = null;
            try
            {
                if ((packet = _packetProvider.GetNextPacket()) == null)
                {
                    // no packet? we're at the end of the stream
                    isEndOfStream = true;
                }
                else
                {
                    // if the packet is flagged as the end of the stream, we can safely mark _eosFound
                    isEndOfStream = packet.IsEndOfStream;

                    // resync... that means we've probably lost some data; pick up a new position
                    if (packet.IsResync)
                    {
                        _hasPosition = false;
                    }

                    // make sure the packet starts with a 0 bit as per the spec
                    if (!packet.ReadBit())
                    {
                        // if we get here, we should have a good packet; decode it and add it to the buffer
                        var mode = _modes[(int)packet.ReadBits(_modeFieldBits)];
                        // Capsule: confirmed allocated exactly once for the decoder's life, not per
                        // packet. The ping-pong swap in ReadNextPacket keeps reusing this pair, and
                        // _hasPrevPacketBuf (not nulling the pair itself) is now what resets on a
                        // seek or end of stream, so a loop wrap no longer forces this to run again.
                        if (_nextPacketBuf == null)
                        {
                            _nextPacketBuf = new float[_channels][];
                            for (var i = 0; i < _channels; i++)
                            {
                                _nextPacketBuf[i] = new float[_block1Size];
                            }
                        }
                        if (mode.Decode(packet, _nextPacketBuf, out packetStartindex, out packetValidLength, out packetTotalLength))
                        {
                            // per the spec, do not decode more samples than the last granulePosition
                            samplePosition = packet.GranulePosition;
                            return _nextPacketBuf;
                        }
                    }
                }
                packetStartindex = 0;
                packetValidLength = 0;
                packetTotalLength = 0;
                samplePosition = null;
                return null;
            }
            finally
            {
                packet?.Done();
            }
        }

        private static void OverlapBuffers(float[][] previous, float[][] next, int prevStart, int prevLen, int nextStart, int channels)
        {
            for (; prevStart < prevLen; prevStart++, nextStart++)
            {
                for (var c = 0; c < channels; c++)
                {
                    next[c][nextStart] += previous[c][prevStart];
                }
            }
        }



        public void SeekTo(long samplePosition)
        {
            int rollForward;
            if (samplePosition == 0)
            {
                // short circuit for the looping case...
                _packetProvider.SeekTo(0, 0, _getPacketGranules);
                rollForward = 0;
            }
            else
            {
                // seek the stream to the correct position
                var pos = _packetProvider.SeekTo(samplePosition, 1, _getPacketGranules);
                rollForward = (int)(samplePosition - pos);
            }

            // clear out old data
            ResetDecoder();
            _hasPosition = true;

            // read the pre-roll packet
            if (!ReadNextPacket(0, out _))
            {
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                if (_packetProvider.GetGranuleCount() != samplePosition)
                {
                    throw new InvalidOperationException("Could not read pre-roll packet!  Try seeking again prior to reading more samples.");
                }
                _prevPacketStart = _prevPacketStop;
                _currentPosition = samplePosition;
                return;
            }

            // read the actual packet
            if (!ReadNextPacket(0, out _))
            {
                ResetDecoder();
                // we'll use this to force ReadSamples to fail to read
                _eosFound = true;
                throw new InvalidOperationException("Could not read pre-roll packet!  Try seeking again prior to reading more samples.");
            }

            // adjust our indexes to match what we want
            _prevPacketStart += rollForward;
            _currentPosition = samplePosition;
        }

        private int GetPacketGranules(Ogg.Packet curPacket, bool isLastInPage)
        {
            // if it's a resync, there's not any audio data to return
            if (curPacket.IsResync) return 0;

            // if it's not an audio packet, there's no audio data (seems obvious, though...)
            if (curPacket.ReadBit()) return 0;

            // OK, let's ask the appropriate mode how long this packet actually is

            // first we need to know which mode...
            var modeIdx = (int)curPacket.ReadBits(_modeFieldBits);

            // if we got an invalid mode value, we can't decode any audio data anyway...
            if (modeIdx < 0 || modeIdx >= _modes.Length) return 0;

            return _modes[modeIdx].GetPacketSampleCount(curPacket, isLastInPage);
        }


        public int Channels => _channels;

        public int SampleRate => _sampleRate;

    }
}
