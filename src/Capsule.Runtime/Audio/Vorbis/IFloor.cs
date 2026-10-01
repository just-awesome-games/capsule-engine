#nullable disable
#pragma warning disable
namespace Capsule.Runtime.Audio.Vorbis
{
    interface IFloor
    {
        void Init(Ogg.Packet packet, int channels, int block0Size, int block1Size, Codebook[] codebooks);

        IFloorData Unpack(Ogg.Packet packet, int blockSize, int channel);

        void Apply(IFloorData floorData, int blockSize, float[] residue);
    }
}
