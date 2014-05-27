using System;
using System.Diagnostics;

using Common;
using Common.Packets;

using log4net;
using log4net.Config;

namespace SocketNet
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));

        static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            var socketServer = new SocketServer();
            socketServer.PacketReceived += ((opcode, data) =>
                {
                    log.Debug(string.Format("Got new packet from client! opcode: {0}, length: {1}", opcode, data.Length));
                    log.Debug(Utils.HexDump(data));
                    log.Debug("Decoding packet...");
                    var packet = PacketDecoder.Decode(opcode, data);
                    log.Debug("Got packet of type: " + packet.GetType().ToString());

                    // Delegate to business logic
                    switch (packet.GetOpcode())
                    {
                        case PacketCode.SensorData:
                            OnSensorDataPacketReceived((SensorDataPacket)packet);
                            break;

                        case PacketCode.Unknown:
                            OnUnknownPacketReceived((UnknownPacket)packet);
                            break;
                    }
                }) ;
            socketServer.Start(9050);

            log.Info("Server started...");

            Console.In.ReadLine();

            socketServer.Stop(true);

            Console.ReadLine();
        }

        static void OnSensorDataPacketReceived(SensorDataPacket packet)
        {
            ProcessThreadCollection currentThreads = Process.GetCurrentProcess().Threads;
            Console.WriteLine("Thread count: " + currentThreads.Count);
            string mood = string.Empty;
            if (packet.Temperature > 30)
            {
                mood = "super hot";
            }
            else if (packet.Temperature > 15)
            {
                mood = "warm";
            }
            else if (packet.Temperature > 0)
            {
                mood = "cold";
            }
            else if (packet.Temperature <= 0)
            {
                mood = "freezing";
            }

            Console.Out.WriteLine("It's a {0} day at the clients site, temperature is {1}°C", mood, packet.Temperature);
        }

        static void OnUnknownPacketReceived(UnknownPacket packet)
        {
            Console.Out.WriteLine("Packet with unknown opcode received from client, dropping it...");
        }
    }
}
