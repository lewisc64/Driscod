using Driscod.Audio.Encoding;
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

        private CancellationToken _globalCancellationToken;

        private int SamplesPerPacket => SampleRate * PacketIntervalMilliseconds / 1000;

        private int MaxQueuedPackets => (int)MaxPacketBuffer.TotalMilliseconds / PacketIntervalMilliseconds;

        private readonly Queue<byte[]> QueuedPackets = new Queue<byte[]>();

        public bool SendingAudio { get; set; } = false;

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
            _globalCancellationToken = cancellationToken;

            AudioLoop().Forget();
        }

        public void SendAudio(Stream sampleStream, bool queueSilence = true, CancellationToken cancellationToken = default)
        {
            const int ChunkSize = 16;

            var encoder = new OpusAudioEncoder(SampleRate, Channels);

            lock (_audioSendLock)
            {
                var chunk = new Queue<byte[]>();
                var bytesReadOfChunk = 0;

                while (!_globalCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var sampleBytes = new byte[SamplesPerPacket * Channels * 4];
                    var bytesReadOfPacket = sampleStream.Read(sampleBytes, bytesReadOfChunk, sampleBytes.Length);

                    chunk.Enqueue(encoder.Encode(CastSamplesAsFloats(sampleBytes)));

                    if (chunk.Count >= ChunkSize)
                    {
                        EnqueuePackets(chunk);
                        chunk.Clear();
                    }

                    if (bytesReadOfPacket < sampleBytes.Length)
                    {
                        break;
                    }

                    bytesReadOfChunk += SamplesPerPacket * Channels;
                }

                EnqueuePackets(chunk);

                if (queueSilence)
                {
                    QueueSilence();
                }
            }
        }

        public void SendAudio(float[] samples, bool queueSilence = false, CancellationToken cancellationToken = default)
        {
            SendAudio(new MemoryStream(samples.Select(x => BitConverter.GetBytes(x)).Aggregate(new List<byte>(), (acc, value) =>
            {
                acc.AddRange(value);
                return acc;
            }).ToArray()), queueSilence: queueSilence, cancellationToken: cancellationToken);
        }

        public void SendAudio(IAudioSource audioSource, bool queueSilence = false, CancellationToken cancellationToken = default)
        {
            SendAudio(audioSource.GetSampleStream(SampleRate, Channels), queueSilence: queueSilence, cancellationToken: cancellationToken);
        }

        public void ClearAudio()
        {
            lock (_packetQueueLock)
            {
                QueuedPackets.Clear();
            }
        }

        private async Task AudioLoop()
        {
            try
            {
                uint timestamp = 0;
                ushort sequence = 0;

                var timer = new DriftTimer(TimeSpan.FromMilliseconds(PacketIntervalMilliseconds));

                while (!_globalCancellationToken.IsCancellationRequested)
                {
                    var packet = GetNextPacket(sequence, timestamp);

                    await timer.Wait(_globalCancellationToken);

                    if (packet.Length > 0)
                    {
                        await UdpSocket.Send(packet);
                    }

                    sequence++;
                    timestamp += (uint)SamplesPerPacket;
                }
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Exception in audio loop: {ex}");
            }
            finally
            {
                Logger.Debug("Audio loop stopped.");
                ClearAudio();
                if (SendingAudio)
                {
                    SendingAudio = false;
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

        private void EnqueuePackets(IEnumerable<byte[]> packets)
        {
            foreach (var packet in packets)
            {
                EnqueuePacket(packet);
            }
        }

        private void EnqueuePacket(byte[] packet)
        {
            while (QueuedPackets.Count > MaxQueuedPackets)
            {
                Thread.Sleep(PacketIntervalMilliseconds / 2);
            }
            lock (_packetQueueLock)
            {
                QueuedPackets.Enqueue(packet);
            }
        }

        private byte[] GetNextPacket(ushort sequence, uint timestamp)
        {
            lock (_packetQueueLock)
            {
                if (QueuedPackets.Any())
                {
                    if (!SendingAudio)
                    {
                        SendingAudio = true;
                        Task.Run(() =>
                        {
                            OnAudioStart?.Invoke(this, EventArgs.Empty);
                        }).Forget();
                    }

                    return AssemblePacket(QueuedPackets.Dequeue(), sequence, timestamp);
                }
                else
                {
                    if (SendingAudio)
                    {
                        SendingAudio = false;
                        Task.Run(() =>
                        {
                            OnAudioStop?.Invoke(this, EventArgs.Empty);
                        }).Forget();
                    }
                }
            }

            return new byte[0];
        }

        private byte[] AssemblePacket(byte[] data, ushort sequence, uint timestamp)
        {
            return new RtpPacketGenerator()
                .CreateHeader(sequence, timestamp, Ssrc)
                .AddPayload(data)
                .EncryptPayload(EncryptionMode, EncryptionKey)
                .Finalize();
        }

        private float[] CastSamplesAsFloats(byte[] sampleBytes)
        {
            var samples = new float[sampleBytes.Length / 4];
            for (var j = 0; j < sampleBytes.Length; j += 4)
            {
                samples[j / 4] = BitConverter.ToSingle(sampleBytes, j);
            }
            return samples;
        }
    }
}
