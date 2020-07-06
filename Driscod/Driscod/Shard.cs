using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using WebSocket4Net;

namespace Driscod
{
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

    public class Shard
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string _token;

        private readonly int _shardNumber;

        private readonly int _totalShards;

        private readonly WebSocket _socket;

        private int _heartbeatInterval = -1;

        private Thread _heartThread = null;

#pragma warning disable S1450
        private bool _heartbeatAcknowledged = false;
#pragma warning restore S1450

        public bool Ready { get; private set; }

        private string SessionId { get; set; }

        private int Sequence { get; set; }

        private bool ShouldResume { get; set; }

        private List<DateTime> LimitTracker { get; set; } = new List<DateTime>();

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
        };

        public string Name => $"SHARD-{_shardNumber}";

        public Shard(string token, int shardNumber, int totalShards)
        {
            _token = token;
            _shardNumber = shardNumber;
            _totalShards = totalShards;

            _socket = new WebSocket(Connectivity.GetWebSocketEndpoint());

            _socket.Closed += (a, b) =>
            {
                Logger.Warn($"[{Name}] Socket closed.");
                if (ShouldResume)
                {
                    _socket.Open();
                }
            };

            AddListener(MessageType.Any, data =>
            {
                Logger.Debug($"[{Name}] <- {data?.ToString() ?? "(no data)"}");
            });

            AddListener(MessageType.Hello, data =>
            {
                _heartbeatInterval = data["heartbeat_interval"].AsInt32;

                if (ShouldResume)
                {
                    Send(MessageType.Resume, new BsonDocument
                    {
                        { "token", _token },
                        { "session_id", SessionId },
                        { "seq", Sequence },
                    });
                }
                else
                {
                    Send(MessageType.Identify, Identity);
                    ShouldResume = true;
                }
                _heartThread?.Abort();
                _heartThread = new Thread(Heart)
                {
                    Name = $"{Name}-HEART",
                    IsBackground = true,
                };
                _heartThread.Start();
            });

            AddListener(MessageType.Dispatch, "READY", data =>
            {
                Ready = true;
                SessionId = data["session_id"].AsString;
            });

            AddListener(MessageType.HeartbeatAck, data =>
            {
                _heartbeatAcknowledged = true;
            });

            AddListener(MessageType.InvalidSession, data =>
            {
                Logger.Warn($"[{Name}] Invalid session.");
                _socket.Close();
            });
        }

        public void Start()
        {
            Ready = false;
            Logger.Info($"[{Name}] Starting...");
            while (_heartThread != null && _heartThread.IsAlive)
            {
                // intentionally empty
            }
            _socket.Open();
        }

        public void Stop()
        {
            Logger.Info($"[{Name}] Stopping...");
            ShouldResume = false;
            _socket.Close();
            while (_socket.State != WebSocketState.Closed)
            {
                // intentionally empty
            }
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        public void Heart()
        {
            Logger.Info($"[{Name}] Heart started.");

            var stopwatch = new Stopwatch();

            Thread.Sleep(_heartbeatInterval);
            while (_socket.State == WebSocketState.Open)
            {
                _heartbeatAcknowledged = false;
                Send(MessageType.Heartbeat, Sequence);

                stopwatch.Restart();
                while (!_heartbeatAcknowledged && stopwatch.Elapsed.Seconds < 10)
                {
                    // intentionally empty
                }
                if (!_heartbeatAcknowledged)
                {
                    Logger.Warn($"[{Name}] Nothing from the venous system.");
                    break;
                }
                stopwatch.Stop();

                Thread.Sleep(_heartbeatInterval);
            }
            Logger.Warn($"[{Name}] Heart stopped.");
        }

        public void RateLimitWait(Action callback)
        {
            do
            {
                LimitTracker.RemoveAll(x => (DateTime.UtcNow - x).TotalSeconds > 60);
            }
            while (LimitTracker.Count >= Connectivity.GatewayEventsPerMinute);

            LimitTracker.Add(DateTime.UtcNow);
            callback();
        }

        public void Send(MessageType type, BsonValue data = null)
        {
            var response = new BsonDocument
            {
                { "op", (int)type },
            };
            if (data != null)
            {
                response["d"] = data;
            }
            RateLimitWait(() =>
            {
                Logger.Debug($"[{Name}] -> {response.ToString()}");
                _socket.Send(response.ToString());
            });
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(MessageType type, Action<BsonDocument> handler)
        {
            return AddListener(type, new string[0], handler);
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(MessageType type, string eventName, Action<BsonDocument> handler)
        {
            return AddListener(type, new[] { eventName }, handler);
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(MessageType type, string[] eventNames, Action<BsonDocument> handler)
        {
            var listener = new EventHandler<MessageReceivedEventArgs>((sender, message) =>
            {
                var doc = BsonDocument.Parse(message.Message);

                if (doc.Contains("s") && !doc["s"].IsBsonNull)
                {
                    Sequence = doc["s"].AsInt32;
                }
                if ((type == MessageType.Any || doc["op"] == (int)type) && (!eventNames.Any() || eventNames.Contains(doc["t"].AsString)))
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            handler(!doc["d"].IsBsonDocument ? null : doc["d"].AsBsonDocument);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"[{Name}] Handler for {type}{(eventNames.Length > 0 ? $" ({string.Join(", ", eventNames)})" : "")} failed: {e.Message}");
                        }
                    });
                    thread.IsBackground = true;
                    thread.Start();
                }
            });
            _socket.MessageReceived += listener;
            return listener;
        }

        public void RemoveListener(EventHandler<MessageReceivedEventArgs> handler)
        {
            _socket.MessageReceived -= handler;
        }
    }
}
