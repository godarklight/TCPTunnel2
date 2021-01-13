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
            Statistics statistics = new Statistics();
            UDPServer udpServer = new UDPServer(settings, statistics);
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
            double sentBytesLast = 0;
            double sentUniqueBytesLast = 0;
            double receivedBytesLast = 0;
            double receivedUniqueBytesLast = 0;
            long lastTime = DateTime.UtcNow.Ticks;
            bool printStats = false;
            Console.WriteLine("Press q to quit");
            Console.WriteLine("Press s to report statistics");
            while (running)
            {
                int key = -1;
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    key = Console.Read();
                }
                if (key == (int)'q')
                {
                    running = false;
                }
                if (key == (int)'s')
                {
                    printStats = !printStats;
                    if (!printStats)
                    {
                        Console.Clear();
                        Console.WriteLine("Press q to quit");
                        Console.WriteLine("Press s to report statistics");
                    }
                }
                if (key == -1)
                {
                    Thread.Sleep(1000);
                }
                if (printStats)
                {
                    Console.Clear();
                    Console.WriteLine("Press q to quit");
                    Console.WriteLine("Press s to report statistics");
                    //Packets
                    Console.WriteLine($"Send P: {statistics.sentPackets}, UP: {statistics.sentUniquePackets}");
                    Console.WriteLine($"Receive P: {statistics.receivedPackets}, UP: {statistics.receivedUniquePackets}");
                    //Data total
                    Console.WriteLine($"Send MB: {Math.Round(statistics.sentBytes / 1048576d, 2)}, UMB: {Math.Round(statistics.sentUniqueBytes / 1048576d, 2)}");
                    Console.WriteLine($"Receive MB: {Math.Round(statistics.receivedBytes / 1048576d, 2)}, UMB: {Math.Round(statistics.receivedUniqueBytes / 1048576d, 2)}");
                    //Speed
                    long currentTime = DateTime.UtcNow.Ticks;
                    double timeElapsed = (currentTime - lastTime) / (double)TimeSpan.TicksPerSecond;
                    double sentSpeed = (statistics.sentBytes - sentBytesLast) / timeElapsed;
                    double sentUniqueSpeed = (statistics.sentUniqueBytes - sentUniqueBytesLast) / timeElapsed;
                    double receivedSpeed = (statistics.receivedBytes - receivedBytesLast) / timeElapsed;
                    double receivedUniqueSpeed = (statistics.receivedUniqueBytes - receivedUniqueBytesLast) / timeElapsed;
                    lastTime = currentTime;
                    sentBytesLast = statistics.sentBytes;
                    sentUniqueBytesLast = statistics.sentUniqueBytes;
                    receivedBytesLast = statistics.receivedBytes;
                    receivedUniqueBytesLast = statistics.receivedUniqueBytes;
                    Console.WriteLine($"Send kBs: {Math.Round(sentSpeed / 1024d)}, UkBs: {Math.Round(sentUniqueSpeed / 1024d)}");
                    Console.WriteLine($"Receive kBs: {Math.Round(receivedSpeed / 1024d)}, UkBs: {Math.Round(receivedUniqueSpeed / 1024d)}");
                    //Efficiency
                    double sendRatio = Math.Round(100d * statistics.sentUniqueBytes / (double)statistics.sentBytes, 2);
                    double receivedRatio = Math.Round(100d * statistics.receivedUniqueBytes / (double)statistics.receivedBytes, 2);
                    if (statistics.sentPackets == 0)
                    {
                        sendRatio = 0;
                    }
                    if (statistics.receivedPackets == 0)
                    {
                        receivedRatio = 0;
                    }
                    Console.WriteLine($"Send efficency ratio: {sendRatio}%");
                    Console.WriteLine($"Receive efficency ratio: {receivedRatio}%");
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
