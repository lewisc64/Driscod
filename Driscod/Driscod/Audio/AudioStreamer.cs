using Driscod.Audio.Encoding;
using Driscod.Extensions;
using Driscod.Network;
using Driscod.Network.Udp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public class AudioStreamer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private const int PacketChunkSize = 16;

        private const int PacketIntervalMilliseconds = 20;

        private readonly object _audioSendLock = new object();

        private readonly Stopwatch _packetlessStopwatch = new Stopwatch();

        private readonly ConcurrentQueue<float[]> _rawPayloadQueue = new ConcurrentQueue<float[]>();

        private CancellationToken _globalCancellationToken;

        private int SamplesPerPacket => Encoder.SampleRate * PacketIntervalMilliseconds / 1000;

        private int MaxQueuedPackets => (int)AudioBufferSize.TotalMilliseconds / PacketIntervalMilliseconds;

        public IAudioEncoder Encoder { get; }

        public VoiceEndPointInfo EndPointInfo { get; }

        public TimeSpan AudioBufferSize { get; set; } = TimeSpan.FromMinutes(1);

        public bool TransmittingAudio { get; private set; } = false;

        public event EventHandler OnAudioStart;

        public event EventHandler OnAudioStop;

        public AudioStreamer(IAudioEncoder encoder, VoiceEndPointInfo endPointInfo, CancellationToken cancellationToken = default)
        {
            Encoder = encoder;
            EndPointInfo = endPointInfo;
            _globalCancellationToken = cancellationToken;

            new Thread(() => AudioLoop().Wait()).Start();
        }

        public string GetStatsString()
        {
            return $"address={EndPointInfo.LocalPort}:{EndPointInfo.SocketEndPoint.Address}:{EndPointInfo.SocketEndPoint.Port},queuedPackets={_rawPayloadQueue.Count}/{MaxQueuedPackets},silenceTime={_packetlessStopwatch.ElapsedMilliseconds}";
        }

        public void SendAudio(Stream sampleStream, CancellationToken cancellationToken = default)
        {
            var chunk = new List<float[]>();
            var bytesRead = 0;

            lock (_audioSendLock)
            {
                while (!_globalCancellationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    var sampleBytes = new byte[SamplesPerPacket * Encoder.Channels * 4];
                    var bytesReadOfPacket = sampleStream.Read(sampleBytes, 0, sampleBytes.Length);

                    chunk.Add(CastSamplesAsFloats(sampleBytes));

                    if (chunk.Count >= PacketChunkSize)
                    {
                        EnqueuePackets(chunk, cancellationToken: cancellationToken);
                        chunk.Clear();
                    }

                    if (bytesReadOfPacket < sampleBytes.Length)
                    {
                        break;
                    }

                    bytesRead += SamplesPerPacket * Encoder.Channels;
                }

                EnqueuePackets(chunk, cancellationToken: cancellationToken);
            }
        }

        public void SendAudio(float[] samples, CancellationToken cancellationToken = default)
        {
            SendAudio(new MemoryStream(samples.SelectMany(x => BitConverter.GetBytes(x)).ToArray()), cancellationToken: cancellationToken);
        }

        public void SendAudio(IAudioSource audioSource, CancellationToken cancellationToken = default)
        {
            SendAudio(audioSource.GetSampleStream(Encoder.SampleRate, Encoder.Channels), cancellationToken: cancellationToken);
        }

        public void QueueSilence(int packets = 10)
        {
            SendAudio(Enumerable.Repeat(0f, SamplesPerPacket * Encoder.Channels * packets).ToArray());
        }

        public void ClearAudio()
        {
            _rawPayloadQueue.Clear();
        }

        private async Task AudioLoop()
        {
            var socket = new UdpSocket(EndPointInfo.SocketEndPoint);
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

        private void EnqueuePackets(IEnumerable<float[]> packets, CancellationToken cancellationToken = default)
        {
            foreach (var packet in packets)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                EnqueuePacket(packet);
            }
        }

        private void EnqueuePacket(float[] packet, CancellationToken cancellationToken = default)
        {
            while (_rawPayloadQueue.Count >= MaxQueuedPackets && !cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(PacketIntervalMilliseconds / 2);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _rawPayloadQueue.Enqueue(packet);
        }

        private byte[] GetNextPacket(ushort sequence, uint timestamp)
        {
            if (_rawPayloadQueue.TryDequeue(out float[] payload))
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

                return AssemblePacket(payload, sequence, timestamp);
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

        private byte[] AssemblePacket(float[] payload, ushort sequence, uint timestamp)
        {
            return new RtpPacketGenerator()
                .CreateHeader(sequence, timestamp, EndPointInfo.Ssrc)
                .AddPayload(Encoder.Encode(payload))
                .EncryptPayload(EndPointInfo.EncryptionMode, EndPointInfo.EncryptionKey)
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
