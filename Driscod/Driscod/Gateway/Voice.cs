using Concentus.Enums;
using Concentus.Structs;
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
                }
                return _udpClient;
            }
        }

        private IEnumerable<string> Modes { get; set; }

        private string Mode => "xsalsa20_poly1305_suffix";

        private uint Ssrc { get; set; }

        public override string Name => $"VOICE-{_sessionId}";

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
                }
            };

            AddListener((int)MessageType.Ready, data =>
            {
                UdpSocketIpAddress = data["ip"].AsString;
                UdpSocketPort = (ushort)data["port"].AsInt32;
                Modes = data["modes"].AsBsonArray.Cast<string>();
                Ssrc = (uint)data["ssrc"].AsInt32;

                var datagram = new byte[] { 0, 1, 0, 70 }.Concat(BitConverter.GetBytes(Ssrc)).Concat(Enumerable.Repeat((byte)0, 66)).ToArray();
                UdpClient.Send(datagram, datagram.Length);

                var endpoint = GetUdpEndpoint();
                var response = UdpClient.Receive(ref endpoint);

                var port = BitConverter.ToUInt16(response, 72);

                var bytes = new List<byte>();
                foreach (var addrByte in response.Skip(8))
                {
                    if (addrByte == 0)
                    {
                        break;
                    }
                    bytes.Add(addrByte);
                }
                var address = Encoding.UTF8.GetString(bytes.ToArray(), 0, bytes.Count);

                Send((int)MessageType.SelectProtocol, new BsonDocument
                {
                    { "protocol", "udp" },
                    { "data",
                        new BsonDocument
                        {
                            { "address", address },
                            { "port", port },
                            { "mode", Mode },
                        }
                    },
                });
            });

            AddListener((int)MessageType.Hello, data =>
            {
                HeartbeatIntervalMilliseconds = (int)data["heartbeat_interval"].AsDouble;
                StartHeart();
            });

            AddListener((int)MessageType.HeartbeatAck, _ =>
            {
                NotifyAcknowledgedHeartbeat();
            });
        }

        protected override void Heartbeat()
        {
            Send((int)MessageType.Heartbeat, _random.Next(int.MinValue, int.MaxValue));
        }

        private void IndicateSpeaking(Action callback)
        {
            Send((int)MessageType.Speaking, new BsonDocument
            {
                { "ssrc", Ssrc },
                { "delay", 0 },
                { "speaking", 1 },
            });

            try
            {
                callback();
            }
            finally
            {
                Send((int)MessageType.Speaking, new BsonDocument
                {
                    { "ssrc", Ssrc },
                    { "delay", 0 },
                    { "speaking", 0 },
                });
            }
        }

        //private void SendAudio(short[] samples)
        //{
        //    OpusEncoder encoder = OpusEncoder.Create(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);

        //    ushort sequence = 0;
        //    int frameSize = 960;

        //    for (var i = 0; i < samples.Length; i += frameSize)
        //    {
        //        var inputBuffer = samples.Skip(i).Take(frameSize).ToArray();
        //        var outputBuffer = new byte[1000];
        //        int packetSize = encoder.Encode(inputBuffer, 0, frameSize, outputBuffer, 0, outputBuffer.Length);

        //        var timestamp = DateTime.UtcNow.

        //        sequence++;
        //    }
        //}

        private IPEndPoint GetUdpEndpoint()
        {
            return new IPEndPoint(new IPAddress(UdpSocketIpAddress.Split(new[] { '.' }).Select(x => Convert.ToByte(x)).ToArray()), UdpSocketPort);
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
