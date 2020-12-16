using System;
using System.Net;
using System.Net.Sockets;

namespace TCPTunnel2
{
    public class TunnelServer
    {
        private TunnelSettings settings;
        public TunnelServer(TunnelSettings settings, UDPServer udpServer)
        {
            this.settings = settings;
            udpServer.ConnectLocalTCPConnection = GetTCPConnection;
        }

        public TcpClient GetTCPConnection()
        {
            TcpClient tcpClient = new TcpClient(AddressFamily.InterNetworkV6);
            try
            {
                IPEndPoint v6endpoint = new IPEndPoint(IPAddress.IPv6Loopback, settings.tcpPort);
                tcpClient.Connect(v6endpoint);
                tcpClient.NoDelay = true;
            }
            catch
            {
                try
                {
                    tcpClient = new TcpClient(AddressFamily.InterNetwork);
                    IPEndPoint v4endpoint = new IPEndPoint(IPAddress.Loopback, settings.tcpPort);
                    tcpClient.Connect(v4endpoint);
                    tcpClient.NoDelay = true;
                }
                catch
                {
                    tcpClient = null;
                }
            }
            return tcpClient;
        }
    }
}