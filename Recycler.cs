using System;
using System.Collections.Concurrent;

namespace TCPTunnel2
{
    public static class Recycler
    {
        private static ConcurrentQueue<ByteArray> freeByteArrays = new ConcurrentQueue<ByteArray>();
        private static int gcCount = 0;
        private static int allocated = 0;

        public static ByteArray Grab()
        {
            allocated++;
            if (freeByteArrays.TryDequeue(out ByteArray byteArray))
            {
                if (!byteArray.free)
                {
                    throw new ArgumentException("Cannot grab a ByteArray that is not free");
                }
                byteArray.free = false;
                byteArray.length = 0;
                return byteArray;
            }
            return new ByteArray();
        }

        public static void Release(ByteArray byteArray)
        {
            if (byteArray.free)
            {
                throw new ArgumentException("Cannot release a ByteArray that is already free");
            }
            byteArray.free = true;
            allocated--;
            if (freeByteArrays.Count > 1000)
            {
                freeByteArrays.Enqueue(byteArray);
            }
            else
            {
                gcCount++;
                if (gcCount > 1000)
                {
                    gcCount = 0;
                    GC.Collect();
                }
            }
        }

        public static ByteArray FromBytes(byte[] data, int length)
        {
            ByteArray byteArray = Grab();
            Array.Copy(data, 0, byteArray.data, 0, length);
            byteArray.length = length;
            return byteArray;
        }

        public static int GetAllocated()
        {
            return allocated;
        }

        public static int GetFree()
        {
            return freeByteArrays.Count;
        }
    }
}