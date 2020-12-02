using Driscod.Audio;
using Driscod.Extensions;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Gateway
{
    internal class Voice : Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static Random _random = new Random();

        private readonly string _serverId;

        private readonly string _userId;

        private readonly string _token;

        private BsonDocument Identity => new BsonDocument
        {
            { "server_id", _serverId },
            { "user_id", _userId },
            { "session_id", SessionId },
            { "token", _token },
        };

        private string UdpSocketIpAddress { get; set; }

        private ushort UdpSocketPort { get; set; }

        private IEnumerable<string> EndpointEncryptionModes { get; set; }

        private IEnumerable<string> AllowedEncryptionModes => EndpointEncryptionModes.Intersect(RtpPacketGenerator.SupportedEncryptionModes);

        private string EncryptionMode { get; set; }

        private uint Ssrc { get; set; }

        private byte[] SecretKey { get; set; }

        private int LocalPort { get; set; }

        private string ExternalAddress { get; set; }

        private Shard ParentShard { get; set; }

        protected override IEnumerable<int> RespectedCloseSocketCodes => new[] { 4006, 4014 }; // Should not reconnect upon forced disconnection.

        public override string Name => $"VOICE-{string.Join('-', SessionId.Reverse().Take(2))}";

        public bool Ready { get; private set; } = false;

        public bool Speaking { get; private set; } = false;

        public string SessionId { get; set; }

        public event EventHandler OnStop;

        public Voice(Shard parentShard, string url, string serverId, string userId, string sessionId, string token)
            : base(url)
        {
            _serverId = serverId;
            _userId = userId;
            _token = token;

            ParentShard = parentShard;
            SessionId = sessionId;

            Socket.Opened += async (a, b) =>
            {
                if (KeepSocketOpen)
                {
                    await Send((int)MessageType.Resume, Identity);
                }
                else
                {
                    await Send((int)MessageType.Identify, Identity);
                    Ready = false;
                }
            };

            Socket.Closed += (a, b) =>
            {
                OnStop.Invoke(this, EventArgs.Empty);
            };

            AddListener<BsonDocument>((int)MessageType.Hello, data =>
            {
                HeartbeatIntervalMilliseconds = (int)data["heartbeat_interval"].AsDouble;
                StartHeart();
                KeepSocketOpen = true;
            });

            AddListener<BsonDocument>((int)MessageType.Ready, async data =>
            {
                UdpSocketIpAddress = data["ip"].AsString;
                UdpSocketPort = (ushort)data["port"].AsInt32;
                EndpointEncryptionModes = data["modes"].AsBsonArray.Select(x => x.AsString);
                Ssrc = (uint)data["ssrc"].AsInt32;

                if (!AllowedEncryptionModes.Any())
                {
                    Logger.Fatal($"[{Name}] Found no allowed encryption modes.");
                    await Stop();
                    return;
                }

                EncryptionMode = AllowedEncryptionModes.First();

                await FetchExternalAddress();

                await Send((int)MessageType.SelectProtocol, new BsonDocument
                {
                    { "protocol", "udp" },
                    { "data",
                        new BsonDocument
                        {
                            { "address", ExternalAddress },
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
            });
        }

        public override async Task Stop()
        {
            try
            {
                await ParentShard.ListenForEvent<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "VOICE_STATE_UPDATE",
                    listenerCreateCallback: async () =>
                    {
                        await ParentShard.Send((int)Shard.MessageType.VoiceStateUpdate, new BsonDocument
                        {
                        { "guild_id", _serverId },
                        { "channel_id", BsonNull.Value },
                        { "self_mute", false },
                        { "self_deaf", false },
                        });
                    },
                    validator: data =>
                    {
                        return data["guild_id"].AsString == _serverId;
                    });
            }
            finally
            {
                // Discord should close the socket after the voice state update.
                await Task.WhenAny(
                    Task.Run(() =>
                    {
                        while (Socket.State != WebSocket4Net.WebSocketState.Closed)
                        {
                            Thread.Sleep(200);
                        }
                    }),
                    Task.Delay(TimeSpan.FromSeconds(10)));

                if (Socket.State != WebSocket4Net.WebSocketState.Closed)
                {
                    Logger.Warn($"[{Name}] Expected Discord to close the socket, but it did not.");
                }

                await base.Stop();
            }
        }

        public AudioStreamer CreateAudioStreamer()
        {
            if (!Ready)
            {
                throw new InvalidOperationException("Voice socket is not ready to create audio streamer.");
            }

            var streamer = new AudioStreamer(CancellationToken.Token)
            {
                SocketEndPoint = GetUdpEndpoint(),
                LocalPort = LocalPort,
                Ssrc = Ssrc,
                EncryptionKey = SecretKey,
                EncryptionMode = EncryptionMode,
            };

            streamer.OnAudioStart += async (a, b) =>
            {
                await BeginSpeaking();
            };

            streamer.OnAudioStop += async (a, b) =>
            {
                await EndSpeaking();
            };

            return streamer;
        }

        protected override async Task Heartbeat()
        {
            var nonce = _random.Next(int.MinValue, int.MaxValue);

            var response = await ListenForEvent<int>(
                (int)MessageType.HeartbeatAck,
                listenerCreateCallback: async () =>
                {
                    await Send((int)MessageType.Heartbeat, nonce);
                },
                timeout: TimeSpan.FromSeconds(10));

            if (response != nonce)
            {
                throw new InvalidOperationException("Heartbeat failed, recieved incorrect nonce.");
            }
        }

        private async Task BeginSpeaking()
        {
            await Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", (int)Ssrc }, // TODO: Make this not dodgy.
                { "delay", 0 },
                { "speaking", 1 },
            });
            Speaking = true;
        }

        private async Task EndSpeaking()
        {
            await Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", (int)Ssrc },
                { "delay", 0 },
                { "speaking", 0 },
            });
            Speaking = false;
        }

        private IPEndPoint GetUdpEndpoint()
        {
            return new IPEndPoint(IPAddress.Parse(UdpSocketIpAddress), UdpSocketPort);
        }

        private async Task FetchExternalAddress()
        {
            var datagram = new byte[] { 0, 1, 0, 70 }
                .Concat(Ssrc.ToBytesBigEndian())
                .Concat(Enumerable.Repeat((byte)0, 66))
                .ToArray();

            byte[] response;

            using (var udpClient = new UdpClient())
            {
                var endpoint = GetUdpEndpoint();
                await udpClient.SendAsync(datagram, datagram.Length, endpoint);
                response = udpClient.Receive(ref endpoint);
            }

            LocalPort = (response[response.Length - 2] << 8) + response[response.Length - 1];
            ExternalAddress = Encoding.UTF8.GetString(response.Skip(8).TakeWhile(x => x != 0).ToArray());
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
