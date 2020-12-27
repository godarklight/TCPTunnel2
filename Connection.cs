using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TCPTunnel2
{
    public class Connection
    {
        private int connectionID;
        public IPEndPoint sendEndpoint;
        private bool connected = true;
        private Bucket uploadBucket;
        private UDPServer udpServer;
        private TcpClient tcpClient;
        //Network state
        private short sendSequence = 0;
        private short sendAck = 0;
        private short receiveSequence = 0;
        //TCP read buffer
        private byte[] readBuffer = new byte[500];
        //Threads
        private Thread tcpSendTask;
        private Thread tcpReceiveTask;
        //UDP send
        private ConcurrentQueue<ByteArray> udpSendQueue = new ConcurrentQueue<ByteArray>();
        private ConcurrentQueue<OutgoingMessage> udpDoubleSendQueue = new ConcurrentQueue<OutgoingMessage>();
        private ConcurrentQueue<OutgoingMessage> udpRetransmitSendQueue = new ConcurrentQueue<OutgoingMessage>();
        private ConcurrentDictionary<int, ByteArray> heldMessages = new ConcurrentDictionary<int, ByteArray>();
        //TCP Send
        private AutoResetEvent tcpSendEvent = new AutoResetEvent(true);
        private ConcurrentQueue<ByteArray> tcpSendQueue = new ConcurrentQueue<ByteArray>();
        private long disconnectTime;
        private long sendTime;
        private TunnelSettings settings;

        public Connection(int connectionID, TunnelSettings settings, TcpClient tcpClient, UDPServer udpServer, Bucket uploadBucket)
        {
            this.connectionID = connectionID;
            this.tcpClient = tcpClient;
            this.udpServer = udpServer;
            this.uploadBucket = uploadBucket;
            this.settings = settings;
            tcpSendTask = new Thread(new ThreadStart(SendLoop));
            tcpSendTask.Name = "Connection.SendLoop." + connectionID;
            tcpSendTask.Start();
            tcpReceiveTask = new Thread(new ThreadStart(ReceiveLoop));
            tcpReceiveTask.Name = "Connection.ReceiveLoop." + connectionID;
            tcpReceiveTask.Start();
            disconnectTime = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerSecond * 5);
        }

        public bool Check()
        {
            long currentTime = DateTime.UtcNow.Ticks;
            if (connected == false)
            {
                return false;
            }
            if (currentTime > disconnectTime)
            {
                Console.WriteLine("Timeout");
                return false;
            }
            return true;
        }

        public void Disconnect(string reason)
        {
            if (!connected)
            {
                return;
            }
            Console.WriteLine($"Disconnecting {connectionID}: {reason}");
            connected = false;
            try
            {
                tcpClient.Close();
            }
            catch
            {
                //Don't care
            }
        }

        public void HandleUDPData(short messageType, short messageLength, short messageSequence, short messageACK, ByteArray payload, IPEndPoint receiveEndpoint)
        {
            //We're the client and now we have the connection to the server
            if (sendEndpoint == null)
            {
                sendEndpoint = receiveEndpoint;
            }
            sendAck = messageACK;
            disconnectTime = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerSecond * 5);
            bool releaseData = true;
            if (messageType == 1)
            {
                if (messageSequence == receiveSequence)
                {
                    releaseData = false;
                    receiveSequence++;
                    tcpSendQueue.Enqueue(payload);
                    while (heldMessages.TryRemove(receiveSequence, out ByteArray heldData))
                    {
                        receiveSequence++;
                        tcpSendQueue.Enqueue(heldData);
                    }
                    tcpSendEvent.Set();
                }
                else
                {
                    if (AckGreaterThan(messageSequence, receiveSequence))
                    {
                        if (!heldMessages.ContainsKey(messageSequence))
                        {
                            //A message from the future out of sequence
                            heldMessages.TryAdd(messageSequence, payload);
                            releaseData = false;
                        }
                    }
                }
            }
            if (payload != null && releaseData)
            {
                Recycler.Release(payload);
            }
        }

        public bool GetUDPMessage(out ByteArray sendMessage)
        {

            sendMessage = null;
            //Not enough data to send a packet
            if (!uploadBucket.TestBytes(500))
            {
                return false;
            }
            long currentTime = DateTime.UtcNow.Ticks;
            OutgoingMessage om;
            //Send retransmits
            if (sendMessage == null && udpRetransmitSendQueue.TryPeek(out om))
            {
                if (currentTime > om.sendTime)
                {
                    bool skipping = true;
                    while (skipping)
                    {
                        if (udpRetransmitSendQueue.TryDequeue(out om))
                        {
                            //Don't send messages they have acknowledged
                            if (AckGreaterThan(om.sequence, sendAck))
                            {
                                //Send back to the retransmit queue                                
                                om.sendTime = currentTime + (settings.retransmit * TimeSpan.TicksPerMillisecond);
                                udpRetransmitSendQueue.Enqueue(om);
                                //Send
                                sendMessage = om.data;
                                break;
                            }
                            else
                            {
                                Recycler.Release(om.data);
                            }
                        }
                        else
                        {
                            skipping = false;
                        }
                    }
                }
            }
            //Send double sends
            if (sendMessage == null)
            {
                bool skipping = true;
                while (skipping)
                {
                    if (udpDoubleSendQueue.TryPeek(out om))
                    {
                        //This isn't ready to transmit yet.
                        if (om.sendTime > currentTime)
                        {
                            break;
                        }
                    }
                    if (udpDoubleSendQueue.TryDequeue(out om))
                    {
                        if (AckGreaterThan(om.sequence, sendAck))
                        {
                            //Send to the retransmit queue
                            om.sendTime = currentTime + (settings.retransmit * TimeSpan.TicksPerMillisecond);
                            udpRetransmitSendQueue.Enqueue(om);
                            //Send
                            sendMessage = om.data;
                            break;
                        }
                        else
                        {
                            //Already ACK'd, skip it
                            Recycler.Release(om.data);
                        }
                    }
                    else
                    {
                        skipping = false;
                    }
                }
            }

            //Take new waiting data and build a message if we have less than 10000 queue'd packets
            int ackDiff = sendSequence - sendAck;
            if (ackDiff < 0)
            {
                ackDiff = ackDiff + ushort.MaxValue;
            }
            if (sendMessage == null && ackDiff < 10000 && udpSendQueue.Count > 0)
            {
                ByteArray payload = Recycler.Grab();
                ByteArray addMessage;
                //Grab upto 500 bytes
                while (udpSendQueue.TryPeek(out addMessage))
                {
                    if (payload.length + addMessage.length > 500)
                    {
                        break;
                    }
                    else
                    {
                        udpSendQueue.TryDequeue(out addMessage);
                        Array.Copy(addMessage.data, 0, payload.data, payload.length, addMessage.length);
                        payload.length += addMessage.length;
                        Recycler.Release(addMessage);
                    }
                }
                om = new OutgoingMessage();
                om.sequence = sendSequence++;
                om.data = GetUDPMessageData(1, om.sequence, payload);
                //Send it to the correct retransmit queue
                if (settings.initialRetransmit > 0)
                {
                    om.sendTime = currentTime + (settings.initialRetransmit * TimeSpan.TicksPerMillisecond);
                    udpDoubleSendQueue.Enqueue(om);
                }
                else
                {
                    om.sendTime = currentTime + (settings.retransmit * TimeSpan.TicksPerMillisecond);
                    udpRetransmitSendQueue.Enqueue(om);
                }
                //Send
                sendMessage = om.data;
            }

            //Send heartbeats
            if (sendMessage == null && currentTime > sendTime)
            {
                sendMessage = GetHeartbeat();
            }

            //Send
            if (sendMessage != null)
            {
                sendTime = currentTime + (TimeSpan.TicksPerMillisecond * 100);
                //Update ACK
                WriteInt16(receiveSequence - 1, sendMessage.data, 14);
                while (!uploadBucket.RequestBytes(sendMessage.length))
                {
                    Thread.Sleep(1);
                }
                return true;
            }
            return false;
        }

        public ByteArray GetHeartbeat()
        {
            return GetUDPMessageData(0, 0, null);
        }

        private ByteArray GetUDPMessageData(short type, short sequence, ByteArray payload)
        {
            ByteArray byteArray = Recycler.Grab();
            byteArray.length = 16;
            //Write magic header, DTT2
            byteArray.data[0] = 68;
            byteArray.data[1] = 84;
            byteArray.data[2] = 84;
            byteArray.data[3] = 50;
            WriteInt32(connectionID, byteArray.data, 4);
            WriteInt16(type, byteArray.data, 8);
            if (payload == null)
            {
                WriteInt16(0, byteArray.data, 10);
            }
            else
            {
                byteArray.length += payload.length;
                WriteInt16(payload.length, byteArray.data, 10);
                Array.Copy(payload.data, 0, byteArray.data, 16, payload.length);
                Recycler.Release(payload);
            }
            WriteInt16(sequence, byteArray.data, 12);
            WriteInt16(receiveSequence, byteArray.data, 14);
            return byteArray;
        }

        private void ReceiveLoop()
        {
            try
            {
                while (connected)
                {
                    int bytesRead = tcpClient.GetStream().Read(readBuffer, 0, readBuffer.Length);
                    if (bytesRead > 0)
                    {
                        udpSendQueue.Enqueue(Recycler.FromBytes(readBuffer, bytesRead));
                        udpServer.SendEvent();
                    }
                    else
                    {
                        Disconnect("Receive connection closed");
                    }
                }
            }
            catch (Exception e)
            {
                Disconnect($"Exception {e}");
            }
            Disconnect("Receive loop exited for shutdown");
        }

        private void SendLoop()
        {
            try
            {
                while (connected)
                {
                    tcpSendEvent.WaitOne();
                    while (tcpSendQueue.TryDequeue(out ByteArray sendBytes))
                    {
                        tcpClient.GetStream().Write(sendBytes.data, 0, sendBytes.length);
                        Recycler.Release(sendBytes);
                    }
                }
            }
            catch
            {
                Disconnect("Sending connection closed");
            }
        }


        public static void WriteInt16(int number, byte[] data, int index)
        {
            uint unumber = (uint)number;
            data[index] = (byte)((unumber >> 8) & 0xFF);
            data[index + 1] = (byte)((unumber) & 0xFF);
        }

        public static void WriteInt32(int number, byte[] data, int index)
        {
            uint unumber = (uint)number;
            data[index] = (byte)((unumber >> 24) & 0xFF);
            data[index + 1] = (byte)((unumber >> 16) & 0xFF);
            data[index + 2] = (byte)((unumber >> 8) & 0xFF);
            data[index + 3] = (byte)((unumber) & 0xFF);
        }

        private bool AckGreaterThan(short lhs, short rhs)
        {
            int distance = Math.Abs(lhs - rhs);
            bool distanceBig = distance > Int16.MaxValue / 2;
            if (distanceBig)
            {
                return lhs < rhs;
            }
            return lhs > rhs;
        }
    }
}