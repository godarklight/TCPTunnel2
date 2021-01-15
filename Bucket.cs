using System;

namespace TCPTunnel2
{
    public class Bucket
    {
        //How large the bucket is
        public long bucketMax { get; private set; }
        //The last time bytes were requested from the bucket (ticks)
        public long lastTime { get; private set; }
        //The rate at which the bucket fills (bytes per second)
        public long bucketRate { get; private set; }
        //The amount of data available in the bucket (kBytes)
        public long bucketBytes { get; private set; }
        private Bucket parent;

        public Bucket(long bucketMax, long bucketRate, Bucket parent)
        {
            this.bucketMax = bucketMax * 1024;
            this.bucketRate = bucketRate * 1024;
            this.bucketBytes = bucketMax;
            this.lastTime = DateTime.UtcNow.Ticks;
            this.parent = parent;
        }

        private void FillBucket()
        {
            long currentTime = DateTime.UtcNow.Ticks;
            long diffTime = currentTime - lastTime;
            long newBytes = (diffTime * bucketRate) / TimeSpan.TicksPerSecond;
            if (newBytes > 0)
            {
                bucketBytes += newBytes;
                lastTime = currentTime;
            }
            if (bucketBytes < 0)
            {
                bucketBytes = 0;
            }
            if (bucketBytes > bucketMax)
            {
                bucketBytes = bucketMax;
            }
        }

        private void RemoveBytes(long bytes)
        {
            if (parent != null)
            {
                parent.RemoveBytes(bytes);
            }
            bucketBytes -= bytes;
        }

        public bool RequestBytes(long bytes)
        {
            if (TestBytes(bytes))
            {
                RemoveBytes(bytes);
                return true;
            }
            return false;
        }

        public bool TestBytes(long bytes)
        {
            if (!LocalTestBytes(bytes))
            {
                //We don't have enough
                return false;
            }
            if (parent != null && !parent.TestBytes(bytes))
            {
                //Parent doesn't have enough data so we can't send.
                return false;
            }
            //We don't have enough data so we can't send
            return true;
        }

        private bool LocalTestBytes(long bytes)
        {
            FillBucket();
            if (bucketBytes < bytes)
            {
                return false;
            }
            return true;
        }

        public void ChangeRate(long bucketRate, long bucketMax)
        {
            this.bucketRate = bucketRate;
            this.bucketMax = bucketMax;
        }

        public void LimitRate(long bucketRate, long bucketMax)
        {
            if (this.bucketRate > bucketRate)
            {
                this.bucketRate = bucketBytes;
            }
            if (this.bucketMax > bucketMax)
            {
                this.bucketMax = bucketMax;
            }
        }
    }
}