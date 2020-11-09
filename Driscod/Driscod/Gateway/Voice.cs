using Concentus.Enums;
using Concentus.Structs;
using MongoDB.Bson;
using Sodium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using WebSocket4Net;

namespace Driscod.Gateway
{
    public class Voice : Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private const string EncryptionMode = "xsalsa20_poly1305_suffix";

        private const int SampleRate = 48000;

        private const int Channels = 2;

        private const int PacketIntervalMilliseconds = 20;

        private static Random _random = new Random();

        private readonly string _serverId;

        private readonly string _userId;

        private readonly string _sessionId;

        private readonly string _token;

        private UdpClient _udpClient = null;

        private BsonDocument Identity => new BsonDocument
        {
            { "server_id", _serverId },
            { "user_id", _userId },
            { "session_id", _sessionId },
            { "token", _token },
        };

        private string UdpSocketIpAddress { get; set; }

        private ushort UdpSocketPort { get; set; }

        private UdpClient UdpClient
        {
            get
            {
                if (_udpClient == null)
                {
                    _udpClient = new UdpClient();
                    _udpClient.Connect(UdpSocketIpAddress, UdpSocketPort);

                    ulong value = 0;
                    var nextMessage = Environment.TickCount;

                    ManageThread(new Thread(() =>
                    {
                        while (KeepSocketOpen && Socket.State != WebSocketState.Closed)
                        {
                            if (Environment.TickCount < nextMessage)
                            {
                                Thread.Sleep(nextMessage - Environment.TickCount);
                            }
                            lock (UdpClient)
                            {
                                if (BitConverter.IsLittleEndian)
                                {
                                    UdpClient.Send(BitConverter.GetBytes(value).Reverse().ToArray(), 8);
                                }
                                else
                                {
                                    UdpClient.Send(BitConverter.GetBytes(value), 8);
                                }

                                var endpoint = GetUdpEndpoint();
                                UdpClient.Receive(ref endpoint);
                            }

                            Logger.Debug($"[{Name}] Sent keep-alive: {value}");

                            value++;
                            nextMessage = Environment.TickCount + 2000;
                        }
                    }));
                }
                return _udpClient;
            }
        }

        private IEnumerable<string> Modes { get; set; }

        private int Ssrc { get; set; }

        private byte[] SecretKey { get; set; }

        private int FrameSize => SampleRate / (1000 / PacketIntervalMilliseconds);

        private readonly Queue<byte[]> QueuedRtpPayloads = new Queue<byte[]>();

        protected override IEnumerable<int> RespectedCloseSocketCodes => new[] { 4006, 4014 }; // Should not reconnect upon forced disconnection.

        public override string Name => $"VOICE-{_sessionId}";

        public bool Ready { get; private set; } = false;

        public bool Speaking { get; private set; } = false;

        public Voice(string url, string serverId, string userId, string sessionId, string token)
            : base(url)
        {
            _serverId = serverId;
            _userId = userId;
            _sessionId = sessionId;
            _token = token;

            Socket.Opened += (a, b) =>
            {
                if (KeepSocketOpen)
                {
                    Send((int)MessageType.Resume, Identity);
                }
                else
                {
                    Send((int)MessageType.Identify, Identity);
                    Ready = false;
                }
            };

            AddListener<BsonDocument>((int)MessageType.Hello, data =>
            {
                HeartbeatIntervalMilliseconds = (int)data["heartbeat_interval"].AsDouble;
                StartHeart();
                KeepSocketOpen = true;
            });

            AddListener<BsonDocument>((int)MessageType.Ready, data =>
            {
                UdpSocketIpAddress = data["ip"].AsString;
                UdpSocketPort = (ushort)data["port"].AsInt32;
                Modes = data["modes"].AsBsonArray.Cast<string>();
                Ssrc = data["ssrc"].AsInt32;

                byte[] ssrcBytes;

                if (BitConverter.IsLittleEndian)
                {
                    ssrcBytes = BitConverter.GetBytes(Ssrc).Reverse().ToArray();
                }
                else
                {
                    ssrcBytes = BitConverter.GetBytes(Ssrc);
                }

                var datagram = new byte[] { 0, 1, 0, 70 }.Concat(ssrcBytes).Concat(Enumerable.Repeat((byte)0, 66)).ToArray();
                byte[] response;

                lock (UdpClient)
                {
                    UdpClient.Send(datagram, datagram.Length);
                    var endpoint = GetUdpEndpoint();
                    response = UdpClient.Receive(ref endpoint);
                }

                var port = BitConverter.ToUInt16(response.Reverse().Take(2).ToArray(), 0);
                var address = Encoding.UTF8.GetString(response.Skip(8).TakeWhile(x => x != 0).ToArray());

                Send((int)MessageType.SelectProtocol, new BsonDocument
                {
                    { "protocol", "udp" },
                    { "data",
                        new BsonDocument
                        {
                            { "address", address },
                            { "port", port },
                            { "mode", EncryptionMode },
                        }
                    },
                });
            });

            AddListener<BsonDocument>((int)MessageType.SessionDescription, data =>
            {
                SecretKey = data["secret_key"].AsBsonArray.Select(x => (byte)x.AsInt32).ToArray();
                Ready = true;

                ManageThread(new Thread(AudioLoop));
            });
        }

