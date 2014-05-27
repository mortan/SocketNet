using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using log4net;

namespace SocketNet
{
    /// <summary>
    /// A state object which is created for every client.
    /// Stores IO buffers and other stateful information needed to parse the packets.
    /// </summary>
    internal class StateObject
    {
        public Socket ClientSocket = null;

        public const int BufferSize = 1024;

        public byte[] Buffer = new byte[BufferSize];

        public int BufferOffset = 0;

        public int PacketDataLength = 0;

        public byte[] PacketData = null;

        public int PacketDataOffset = 0;

        public short Opcode = -1;

        public int TotalBytesRead = 0;
    }

    /// <summary>
    /// Represents a client connection.
    /// The server maintains a list of client connection (add custom data here).
    /// </summary>
    internal class ClientConnection
    {
        public Socket ClientSocket { get; set; }

        public SocketAsyncEventArgs ClientAsyncEventArgs;
    }

    /// <summary>
    /// A pool implemented as a stack of SocketAsyncEventArgs.
    /// Grows to the maximum of concurrent client connections.
    /// </summary>
    internal class SocketAsyncEventArgsPool
    {
        private readonly Stack<SocketAsyncEventArgs> pool;

        public SocketAsyncEventArgsPool(int capacity)
        {
            pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        public void Push(SocketAsyncEventArgs item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            lock (pool)
            {
                pool.Push(item);
            }
        }

        public SocketAsyncEventArgs Pop()
        {
            lock (pool)
            {
                return pool.Pop();
            }
        }

        public int Count
        {
            get
            {
                return pool.Count;
            }
        }
    }

