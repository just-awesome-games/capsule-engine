#nullable disable
#pragma warning disable

namespace Capsule.Runtime.Audio.Vorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    sealed class Residue1 : Residue0
    {
        protected override bool WriteVectors(Codebook codebook, Ogg.Packet packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            var res = residue[channel];

            for (int i = 0; i < partitionSize;)
            {
                var entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }
                for (int j = 0; j < codebook.Dimensions; i++, j++)
                {
                    res[offset + i] += codebook[entry, j];
                }
            }

            return false;
        }
    }
}
