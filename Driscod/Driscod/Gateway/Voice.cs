using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Driscod.Gateway
{
    public class Voice : Gateway
    {
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
                Send((int)MessageType.Indentify, Identity);
            };

            AddListener((int)MessageType.Ready, data =>
            {
                // TODO
            });
        }

        protected override void Heartbeat()
        {
            throw new NotImplementedException();
        }

        public enum MessageType
        {
            Any = -1,
            Indentify = 0,
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
