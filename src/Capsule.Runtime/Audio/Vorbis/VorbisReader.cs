#nullable disable
#pragma warning disable
using System;
using System.IO;

namespace Capsule.Runtime.Audio.Vorbis
{
    // Capsule: reduced to what VorbisPcmSource calls. It decodes the first logical stream, ignores
    // any chained after it, and owns the stream.
    internal sealed class VorbisReader : IDisposable
    {
        private readonly Ogg.PageReaderBase _pageReader;
        private readonly StreamDecoder _decoder;
        private Ogg.PacketProvider _packetProvider;

        public VorbisReader(Stream stream)
        {
            // Capsule: shipped content is seekable (HostPlatform.OpenContent), so the forward-only reader is gone.
            _pageReader = new Ogg.PageReader(stream, AcceptFirstStream);

            try
            {
                while (_packetProvider == null && _pageReader.ReadNextPage())
                {
                }

                if (_packetProvider == null)
                {
                    throw new ArgumentException("Could not load the specified container!", nameof(stream));
                }

                _decoder = new StreamDecoder(_packetProvider);
            }
            catch
            {
                _pageReader.Dispose();
                throw;
            }
        }

        public int Channels => _decoder.Channels;

        public int SampleRate => _decoder.SampleRate;

        public void SeekTo(long samplePosition) => _decoder.SeekTo(samplePosition);

        // Reads whole frames of interleaved samples and returns the count written.
        public int ReadSamples(Span<float> buffer) => _decoder.Read(buffer);

        public void Dispose() => _pageReader.Dispose();

        private bool AcceptFirstStream(Ogg.PacketProvider packetProvider)
        {
            if (_packetProvider != null)
            {
                return false;
            }

            _packetProvider = packetProvider;
            return true;
        }
    }
}
