using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Common;
using Common.Packets;
using System.Collections.Generic;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9050);

            Console.Out.WriteLine("Connecting to server...");
            sock.Connect(endpoint);
            Console.Out.WriteLine("Connected!");

            var rnd = new Random();

            int packetCount = rnd.Next(0, 2000);
            for (int i = 0; i < packetCount; i++)
            {
                Console.Out.WriteLine("Generating random test data");
                var packet = CreateRandomPacket(rnd);

                Console.Out.WriteLine("Sending data to server...");

                if (rnd.Next(0, 1) == 0)
                {
                    SendWhole(sock, packet);
                }
                else
                {
                    SendPartial(sock, packet);
                }

                Thread.Sleep(rnd.Next(100, 1000));
            }

            Console.Out.WriteLine("Shutting down...");
            sock.Shutdown(SocketShutdown.Both);
            sock.Close();
        }
        
        static void SimulateMultipleClients(int clientCount)
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9050);
            var sockets = new List<Socket>();
            for (int i = 0; i < clientCount; i++)
            {
                Console.WriteLine("Connecting client...");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(endpoint);
                sockets.Add(socket);
            }

            Thread.Sleep(10000);

            sockets.ForEach(socket => {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            });
        }

        static void SendPartial(Socket sock, IPacket packet)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            byte[] payload = packet.ToBytes();
            writer.Write((short)packet.GetOpcode());
            writer.Write(payload.Length);
            writer.Write(payload);

            var data = stream.ToArray();
            Console.WriteLine(Utils.HexDump(data));
            sock.Send(data, 0, 8, 0);
            Thread.Sleep(100);
            sock.Send(data, 8, data.Length - 8, 0);
        }

        static void SendWhole(Socket sock, IPacket packet)
        {
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            byte[] payload = packet.ToBytes();
            writer.Write((short)packet.GetOpcode());
            writer.Write(payload.Length);
            writer.Write(payload);

            sock.Send(stream.ToArray());
        }

        static IPacket CreateRandomPacket(Random rnd)
        {
            var packet = rnd.Next(0, 1);

            if (packet == 0)
            {
                return new SensorDataPacket() { Date = DateTime.Now, Temperature = rnd.Next(-30, 40) };
            }

            return new UnknownPacket() { Data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF } };
        }
    }
}
