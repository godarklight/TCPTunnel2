using System;
using System.Threading;

namespace TCPTunnel2
{
    public class Program
    {
        public static void Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.Name = "Main";
            bool running = true;
            TunnelSettings settings = new TunnelSettings();
            settings.Load("TunnelSettings.txt");
            UDPServer udpServer = new UDPServer(settings);
            TunnelClient tunnelClient = null;
            TunnelServer tunnelServer = null;
            if (settings.tunnelServer == "")
            {
                tunnelServer = new TunnelServer(settings, udpServer);
                Console.WriteLine("Server listening for incoming connections");
            }
            else
            {
                tunnelClient = new TunnelClient(settings, udpServer);
                Console.WriteLine("Client listening for incoming connections");
            }

            Console.WriteLine("Press q to quit");
            while (running)
            {
                int key = Console.Read();
                if (key == (int)'q')
                {
                    running = false;
                }
                if (key == -1)
                {
                    Thread.Sleep(100);
                }
            }
            if (tunnelClient != null)
            {
                tunnelClient.Stop();
            }
            udpServer.Stop();
        }
    }
}
