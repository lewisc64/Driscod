using MongoDB.Bson;
using System;

namespace Driscod.Gateway
{
    public class Voice : Gateway
    {
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

        public override string Name => "VOICE";

        public Voice(string url, string serverId, string userId, string sessionId, string token)
            : base(url)
        {
            _serverId = serverId;
            _userId = userId;
            _sessionId = sessionId;
            _token = token;

            Socket.Opened += (a, b) =>
            {
                Send((int)MessageType.Identify, Identity);
            };

            AddListener((int)MessageType.Ready, data =>
            {
                // TODO
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
