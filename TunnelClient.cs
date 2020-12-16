using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCPTunnel2
{
    public class TunnelClient
    {
        private bool running = true;
        private TunnelSettings settings;
        private UDPServer udpServer;
        private TcpListener tcpServer;
        private Thread listenTask;

        public TunnelClient(TunnelSettings settings, UDPServer udpServer)
        {
            this.settings = settings;
            this.udpServer = udpServer;
            listenTask = new Thread(new ThreadStart(ListenLoop));
            listenTask.Name = "TunnelClient.ListenLoop";
            listenTask.Start();            
        }

        private void ListenLoop()
        {
            tcpServer = new TcpListener(new IPEndPoint(IPAddress.IPv6Any, settings.tcpPort));
            tcpServer.Server.NoDelay = true;
            tcpServer.Server.DualMode = true;
            tcpServer.Start();
            while (running)
            {
                try
                {
                    TcpClient tcpClient = tcpServer.AcceptTcpClient();
                    tcpClient.NoDelay = true;
                    udpServer.ConnectClient(tcpClient);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error accepting: " + e);
                }
            }
        }

        public void Stop()
        {
            if (running)
            {
                running = false;
                tcpServer.Server.Close();
            }
        }
    }
}