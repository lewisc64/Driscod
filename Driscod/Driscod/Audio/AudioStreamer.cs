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
using System.IO;

namespace Driscod.Audio
{
    internal class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly string[] SupportedEncryptionModes = new[] { "xsalsa20_poly1305" };

        private readonly object _audioSendLock = new object();

#pragma warning disable S1450 // Private fields only used as local variables in methods should become local variables
        private readonly Thread _loopThread;
#pragma warning restore S1450

        private UdpClient _udpClient;

        private CancellationToken _cancellationToken;

        private int SamplesPerPacket => SampleRate * PacketIntervalMilliseconds / 1000;

        private int MaxQueuedOpusPackets => (int)MaxPacketBuffer.TotalMilliseconds / PacketIntervalMilliseconds;

        private readonly Queue<byte[]> QueuedOpusPackets = new Queue<byte[]>();

        public bool Playing { get; set; } = false;

        public int SampleRate { get; set; } = 48000;

        public int PacketIntervalMilliseconds { get; set; } = 20;

        public TimeSpan MaxPacketBuffer { get; set; } = TimeSpan.FromMinutes(5);

        public int Channels { get; set; } = 2;

        public byte[] EncryptionKey { get; set; }

        public string EncryptionMode { get; set; } = SupportedEncryptionModes.First();

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

        public void SendAudio(Stream sampleStream, bool queueSilence = true)
        {
            const int ChunkSize = 16;

            lock (_audioSendLock)
            {
                var chunk = new Queue<byte[]>();

                var encoder = OpusEncoder.Create(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);

                var i = 0;
                while (!_cancellationToken.IsCancellationRequested)
                {
                    var sampleBytes = new byte[SamplesPerPacket * Channels * 4];
                    var bytesRead = sampleStream.Read(sampleBytes, i, sampleBytes.Length);

                    var samples = new float[sampleBytes.Length / 4];
                    for (var j = 0; j < sampleBytes.Length; j += 4)
                    {
                        samples[j / 4] = BitConverter.ToSingle(sampleBytes, j);
                    }

                    var opusPacket = new byte[(int)(SamplesPerPacket * 1.1)];
                    int opusPacketSize = encoder.Encode(samples, 0, SamplesPerPacket, opusPacket, 0, opusPacket.Length);
                    opusPacket = opusPacket.Take(opusPacketSize).ToArray();

                    chunk.Enqueue(opusPacket);

                    if (chunk.Count >= ChunkSize)
                    {
                        EnqueueOpusPackets(chunk);
                        chunk.Clear();
                    }

                    if (bytesRead < sampleBytes.Length)
                    {
                        break;
                    }

                    i += SamplesPerPacket * Channels;
                }

                EnqueueOpusPackets(chunk);

                if (queueSilence)
                {
                    QueueSilence();
                }
            }
        }

        public void SendAudio(float[] samples, bool queueSilence = false)
        {
            SendAudio(new MemoryStream(samples.Select(x => BitConverter.GetBytes(x)).Aggregate(new List<byte>(), (acc, value) =>
            {
                acc.AddRange(value);
                return acc;
            }).ToArray()), queueSilence: queueSilence);
        }

        public void SendAudio(IAudioSource audioSource, bool queueSilence = false)
        {
            SendAudio(audioSource.GetSampleStream(SampleRate, Channels), queueSilence: queueSilence);
        }

        public void ClearAudio()
        {
            lock (QueuedOpusPackets)
            {
                QueuedOpusPackets.Clear();
            }
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

                    if (packet != null)
                    {
                        while (stopwatch.Elapsed.TotalMilliseconds < PacketIntervalMilliseconds)
                        {
                            // intentionally empty
                        }

                        UdpClient.SendAsync(packet, packet.Length);
                    }
                    else
                    {
                        // use inaccurate timing when no packets are being sent to save CPU.
                        Thread.Sleep(Math.Max(PacketIntervalMilliseconds - (int)stopwatch.Elapsed.TotalMilliseconds, 0));
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
            finally
            {
                ClearAudio();
                if (Playing)
                {
                    Playing = false;
                    OnAudioStop.Invoke(this, null);
                }
            }
        }

        private void QueueSilence()
        {
            SendAudio(Enumerable.Repeat(0f, SamplesPerPacket * 10).ToArray(), queueSilence: false);
        }

        private void EnqueueOpusPackets(IEnumerable<byte[]> packets)
        {
            foreach (var packet in packets)
            {
                EnqueueOpusPacket(packet);
            }
        }

        private void EnqueueOpusPacket(byte[] packet)
        {
            while (QueuedOpusPackets.Count > MaxQueuedOpusPackets)
            {
                Thread.Sleep(10);
            }
            QueuedOpusPackets.Enqueue(packet);
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
