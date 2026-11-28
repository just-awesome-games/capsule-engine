#nullable disable
#pragma warning disable
using System;
using System.IO;

namespace Capsule.Runtime.Audio.Vorbis
{
    // Capsule: reduced to what VorbisPcmSource calls. It decodes the file's first logical stream,
    // ignores any other chained after it or interleaved with it, and owns the stream.
    internal sealed class VorbisReader : IDisposable
    {
        private readonly Ogg.PageReader _pageReader;
        private readonly StreamDecoder _decoder;

        public VorbisReader(Stream stream)
        {
            // Capsule: shipped content is seekable (HostPlatform.OpenContent). The reader needs no forward-only path.
            _pageReader = new Ogg.PageReader(stream);

            try
            {
                while (_pageReader.FirstStream == null && _pageReader.ReadNextPage())
                {
                }

                if (_pageReader.FirstStream == null)
                {
                    throw new ArgumentException("Could not load the specified container!", nameof(stream));
                }

                _decoder = new StreamDecoder(_pageReader.FirstStream);
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
    }
}
