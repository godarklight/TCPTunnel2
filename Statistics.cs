using System;
using System.Collections.Concurrent;

namespace TCPTunnel2
{
    public class Statistics
    {
        //UDP statistics
        public long receivedPackets;
        public long receivedUniquePackets;
        public long receivedBytes;
        public long receivedUniqueBytes;
        public long sentPackets;
        public long sentUniquePackets;
        public long sentBytes;
        public long sentUniqueBytes;
    }
}