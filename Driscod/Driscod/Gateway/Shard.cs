using MongoDB.Bson;
using System;

namespace Driscod.Gateway
{
    public class Shard : Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string _token;

        private readonly int _shardNumber;

        private readonly int _totalShards;

        private readonly int _intents;

        private string SessionId { get; set; }

        public bool Ready { get; private set; }

        private BsonDocument Identity => new BsonDocument
        {
            { "token", _token },
            { "shard", new BsonArray { _shardNumber, _totalShards } },
            {
                "properties", new BsonDocument
                {
                    { "$os", Environment.OSVersion.VersionString },
                    { "$browser", "c#" },
                    { "$device", "c#" },
                    { "$referrer", "" },
                    { "$referring_domain", "" },
                }
            },
            { "intents", _intents },
        };

        public override string Name => $"SHARD-{_shardNumber}";

        public Shard(string token, int shardNumber, int totalShards, int intents = 32767)
            : base(Connectivity.WebSocketEndpoint)
        {
            _token = token;
            _shardNumber = shardNumber;
            _totalShards = totalShards;
            _intents = intents;

            AddListener((int)MessageType.Hello, data =>
            {
                HeartbeatIntervalMilliseconds = data["heartbeat_interval"].AsInt32;

                if (KeepSocketOpen)
                {
                    Send((int)MessageType.Resume, new BsonDocument
                    {
                        { "token", _token },
                        { "session_id", SessionId },
                        { "seq", Sequence },
                    });
                }
                else
                {
                    Send((int)MessageType.Identify, Identity);
                    KeepSocketOpen = true;
                    StartHeart();
                }
            });

            AddListener((int)MessageType.Dispatch, "READY", data =>
            {
                Logger.Info($"[{Name}] Ready.");
                Ready = true;
                SessionId = data["session_id"].AsString;
            });

            AddListener((int)MessageType.HeartbeatAck, data =>
            {
                AcknowledgeHeartbeat();
            });

            AddListener((int)MessageType.InvalidSession, data =>
            {
                Logger.Warn($"[{Name}] Invalid session.");
                Socket.Close();
            });
        }        

        public enum MessageType
        {
            Any = -1,
            Dispatch = 0,
            Heartbeat = 1,
            Identify = 2,
            StatusUpdate = 3,
            VoiceStateUpdate = 4,
            Resume = 6,
            Reconnect = 7,
            RequestGuildMembers = 8,
            InvalidSession = 9,
            Hello = 10,
            HeartbeatAck = 11,
        }

        protected override void Heartbeat()
        {
            Send((int)MessageType.Heartbeat, Sequence);
        }
    }
}
