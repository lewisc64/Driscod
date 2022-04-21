using Driscod.Audio.Encoding;
using Driscod.Extensions;
using Driscod.Network.Udp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

        private const int PacketChunkSize = 16;

        private readonly object _audioSendLock = new object();

        private readonly IAudioEncoder _encoder;

        private readonly Stopwatch _packetlessStopwatch = new Stopwatch();

        private CancellationToken _globalCancellationToken;

        private int SamplesPerPacket => SampleRate * PacketIntervalMilliseconds / 1000;

        private int MaxQueuedPackets => (int)MaxPacketBuffer.TotalMilliseconds / PacketIntervalMilliseconds;

        private readonly ConcurrentQueue<byte[]> QueuedPackets = new ConcurrentQueue<byte[]>();

        public bool TransmittingAudio { get; private set; } = false;

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

        public AudioStreamer(IAudioEncoder encoder, CancellationToken cancellationToken = default)
        {
            _encoder = encoder;
            _globalCancellationToken = cancellationToken;

            new Thread(() => AudioLoop().Wait()).Start();
        }

        public string GetStatsString()
        {
            return $"address={LocalPort}:{SocketEndPoint.Address}:{SocketEndPoint.Port},queuedPackets={QueuedPackets.Count}/{MaxQueuedPackets},silenceTime={_packetlessStopwatch.ElapsedMilliseconds}";
        }

        public void SendAudio(Stream sampleStream, CancellationToken cancellationToken = default)
        {
            _encoder.Setup(SampleRate, Channels);
            var chunk = new List<byte[]>();
            var bytesRead = 0;

            lock (_audioSendLock)
            {
                while (!_globalCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var sampleBytes = new byte[SamplesPerPacket * Channels * 4];
                    var bytesReadOfPacket = sampleStream.Read(sampleBytes, 0, sampleBytes.Length);

                    chunk.Add(_encoder.Encode(CastSamplesAsFloats(sampleBytes)));

                    if (chunk.Count >= PacketChunkSize)
                    {
                        EnqueuePackets(chunk);
                        chunk.Clear();
                    }

                    if (bytesReadOfPacket < sampleBytes.Length)
                    {
                        break;
                    }

                    bytesRead += SamplesPerPacket * Channels;
                }

                EnqueuePackets(chunk);
            }
        }

        public void SendAudio(float[] samples, CancellationToken cancellationToken = default)
        {
            SendAudio(new MemoryStream(samples.SelectMany(x => BitConverter.GetBytes(x)).ToArray()), cancellationToken: cancellationToken);
        }

        public void SendAudio(IAudioSource audioSource, CancellationToken cancellationToken = default)
        {
            SendAudio(audioSource.GetSampleStream(SampleRate, Channels), cancellationToken: cancellationToken);
        }

        public void QueueSilence(int packets = 10)
        {
            SendAudio(Enumerable.Repeat(0f, SamplesPerPacket * Channels * packets).ToArray());
        }

        public void ClearAudio()
        {
            QueuedPackets.Clear();
        }

        private async Task AudioLoop()
        {
            var socket = new UdpSocket(SocketEndPoint);
            var timer = new DriftTimer(TimeSpan.FromMilliseconds(PacketIntervalMilliseconds));

            uint timestamp = 0;
            ushort sequence = 0;

            try
            {
                while (!_globalCancellationToken.IsCancellationRequested)
                {
                    var packet = GetNextPacket(sequence, timestamp);

                    await timer.Wait(_globalCancellationToken);

                    if (packet.Length > 0)
                    {
                        await socket.Send(packet);
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
                if (TransmittingAudio)
                {
                    TransmittingAudio = false;
                    Task.Run(() =>
                    {
                        OnAudioStop?.Invoke(this, EventArgs.Empty);
                    }).Forget();
                }
            }
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
            while (QueuedPackets.Count >= MaxQueuedPackets)
            {
                Thread.Sleep(PacketIntervalMilliseconds / 2);
            }
            QueuedPackets.Enqueue(packet);
        }

        private byte[] GetNextPacket(ushort sequence, uint timestamp)
        {
            if (QueuedPackets.TryDequeue(out byte[] data))
            {
                if (!TransmittingAudio)
                {
                    _packetlessStopwatch.Reset();
                    TransmittingAudio = true;
                    Task.Run(() =>
                    {
                        OnAudioStart?.Invoke(this, EventArgs.Empty);
                    }).Forget();
                }

                return AssemblePacket(data, sequence, timestamp);
            }
            else
            {
                if (TransmittingAudio)
                {
                    if (!_packetlessStopwatch.IsRunning)
                    {
                        _packetlessStopwatch.Restart();
                    }
                    if (_packetlessStopwatch.Elapsed >= TimeSpan.FromSeconds(0.5))
                    {
                        TransmittingAudio = false;
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
