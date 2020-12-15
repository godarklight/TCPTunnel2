using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCPTunnel2
{
    class UDPServer
    {
        private bool running = true;
        private TunnelSettings settings;
        private byte[] readBuffer = new byte[1500];
        private Thread sendTask;
        private Thread receiveTask;
        private ConcurrentQueue<ByteArray> sendQueue = new ConcurrentQueue<ByteArray>();
        private AutoResetEvent sendEvent = new AutoResetEvent(true);
        private ConcurrentDictionary<int, Connection> connections = new ConcurrentDictionary<int, Connection>();
        private UdpClient udpClient;
        public Func<TcpClient> ConnectLocalTCPConnection;
        private Bucket globalLimit;
        private Random random = new Random();

        public UDPServer(TunnelSettings settings)
        {
            this.settings = settings;
            globalLimit = new Bucket(settings.globalUpload, settings.globalUpload, null);
            udpClient = new UdpClient(AddressFamily.InterNetworkV6);
            udpClient.Client.DualMode = true;
            udpClient.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, settings.udpPort));
            sendTask = new Thread(new ThreadStart(SendLoop));
            sendTask.Name = "UDPServer.SendLoop";
            sendTask.Start();
            receiveTask = new Thread(new ThreadStart(ReceiveLoop));
            receiveTask.Name = "UDPServer.ReceiveLoop";
            receiveTask.Start();
        }

        public void ConnectClient(TcpClient tcpClient)
        {
            int connectionID = random.Next();
            Console.WriteLine($"TCP connected {connectionID} from {tcpClient.Client.RemoteEndPoint}");
            Bucket newBucket = new Bucket(settings.connectionUpload, settings.connectionUpload, globalLimit);
            Connection c = new Connection(connectionID, settings, tcpClient, this, newBucket);
            connections.TryAdd(connectionID, c);
            int portIndex = settings.tunnelServer.LastIndexOf(":");
            string lhs = settings.tunnelServer.Substring(0, portIndex);
            string rhs = settings.tunnelServer.Substring(portIndex + 1);
            int portParse = int.Parse(rhs);
            //We don't know if the server or client has IPv4/IPv6 access, so just send to everything and let the server use the first received one.
            ByteArray sendBytes = c.GetHeartbeat();
            foreach (IPAddress address in Dns.GetHostAddresses(lhs))
            {
                IPEndPoint endpoint = new IPEndPoint(address, portParse);
                Console.WriteLine("UDP connecting to " + endpoint + " for connection " + connectionID);
                try
                {
                    //Send 4 of each to each address
                    for (int i = 0; i < 4; i++)
                    {
                        udpClient.Send(sendBytes.data, sendBytes.length, endpoint);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error connecting: " + e);
                }
            }
        }

        public void Stop()
        {
            running = false;
            udpClient.Close();
        }

        private void ReceiveLoop()
        {
            IPEndPoint listenAny = new IPEndPoint(IPAddress.IPv6Any, settings.udpPort);
            while (running)
            {
                try
                {
                    EndPoint sendAddress = listenAny;
                    int bytesRead = udpClient.Client.ReceiveFrom(readBuffer, ref sendAddress);
                    if (bytesRead > 0)
                    {
                        Process(readBuffer, bytesRead, sendAddress as IPEndPoint);
                    }
                }
                catch
                {
                    //Don't care
                }
            }
        }

        private void Process(byte[] data, int length, IPEndPoint receiveAddress)
        {
            if (length < 16)
            {
                Console.WriteLine($"Rejecting short message from {receiveAddress}");
                return;
            }
            if (data[0] != 68 || data[1] != 84 || data[2] != 84 || data[3] != 50)
            {
                Console.WriteLine($"Rejecting non TCPTunnel traffic from {receiveAddress}");
                return;
            }
            int connectionID = BitConverter.ToInt32(data, 4);
            short messageType = BitConverter.ToInt16(data, 8);
            short messageLength = BitConverter.ToInt16(data, 10);
            short messageSequence = BitConverter.ToInt16(data, 12);
            short messageACK = BitConverter.ToInt16(data, 14);
            if (BitConverter.IsLittleEndian)
            {
                connectionID = IPAddress.NetworkToHostOrder(connectionID);
                messageType = IPAddress.NetworkToHostOrder(messageType);
                messageLength = IPAddress.NetworkToHostOrder(messageLength);
                messageSequence = IPAddress.NetworkToHostOrder(messageSequence);
                messageACK = IPAddress.NetworkToHostOrder(messageACK);
            }
            if (messageLength != length - 16)
            {
                Console.WriteLine($"Rejecting bad payload TCPTunnel traffic from {receiveAddress}");
                return;
            }
            //Payload is limited to 500 bytes
            if (messageLength > 500)
            {
                Console.WriteLine($"Broken payload from {connectionID}");
                return;
            }
            if (!connections.ContainsKey(connectionID))
            {
                //We're the server so we need to grab a new tcp connection for this client
                if (ConnectLocalTCPConnection != null)
                {
                    TcpClient newClient = ConnectLocalTCPConnection();
                    if (newClient == null)
                    {
                        //Server down?
                        Console.WriteLine($"Unable to connect to {settings.tcpPort}, is the server down?");
                        return;
                    }
                    Bucket newBucket = new Bucket(settings.connectionUpload, settings.connectionUpload, globalLimit);
                    Connection newConnection = new Connection(connectionID, settings, newClient, this, newBucket);
                    newConnection.sendEndpoint = receiveAddress;
                    connections[connectionID] = newConnection;
                    Console.WriteLine($"New connection {connectionID} from {receiveAddress} mapped to {newClient.Client.LocalEndPoint}");
                }
                else
                {
                    //We can't do anything client side if we don't have an existing TCP connection, ignore the messages
                    Console.WriteLine($"Unknown UDP connection {connectionID} in client mode");
                    return;
                }
            }
            Connection c = connections[connectionID];
            ByteArray payload = null;
            if (messageLength > 0)
            {
                payload = Recycler.Grab();
                Array.Copy(data, 16, payload.data, 0, messageLength);
                payload.length = messageLength;
            }
            c.HandleUDPData(messageType, messageLength, messageSequence, messageACK, payload, receiveAddress);
        }

        private void SendLoop()
        {
            while (running)
            {
                int disconnectID = 0;
                bool sentData = false;
                foreach (KeyValuePair<int, Connection> kvp in connections)
                {
                    //We are the client and don't know the server endpoint yet.
                    if (kvp.Value.sendEndpoint == null)
                    {
                        continue;
                    }
                    //Out of data for all connections, so we should wait here
                    while (!globalLimit.TestBytes(500))
                    {
                        Thread.Sleep(1);
                    }
                    if (!kvp.Value.Check())
                    {
                        disconnectID = kvp.Key;
                    }
                    else
                    {
                        try
                        {
                            while (kvp.Value.GetUDPMessage(out ByteArray sendBytes))
                            {
                                sentData = true;
                                int bytesSent = udpClient.Send(sendBytes.data, sendBytes.length, kvp.Value.sendEndpoint);
                            }
                        }
                        catch (Exception e)
                        {
                            kvp.Value.Disconnect($"UDP send error: {e}");
                            disconnectID = kvp.Key;
                        }
                    }
                }
                if (disconnectID != 0)
                {
                    Console.WriteLine($"Disconnecting {disconnectID}");
                    connections[disconnectID].Disconnect("Removed from UDPServer loop");
                    connections.TryRemove(disconnectID, out _);
                }
                if (!sentData)
                {
                    sendEvent.WaitOne(100);
                }
            }
        }

        public void SendEvent()
        {
            sendEvent.Set();
        }
    }
}