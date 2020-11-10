using Concentus.Enums;
using Concentus.Structs;
using Driscod.Audio;
using Sodium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace Driscod.Audio
{
    internal class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly Random Random = new Random();

        private const string EncryptionMode = "xsalsa20_poly1305_suffix";

        private Thread _loopThread;

        private UdpClient _udpClient;

        private CancellationToken _cancellationToken;

        private int SamplesPerPacket => SampleRate * PacketIntervalMilliseconds / 1000;

        private readonly Queue<byte[]> QueuedRtpPayloads = new Queue<byte[]>();

        private bool Playing { get; set; } = false;

        public int SampleRate { get; set; } = 48000;

        public int PacketIntervalMilliseconds { get; set; } = 20;

        public int Channels { get; set; } = 2;

        public byte[] EncryptionKey { get; set; }

        public string Address { get; set; }

        public int LocalPort { get; set; }

        public int Port { get; set; }

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
                    _udpClient.Connect(Address, Port);
                    Logger.Debug($"Connecting to {Address}:{Port}");
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

                lock (QueuedRtpPayloads)
                {
                    var nonce = Enumerable.Range(1, 24).Select(x => (byte)Random.Next(byte.MinValue, byte.MaxValue)).ToArray();

                    chunk.Enqueue(StreamEncryption.Encrypt(opusPacket, nonce, EncryptionKey ?? throw new InvalidOperationException($"{nameof(EncryptionKey)} is null.")).Concat(nonce).ToArray());

                    if (chunk.Count >= 10)
                    {
                        while (chunk.Any())
                        {
                            QueuedRtpPayloads.Enqueue(chunk.Dequeue());
                        }
                    }
                }
            }

            while (chunk.Any())
            {
                QueuedRtpPayloads.Enqueue(chunk.Dequeue());
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

                    lock (QueuedRtpPayloads)
                    {
                        if (QueuedRtpPayloads.Any())
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

                            packet.AddRange(QueuedRtpPayloads.Dequeue());
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