        private void BeginSpeaking()
        {
            Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", Ssrc },
                { "delay", 0 },
                { "speaking", 1 },
            });
            Speaking = true;
            Logger.Info($"[{Name}] Speaking.");
        }

        private void EndSpeaking()
        {
            Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", Ssrc },
                { "delay", 0 },
                { "speaking", 0 },
            });
            Speaking = false;
            Logger.Info($"[{Name}] Stopped speaking.");
        }

        private void AudioLoop()
        {
            try
            {
                var nextFrame = Environment.TickCount;
                uint timestamp = (uint)(uint.MaxValue * _random.NextDouble());
                ushort sequence = (ushort)(ushort.MaxValue * _random.NextDouble());

                while (!CancellationToken.IsCancellationRequested)
                {
                    List<byte> packet = null;

                    lock (QueuedRtpPayloads)
                    {
                        if (QueuedRtpPayloads.Any())
                        {
                            if (!Speaking)
                            {
                                BeginSpeaking();
                            }

                            packet = new List<byte>() { 0x80, 0x78 };

                            if (BitConverter.IsLittleEndian)
                            {
                                packet.AddRange(BitConverter.GetBytes(sequence).Reverse());
                                packet.AddRange(BitConverter.GetBytes(timestamp).Reverse());
                                packet.AddRange(BitConverter.GetBytes((uint)Ssrc).Reverse());
                            }
                            else
                            {
                                packet.AddRange(BitConverter.GetBytes(sequence));
                                packet.AddRange(BitConverter.GetBytes(timestamp));
                                packet.AddRange(BitConverter.GetBytes((uint)Ssrc));
                            }

                            packet.AddRange(QueuedRtpPayloads.Dequeue());
                        }
                        else
                        {
                            if (Speaking)
                            {
                                EndSpeaking();
                            }
                        }
                    }

                    if (Environment.TickCount < nextFrame)
                    {
                        Thread.Sleep(nextFrame - Environment.TickCount);
                    }
                    if (packet != null)
                    {
                        UdpClient.Send(packet.ToArray(), packet.Count);
                    }
                    nextFrame = Environment.TickCount + PacketIntervalMilliseconds;

                    sequence++;
                    timestamp += (uint)FrameSize;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Exception in audio loop: {ex}");
            }
        }

        private void QueueSilence()
        {
            SendAudio(Enumerable.Repeat((short)0, FrameSize * 10).ToArray(), padSilence: false);
        }

        private IPEndPoint GetUdpEndpoint()
        {
            return new IPEndPoint(new IPAddress(UdpSocketIpAddress.Split(new[] { '.' }).Select(x => Convert.ToByte(x)).ToArray()), UdpSocketPort);
        }

        protected override void Heartbeat()
        {
            var nonce = _random.Next(int.MinValue, int.MaxValue);

            var response = WaitForEvent<int>(
                (int)MessageType.HeartbeatAck,
                listenerCreateCallback: () =>
                {
                    Send((int)MessageType.Heartbeat, nonce);
                },
                timeout: TimeSpan.FromSeconds(10));

            if (response != nonce)
            {
                throw new InvalidOperationException("Heartbeat failed, recieved incorrect nonce.");
            }
        }

        public void SendAudio(short[] samples, bool padSilence = true)
        {
            OpusEncoder encoder = OpusEncoder.Create(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);

            for (var i = 0; i < samples.Length; i += FrameSize * 2)
            {
                var inputBuffer = samples.Skip(i).Take(FrameSize * 2).ToArray();

                var opusPacket = new byte[1000];
                int opusPacketSize = encoder.Encode(inputBuffer, 0, FrameSize, opusPacket, 0, opusPacket.Length);
                opusPacket = opusPacket.Take(opusPacketSize).ToArray();

                var nonce = Enumerable.Range(1, 24).Select(x => (byte)_random.Next(byte.MinValue, byte.MaxValue)).ToArray();

                lock (QueuedRtpPayloads)
                {
                    QueuedRtpPayloads.Enqueue(StreamEncryption.Encrypt(opusPacket, nonce, SecretKey).Concat(nonce).ToArray());
                }
            }

            if (padSilence)
            {
                QueueSilence();
            }
        }

        public void SendAudio(IEnumerable<float> samples, bool padSilence = true)
        {
            SendAudio(samples.Select(x => (short)(x * short.MaxValue)).ToArray());
        }

        public enum MessageType
        {
            Any = -1,
            Identify = 0,
            SelectProtocol = 1,
            Ready = 2,
            Heartbeat = 3,
            SessionDescription = 4,
            Speaking = 5,
            HeartbeatAck = 6,
            Resume = 7,
            Hello = 8,
            Resumed = 9,
            ClientDisconnect = 13,
        }
    }
}
