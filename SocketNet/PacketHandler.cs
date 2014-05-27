using Common.Packets;

namespace SocketNet
{
    public class PacketHandler
    {
        public static void HandlePacket(IPacket packet)
        {
            switch (packet.GetOpcode())
            {
                case PacketCode.SensorData:
                    break;
            }
        }
    }
}
