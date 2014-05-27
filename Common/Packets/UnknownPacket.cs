namespace Common.Packets
{
    public class UnknownPacket : IPacket
    {
        public byte[] Data;

        public byte[] ToBytes()
        {
            return Data;
        }

        public void FromBytes(byte[] data)
        {
            Data = data;
        }

        public PacketCode GetOpcode()
        {
            return PacketCode.Unknown;
        }
    }
}
