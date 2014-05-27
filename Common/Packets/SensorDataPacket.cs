using System;
using System.IO;

namespace Common.Packets
{
    public class SensorDataPacket : IPacket
    {
        public DateTime Date { get; set; }

        public int Temperature { get; set; }

        public byte[] ToBytes()
        {
            var stream = new MemoryStream(8);
            var writer = new BinaryWriter(stream);
            writer.Write(Date.ToBinary());
            writer.Write(Temperature);

            return stream.ToArray();
        }

        public void FromBytes(byte[] data)
        {
            var stream = new MemoryStream(data);
            var reader = new BinaryReader(stream);

            var dateVal = reader.ReadInt64();
            Date = DateTime.FromBinary(dateVal);

            Temperature = reader.ReadInt32();
        }

        public PacketCode GetOpcode()
        {
            return PacketCode.SensorData;
        }
    }
}
