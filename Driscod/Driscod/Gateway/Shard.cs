using Driscod.Gateway.Consts;
using Driscod.Network;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Driscod.Gateway
{
    public class Shard : Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string _token;

        private readonly int _shardNumber;

        private readonly int _totalShards;

        private readonly int _intents;

        private JObject Identity => new JObject
        {
            { "token", _token },
            { "shard", JToken.FromObject(new[] { _shardNumber, _totalShards })},
            {
                "properties", new JObject
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

        private string SessionId { get; set; }

        protected override IEnumerable<int> RespectedCloseSocketCodes => new[] { 4010, 4011, 4012, 4013, 4014 };

        public bool Ready { get; private set; }

        public string BotName { get; set; } = "Unnamed";

        public override string Name => $"{BotName}-{_shardNumber}";

        public Shard(string token, int shardNumber, int totalShards, int intents)
            : base(Connectivity.WebSocketEndpoint)
        {
            _token = token;
            _shardNumber = shardNumber;
            _totalShards = totalShards;
            _intents = intents;

            AddListener<JObject>((int)MessageType.Hello, async data =>
            {
                HeartbeatIntervalMilliseconds = data["heartbeat_interval"].ToObject<int>();

                if (KeepSocketOpen)
                {
                    await Send((int)MessageType.Resume, new JObject
                    {
                        { "token", _token },
                        { "session_id", SessionId },
                        { "seq", Sequence },
                    });
                }
                else
                {
                    await Send((int)MessageType.Identify, Identity);
                }

                KeepSocketOpen = true;
                StartHeart();
            });

            AddListener<JObject>((int)MessageType.Dispatch, EventNames.Ready, data =>
            {
                if (DetailedLogging)
                {
                    Logger.Info($"[{Name}] Ready.");
                }
                Ready = true;
                SessionId = data["session_id"].ToObject<string>();
            });

            AddListener<object>((int)MessageType.InvalidSession, async _ =>
            {
                Logger.Warn($"[{Name}] Invalid session.");
                await Restart();
            });
        }

        protected override async Task Heartbeat()
        {
            await ListenForEvent<JObject>(
                (int)MessageType.HeartbeatAck,
                listenerCreateCallback: async () =>
                {
                    await Send((int)MessageType.Heartbeat, Sequence);
                },
                timeout: TimeSpan.FromSeconds(10));
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
    }
}
