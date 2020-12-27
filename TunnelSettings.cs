using System.IO;
using System.Net;
using System.Text;

namespace TCPTunnel2
{
    public class TunnelSettings
    {
        public string tunnelServer = "godarklight.privatedns.org:56552";
        public int tcpPort = 25565;
        public int udpPort = 56552;
        //Max connections
        public int connections = 10;
        //Rate settings (KB/s)
        public int connectionUpload = 1024;
        public int connectionDownload = 1024;
        public int globalUpload = 2048;
        public int globalDownload = 2048;
        //Retransmit timers (milliseconds)
        public int initialRetransmit = 5;
        public int retransmit = 100;


        public void Load(string fileName)
        {
            if (!File.Exists(fileName))
            {
                Save(fileName);
            }
            using (StreamReader sr = new StreamReader(fileName))
            {
                string currentLine = null;
                while ((currentLine = sr.ReadLine()) != null)
                {
                    string trimLine = currentLine.Trim();
                    if (trimLine.StartsWith("#") || trimLine == string.Empty)
                    {
                        continue;
                    }
                    string[] split = trimLine.Split("=");
                    if (split.Length != 2)
                    {
                        continue;
                    }
                    split[0] = split[0].Trim();
                    split[1] = split[1].Trim();
                    switch (split[0])
                    {
                        case "tunnelServer":
                            tunnelServer = split[1];
                            break;
                        case "tcpPort":
                            tcpPort = int.Parse(split[1]);
                            break;
                        case "udpPort":
                            udpPort = int.Parse(split[1]);
                            break;
                        case "connections":
                            connections = int.Parse(split[1]);
                            break;
                        case "connectionUpload":
                            connectionUpload = int.Parse(split[1]);
                            break;
                        case "connectionDownload":
                            connectionDownload = int.Parse(split[1]);
                            break;
                        case "globalUpload":
                            globalUpload = int.Parse(split[1]);
                            break;
                        case "initialRetransmit":
                            initialRetransmit = int.Parse(split[1]);
                            break;
                        case "retransmit":
                            retransmit = int.Parse(split[1]);
                            break;
                    }
                }
            }
        }

        public void Save(string fileName)
        {
            using (StreamWriter sw = new StreamWriter(fileName))
            {
                sw.WriteLine("# Tunnel server to connect to, if left blank this will instead act as the tunnel server");
                sw.WriteLine($"tunnelServer = {tunnelServer}");
                sw.WriteLine();
                sw.WriteLine("# Server mode: The port of the server program to connect to");
                sw.WriteLine("# Client mode: The port you want to host the forwarded server on");
                sw.WriteLine($"tcpPort = {tcpPort}");
                sw.WriteLine();
                sw.WriteLine("# The port for the tunnel program to use");
                sw.WriteLine($"udpPort = {udpPort}");
                sw.WriteLine();
                sw.WriteLine("# Maximum connections to support");
                sw.WriteLine($"connections = {connections}");
                sw.WriteLine();
                sw.WriteLine("# Maximum speed of individual connections, KB/s");
                sw.WriteLine($"connectionUpload = {connectionUpload}");
                sw.WriteLine($"connectionDownload = {connectionDownload}");
                sw.WriteLine();
                sw.WriteLine("# Maximum speed of all connections, KB/s");
                sw.WriteLine($"globalUpload = {globalUpload}");
                sw.WriteLine();
                sw.WriteLine("# Retransmit timers, ms. Set initial retransmit to 0 to disable double send");
                sw.WriteLine($"initialRetransmit = {initialRetransmit}");
                sw.WriteLine($"retransmit = {retransmit}");
                sw.WriteLine();
            }
        }
    }
}