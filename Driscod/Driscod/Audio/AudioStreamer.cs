using Concentus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Driscod.Audio
{
    internal class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string[] SupportedEncryptionModes = new[] { "xsalsa20_poly1305" };

        private static readonly Random Random = new Random();

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

        public string EncryptionMode { get; set; } = "xsalsa20_poly1305";

        public int LocalPort { get; set; }

        public IPEndPoint SocketEndPoint { get; set; }

        public uint Ssrc { get; set; }

        public event EventHandler OnAudioStart;

        public event EventHandler OnAudioStop;

        private UdpClient UdpClient
        {
            get
            {
                if (_udpClient == null)
                {
                    _udpClient = new UdpClient(LocalPort);
                    _udpClient.Connect(SocketEndPoint);
                    Logger.Debug($"Connecting to {SocketEndPoint.Address}:{SocketEndPoint.Port}");
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
            var encoder = OpusEncoder.Create(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);

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

                var nextPacketTime = Environment.TickCount;

                var stopwatch = Stopwatch.StartNew();

                while (!_cancellationToken.IsCancellationRequested)
                {
                    byte[] packet = null;

                    lock (QueuedOpusPackets)
                    {
                        if (QueuedOpusPackets.Any())
                        {
                            if (!Playing)
                            {
                                Playing = true;
                                OnAudioStart.Invoke(this, null);
                            }

                            packet = AssemblePacket(QueuedOpusPackets.Dequeue(), sequence, timestamp);
                        }
                        else
                        {
                            if (Playing)
                            {
                                Playing = false;
                                OnAudioStop.Invoke(this, null);
                            }
                        }
                    }

                    while (stopwatch.Elapsed.TotalMilliseconds < PacketIntervalMilliseconds)
                    {
                        // intentionally empty
                    }
                    stopwatch.Restart();

                    if (packet != null)
                    {
                        UdpClient.Send(packet, packet.Length);
                    }

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
            SendAudio(Enumerable.Repeat(0f, SamplesPerPacket * 10).ToArray(), queueSilence: false);
        }

        private byte[] AssemblePacket(byte[] opusData, ushort sequence, uint timestamp)
        {
            var packet = new List<byte>() { 0x80, 0x78 };

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

            var nonce = new byte[24];
            packet.CopyTo(nonce, 0);

            if (!SupportedEncryptionModes.Contains(EncryptionMode))
            {
                throw new InvalidOperationException($"Encryption mode '{EncryptionMode}' is not supported.");
            }

            packet.AddRange(Encrypt(opusData, EncryptionKey, nonce));

            return packet.ToArray();
        }

        private byte[] Encrypt(byte[] bytes, byte[] key, byte[] nonce)
        {
            var salsa = new XSalsa20Engine();
            var poly = new Poly1305();

            salsa.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));

            byte[] subKey = new byte[key.Length];
            salsa.ProcessBytes(subKey, 0, key.Length, subKey, 0);

            byte[] output = new byte[bytes.Length + poly.GetMacSize()];

            salsa.ProcessBytes(bytes, 0, bytes.Length, output, poly.GetMacSize());

            poly.Init(new KeyParameter(subKey));
            poly.BlockUpdate(output, poly.GetMacSize(), bytes.Length);
            poly.DoFinal(output, 0);

            return output;
        }
    }
}