    /// <summary>
    /// The socket server.
    /// Responsible for accepting clients and reading length prefixed packets.
    /// </summary>
    public class SocketServer
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SocketServer));

        private const int HeaderLength = 6;

        private readonly SocketAsyncEventArgsPool readWritePool = new SocketAsyncEventArgsPool(100);

        private Timer timer;

        private readonly Dictionary<Socket, ClientConnection>  clientConnections = new Dictionary<Socket, ClientConnection>();

        private Socket listenSocket;

        private bool isShuttingDown = false;

        public delegate void DataReceivedHandler(short opcode, byte[] data);

        public event DataReceivedHandler PacketReceived;

        public int ClientCount
        {
            get { return clientConnections.Count; }
        }

        /// <summary>
        /// Starts the server and listens at the specified port.
        /// </summary>
        /// <param name="port">The port.</param>
        public void Start(int port)
        {
            isShuttingDown = false;

            listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var iep = new IPEndPoint(IPAddress.Any, port);
            listenSocket.Bind(iep);
            listenSocket.Listen(100);

            StartAccept(null);

            // Has to be assigned because of garbage collection
            timer = new Timer(state => CheckClientConnections(), null, 1000, 5000);
        }

        /// <summary>
        /// Stops the server gracefully.
        /// </summary>
        public void Stop(bool forceShutdown = false)
        {
            isShuttingDown = true;

            if (forceShutdown)
            {
                log.Info("Forcing shutdown, terminating all client connections...");
                clientConnections.ToList().ForEach(c => CloseClientSocket(c.Value.ClientAsyncEventArgs));
            }
            else
            {
                if (clientConnections.Count > 0)
                {
                    log.Info("Gracefully shutting down, waiting for all clients to disconnect...");
                }
                else
                {
                    log.Info("All client connections closed, server was shut down!");
                }
            }
        }

        /// <summary>
        /// Called to start listening for a new connection.
        /// </summary>
        /// <param name="acceptEventArg">The SocketAsyncEventArgs for the client.</param>
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += AcceptEventArgCompleted;
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        /// <summary>
        /// Called when a connection attempt is made.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        void AcceptEventArgCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        /// <summary>
        /// Called from <see cref="AcceptEventArgCompleted"/> when a connection attempt is made.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (isShuttingDown)
            {
                log.Info("Dropping client connection attempt because server is shutting down");
                return;
            }

            // Assign the new connection an existing EventArgs object or create a new one
            SocketAsyncEventArgs readEventArgs = null;
            if (readWritePool.Count > 0)
            {
                readEventArgs = readWritePool.Pop();
            }
            else
            {
                readEventArgs = new SocketAsyncEventArgs { UserToken = new StateObject() };
                readEventArgs.Completed += IoCompleted;

                // Set IO buffer to user state object buffer and read up to HeaderLength bytes
                readEventArgs.SetBuffer(((StateObject)readEventArgs.UserToken).Buffer, 0, HeaderLength);
            }

            var state = (StateObject)readEventArgs.UserToken;
            state.ClientSocket = e.AcceptSocket;
            readEventArgs.SetBuffer(state.Buffer, 0, HeaderLength);

            // Reset state object to handle the case where the client connection got
            // terminated after parsing the header
            state.TotalBytesRead = 0;
            state.Opcode = -1;

            lock (clientConnections)
            {
                clientConnections.Add(e.AcceptSocket, new ClientConnection { ClientSocket = e.AcceptSocket, ClientAsyncEventArgs = readEventArgs });
            }

            try
            {
                // As soon as the client is connected, post a receive to the connection 
                bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(readEventArgs);
                }
            }
            catch (ObjectDisposedException)
            {
                // Could be that the server was force closed while handling this connect
            }

            // Accept the next connection request
            StartAccept(e);
        }

        /// <summary>
        /// Callback for completed socket read / write.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        void IoCompleted(object sender, SocketAsyncEventArgs e)
        {
            // Determine which type of operation just completed and call the associated handler 
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        /// <summary>
        /// Starts a new receive cycle.
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void BeginReceive(SocketAsyncEventArgs e)
        {
            try
            {
                var state = (StateObject)e.UserToken;
                bool willRaiseEvent = state.ClientSocket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal if the socket got force closed by a shutdown but a
                // read is pending
            }
        }

        /// <summary>
        /// Called when socket data is available, parses the packet header or body.
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            var state = (StateObject)e.UserToken;

            // Check if the remote host closed the connection
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                // Opcode not set -> Header has not been parsed yet
                if (state.Opcode == -1)
                {
                    ReadHeader(e);
                }
                else
                {
                    ReadBody(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        /// <summary>
        /// Parses the packet header.
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void ReadHeader(SocketAsyncEventArgs e)
        {
            var state = (StateObject)e.UserToken;

            state.TotalBytesRead += e.BytesTransferred;

            // If header isn't read fully, read more
            if (state.TotalBytesRead < HeaderLength)
            {
                int missing = HeaderLength - state.TotalBytesRead;
                e.SetBuffer(e.Buffer, e.Offset, missing);
            }
            else
            {
                // Received full header, parse it and read packet body
                state.Opcode = BitConverter.ToInt16(state.Buffer, 0);
                state.PacketDataLength = BitConverter.ToInt32(state.Buffer, 2);
                state.PacketData = new byte[state.PacketDataLength]; // TODO: Hot-Spot, use pooled buffers to avoid GC overhead
                state.TotalBytesRead = 0;

                e.SetBuffer(state.PacketData, 0, state.PacketDataLength);
            }

            BeginReceive(e);
        }

        /// <summary>
        /// Parses the packet body
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void ReadBody(SocketAsyncEventArgs e)
        {
            var state = (StateObject)e.UserToken;

            // Body not received fully, read more
            if (e.BytesTransferred < state.PacketDataLength)
            {
                var missing = state.PacketDataLength - e.BytesTransferred;

                e.SetBuffer(state.PacketData, e.Offset, missing);
            }
            else
            {
                // Publish packet to business logic (DON'T BLOCK THERE, use worker threads if needed)
                OnPacketReceived(state.Opcode, state.PacketData);

                // Don't forget to reset this
                e.SetBuffer(state.Buffer, 0, HeaderLength);
                state.TotalBytesRead = 0;
                state.Opcode = -1;
            }

            BeginReceive(e);
        }

        /// <summary>
        /// Callback for socket writes.
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            // TODO: Implement that
            throw new NotImplementedException();
        }

        /// <summary>
        /// Closes a client socket silently.
        /// </summary>
        /// <param name="e">The SocketAsyncEventArgs.</param>
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            var state = (StateObject)e.UserToken;

            // close the socket associated with the client 
            try
            {
                state.ClientSocket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed 
            catch (Exception) { }
            state.ClientSocket.Close();            

            // Back to the pool with the state object for reuse
            readWritePool.Push(e);

            lock (clientConnections)
            {
                // CloseClientSocket will be called twice if the socket gets closed while
                // receiving data
                var removed = clientConnections.Remove(state.ClientSocket);
                if (removed)
                {
                    log.Debug(string.Format("Closed client connection ({0})", state.ClientSocket));

                    if (clientConnections.Count == 0 && isShuttingDown)
                    {
                        log.Info("All client connections closed, server was shut down!");
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a client socket is still connected.
        /// </summary>
        /// <param name="socket"></param>
        /// <returns></returns>
        private bool IsSocketConnected(Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Periodically checks for dead connections via a timer.
        /// </summary>
        private void CheckClientConnections()
        {
            lock (clientConnections)
            {
                var deadSockets = clientConnections.Where(c => !IsSocketConnected(c.Key)).Select(c => c.Key).ToList();
                deadSockets.ForEach(sock => clientConnections.Remove(sock));
            }
        }

        /// <summary>
        /// Publishes a received packet to all listeners.
        /// </summary>
        /// <param name="opcode">The opcode of the packet.</param>
        /// <param name="data">The payload of the packet.</param>
        private void OnPacketReceived(short opcode, byte[] data)
        {
            if (PacketReceived != null)
            {
                var invocationList = PacketReceived.GetInvocationList();
                foreach (var del in invocationList)
                {
                    try
                    {
                        ((DataReceivedHandler)del)(opcode, data);
                    }
                    catch (Exception ex)
                    {
                        log.Error("Unhandled Exception in delegate", ex);
                    }
                }
            }
        }
    }
}
