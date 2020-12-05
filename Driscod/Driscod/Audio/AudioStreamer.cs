using Concentus.Enums;
using Concentus.Structs;
using Driscod.Extensions;
using Driscod.Network.Udp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly object _audioSendLock = new object();

        private readonly object _packetQueueLock = new object();

        private UdpSocket _udpSocket;

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

        public string EncryptionMode { get; set; }

        public int LocalPort { get; set; }

        public IPEndPoint SocketEndPoint { get; set; }

        public uint Ssrc { get; set; }

        public event EventHandler OnAudioStart;

        public event EventHandler OnAudioStop;

        private UdpSocket UdpSocket
        {
            get
            {
                if (_udpSocket == null)
                {
                    _udpSocket = new UdpSocket(SocketEndPoint);
                }
                return _udpSocket;
            }
        }

        public AudioStreamer()
            : this(new CancellationToken())
        {
        }

        public AudioStreamer(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;

            AudioLoop().Forget();
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

        private async Task AudioLoop()
        {
            try
            {
                uint timestamp = 0;
                ushort sequence = 0;

                var timer = new DriftTimer(TimeSpan.FromMilliseconds(PacketIntervalMilliseconds));

                while (!_cancellationToken.IsCancellationRequested)
                {
                    var packet = GetNextPacket(sequence, timestamp);

                    await timer.Wait(_cancellationToken);

                    if (packet.Length > 0)
                    {
                        await UdpSocket.Send(packet);
                    }

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
                Logger.Debug("Audio loop stopped.");
                ClearAudio();
                if (Playing)
                {
                    Playing = false;
                    Task.Run(() =>
                    {
                        OnAudioStop?.Invoke(this, EventArgs.Empty);
                    }).Forget();
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
                Thread.Sleep(PacketIntervalMilliseconds / 2);
            }
            lock (_packetQueueLock)
            {
                QueuedOpusPackets.Enqueue(packet);
            }
        }

        private byte[] GetNextPacket(ushort sequence, uint timestamp)
        {
            lock (_packetQueueLock)
            {
                if (QueuedOpusPackets.Any())
                {
                    if (!Playing)
                    {
                        Playing = true;
                        Task.Run(() =>
                        {
                            OnAudioStart?.Invoke(this, EventArgs.Empty);
                        }).Forget();
                    }

                    return AssemblePacket(QueuedOpusPackets.Dequeue(), sequence, timestamp);
                }
                else
                {
                    if (Playing)
                    {
                        Playing = false;
                        Task.Run(() =>
                        {
                            OnAudioStop?.Invoke(this, EventArgs.Empty);
                        }).Forget();
                    }
                }
            }

            return new byte[0];
        }

        private byte[] AssemblePacket(byte[] opusData, ushort sequence, uint timestamp)
        {
            return new RtpPacketGenerator()
                .CreateHeader(sequence, timestamp, Ssrc)
                .AddPayload(opusData)
                .EncryptPayload(EncryptionMode, EncryptionKey)
                .Finalize();
        }
    }
}
