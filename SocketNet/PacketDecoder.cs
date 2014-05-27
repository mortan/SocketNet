using System;

using Common.Packets;

namespace SocketNet
{
    class PacketDecoder
    {
        public static IPacket Decode(short opcode, byte[] data)
        {
            IPacket packet = null;
            switch (opcode)
            {
                case (short)PacketCode.SensorData:
                    packet = new SensorDataPacket();
                    break;

                default:
                    packet = new UnknownPacket();
                    break;
            }

            packet.FromBytes(data);

            return packet;
        }

        public static IPacket DecodeToSql(short opcode, byte[] data)
        {
            throw new NotImplementedException();
        }
    }
}
