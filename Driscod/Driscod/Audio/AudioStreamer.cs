using Concentus.Enums;
using Concentus.Structs;
using Sodium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Driscod.Audio
{
    internal class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly Random Random = new Random();

        private const string EncryptionMode = "xsalsa20_poly1305";

#pragma warning disable S1450 // Private fields only used as local variables in methods should become local variables
        private readonly Thread _loopThread;
#pragma warning restore S1450

        private UdpClient _udpClient;

        private CancellationToken _cancellationToken;

        private int SamplesPerPacket => SampleRate * PacketIntervalMilliseconds / 1000;

        private readonly Queue<byte[]> QueuedOpusPackets = new Queue<byte[]>();

        private bool Playing { get; set; } = false;

        public int SampleRate { get; set; } = 48000;

        public int PacketIntervalMilliseconds { get; set; } = 20;

        public int Channels { get; set; } = 2;

        public byte[] EncryptionKey { get; set; }

        public int LocalPort { get; set; }

        public IPEndPoint SocketEndPoint { get; set; }

        public uint Ssrc { get; set; }

        public Action AudioStartCallback { get; set; }

        public Action AudioStopCallback { get; set; }

        private UdpClient UdpClient
        {
            get
            {
                if (_udpClient == null)
                {
                    _udpClient = new UdpClient(LocalPort);
                    _udpClient.Connect(SocketEndPoint);
                    Logger.Debug($"Connecting to {SocketEndPoint.Address}:{SocketEndPoint.Port}");

                    //byte[] ssrcBytes;

                    //if (BitConverter.IsLittleEndian)
                    //{
                    //    ssrcBytes = BitConverter.GetBytes(Ssrc).Reverse().ToArray();
                    //}
                    //else
                    //{
                    //    ssrcBytes = BitConverter.GetBytes(Ssrc);
                    //}

                    //var datagram = new byte[] { 0, 1, 0, 70 }.Concat(ssrcBytes).Concat(Enumerable.Repeat((byte)0, 66)).ToArray();

                    //var end = SocketEndPoint;

                    //_udpClient.Send(datagram, datagram.Length);
                    //var response = _udpClient.Receive(ref end);

                    //var port = BitConverter.ToUInt16(response.Reverse().Take(2).ToArray(), 0);
                    //var address = System.Text.Encoding.UTF8.GetString(response.Skip(8).TakeWhile(x => x != 0).ToArray());
                }
                return _udpClient;
            }
        }

        public AudioStreamer()
            : this(new CancellationToken())
        {
        }

        public AudioStreamer(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;

            _loopThread = new Thread(AudioLoop);
            _loopThread.Start();
        }

        public void SendAudio(float[] samples, bool queueSilence = true)
        {
            Logger.Info("Queuing audio");

            OpusEncoder encoder = OpusEncoder.Create(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);

            var chunk = new Queue<byte[]>();

            for (var i = 0; i < samples.Length - SamplesPerPacket * Channels; i += SamplesPerPacket * Channels)
            {
                var opusPacket = new byte[(int)(SamplesPerPacket * 1.1)];
                int opusPacketSize = encoder.Encode(samples, i, SamplesPerPacket, opusPacket, 0, opusPacket.Length);
                opusPacket = opusPacket.Take(opusPacketSize).ToArray();

                lock (QueuedOpusPackets)
                {
                    chunk.Enqueue(opusPacket);

                    if (chunk.Count >= 10)
                    {
                        while (chunk.Any())
                        {
                            QueuedOpusPackets.Enqueue(chunk.Dequeue());
                        }
                    }
                }
            }

            while (chunk.Any())
            {
                QueuedOpusPackets.Enqueue(chunk.Dequeue());
            }

            if (queueSilence)
            {
                QueueSilence();
            }
        }

        public void SendAudio(IAudioSource audioSource)
        {
            SendAudio(audioSource.GetSamples(SampleRate, Channels));
        }

        private void AudioLoop()
        {
            try
            {
                uint timestamp = 0;
                ushort sequence = 0;

                var stopwatch = Stopwatch.StartNew();

                while (!_cancellationToken.IsCancellationRequested)
                {
                    List<byte> packet = null;

                    lock (QueuedOpusPackets)
                    {
                        if (QueuedOpusPackets.Any())
                        {
                            if (!Playing)
                            {
                                Playing = true;
                                AudioStartCallback?.Invoke();
                            }

                            packet = new List<byte>() { 0x80, 0x78 };

                            if (BitConverter.IsLittleEndian)
                            {
                                packet.AddRange(BitConverter.GetBytes(sequence).Reverse());
                                packet.AddRange(BitConverter.GetBytes(timestamp).Reverse());
                                packet.AddRange(BitConverter.GetBytes(Ssrc).Reverse());
                            }
                            else
                            {
                                packet.AddRange(BitConverter.GetBytes(sequence));
                                packet.AddRange(BitConverter.GetBytes(timestamp));
                                packet.AddRange(BitConverter.GetBytes(Ssrc));
                            }

                            var opusPacket = QueuedOpusPackets.Dequeue();

                            var nonce = new byte[24];
                            packet.CopyTo(nonce, 0);

                            var encryptedOpusPacket = StreamEncryption.Encrypt(opusPacket, nonce, EncryptionKey ?? throw new InvalidOperationException($"{nameof(EncryptionKey)} is null."));

                            packet.AddRange(encryptedOpusPacket);
                        }
                        else
                        {
                            if (Playing)
                            {
                                Playing = false;
                                AudioStopCallback?.Invoke();
                            }
                        }
                    }

                    while (stopwatch.Elapsed.TotalMilliseconds < PacketIntervalMilliseconds)
                    {
                        // intentionally empty
                    }
                    if (packet != null)
                    {
                        UdpClient.SendAsync(packet.ToArray(), packet.Count);
                    }
                    stopwatch.Restart();

                    sequence++;
                    timestamp += (uint)SamplesPerPacket;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Exception in audio loop: {ex}");
            }
        }

        private void QueueSilence()
        {
            Logger.Info("Silence inbound");
            SendAudio(Enumerable.Repeat(0f, SamplesPerPacket * 10).ToArray(), queueSilence: false);
        }
    }
}
