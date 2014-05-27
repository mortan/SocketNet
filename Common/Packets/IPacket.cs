namespace Common.Packets
{
    public interface IPacket
    {
        byte[] ToBytes();

        void FromBytes(byte[] data);

        PacketCode GetOpcode();
    }
}
