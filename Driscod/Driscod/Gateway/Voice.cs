using Driscod.Audio;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Driscod.Gateway
{
    public class Voice : Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static Random _random = new Random();

        private readonly string _serverId;

        private readonly string _userId;

        private readonly string _sessionId;

        private readonly string _token;

        private BsonDocument Identity => new BsonDocument
        {
            { "server_id", _serverId },
            { "user_id", _userId },
            { "session_id", _sessionId },
            { "token", _token },
        };

        private string UdpSocketIpAddress { get; set; }

        private ushort UdpSocketPort { get; set; }

        private IEnumerable<string> Modes { get; set; }

        private string EncryptionMode => "xsalsa20_poly1305";

        private uint Ssrc { get; set; }

        private byte[] SecretKey { get; set; }

        private int LocalPort { get; set; }

        private AudioStreamer AudioStreamer { get; set; }

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
                Ssrc = (uint)data["ssrc"].AsInt32;

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

                using (var udpClient = new UdpClient())
                {
                    var endpoint = GetUdpEndpoint();
                    udpClient.Send(datagram, datagram.Length, endpoint);
                    response = udpClient.Receive(ref endpoint);
                }

                LocalPort = BitConverter.ToUInt16(response.Reverse().Take(2).ToArray(), 0);
                var address = Encoding.UTF8.GetString(response.Skip(8).TakeWhile(x => x != 0).ToArray());

                Send((int)MessageType.SelectProtocol, new BsonDocument
                {
                    { "protocol", "udp" },
                    { "data",
                        new BsonDocument
                        {
                            { "address", address },
                            { "port", LocalPort },
                            { "mode", EncryptionMode },
                        }
                    },
                });
            });

            AddListener<BsonDocument>((int)MessageType.SessionDescription, data =>
            {
                SecretKey = data["secret_key"].AsBsonArray.Select(x => (byte)x.AsInt32).ToArray();
                Ready = true;

                AudioStreamer = new AudioStreamer(CancellationToken.Token)
                {
                    SocketEndPoint = GetUdpEndpoint(),
                    LocalPort = LocalPort,
                    Ssrc = Ssrc,
                    EncryptionKey = SecretKey,
                    AudioStartCallback = BeginSpeaking,
                    AudioStopCallback = EndSpeaking,
                };

                Thread.Sleep(5000);

                //AudioStreamer.SendAudio(new WaveAudioFile(@"E:\assorted\music\games\TLoZ\TLoZ_MM_Clock_Town_Day_3.wav"));
                AudioStreamer.SendAudio(new WaveAudioFile(@"C:\Users\pc\Desktop\audio-2012-2-28-483_192746_1\talk.wav"));
            });
        }

        private void BeginSpeaking()
        {
            Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", (int)Ssrc }, // TODO: Make this not dodgy.
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
                { "ssrc", (int)Ssrc },
                { "delay", 0 },
                { "speaking", 0 },
            });
            Speaking = false;
            Logger.Info($"[{Name}] Stopped speaking.");
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
