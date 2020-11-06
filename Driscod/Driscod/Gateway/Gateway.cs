using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using WebSocket4Net;

namespace Driscod.Gateway
{
    public abstract class Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static bool DetailedLogging { get; set; } = false;

        private bool HeartbeatAcknowledged { get; set; }

        protected Thread _heartThread = null;

        private List<DateTime> LimitTracker { get; set; } = new List<DateTime>();

        protected int Sequence { get; set; }

        protected WebSocket Socket { get; private set; }

        protected int HeartbeatIntervalMilliseconds { get; set; }

        protected bool KeepSocketOpen { get; set; }

        protected virtual IEnumerable<int> RespectedCloseSocketCodes => new int[0];

        protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(1);

        public abstract string Name { get; }

        protected Gateway(string url)
        {
            Socket = new WebSocket(url);

            Socket.Opened += (a, b) =>
            {
                Logger.Info($"[{Name}] Socket opened.");
            };

            Socket.Closed += (_, e) =>
            {
                var evnt = e as ClosedEventArgs;
                Logger.Info($"[{Name}] Socket closed. Code: {evnt?.Code}, Reason: {evnt?.Reason}");
                StopHeart();
                if (KeepSocketOpen)
                {
                    if (evnt != null && RespectedCloseSocketCodes.Contains(evnt.Code))
                    {
                        Logger.Debug($"[{Name}] Socket is marked to be kept open, but encountered respected close code '{evnt.Code}'.");
                    }
                    else
                    {
                        try
                        {
                            Thread.Sleep(ReconnectDelay);
                            Socket.Open();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"[{Name}] Socket opened during close event handling.");
                        }
                    }
                }
            };

            Socket.MessageReceived += new EventHandler<MessageReceivedEventArgs>((sender, message) =>
            {
                if (DetailedLogging)
                {
                    Logger.Debug($"[{Name}] <- {message.Message}");
                }
            });
        }

        protected void StartHeart()
        {
            StopHeart();
            _heartThread = new Thread(Heart)
            {
                Name = $"{Name} Heart",
                IsBackground = true,
            };
            Logger.Debug($"[{Name}] Starting heart...");
            _heartThread.Start();
        }

        protected void StopHeart()
        {
            if (_heartThread == null || !_heartThread.IsAlive)
            {
                return;
            }
            Logger.Debug($"[{Name}] Stopping heart...");
            _heartThread.Abort();
            while (_heartThread.IsAlive)
            {
                // intentionally empty
            }
        }

        protected void Heart()
        {
            Logger.Debug($"[{Name}] Heart started.");

            var stopwatch = new Stopwatch();

            Thread.Sleep(HeartbeatIntervalMilliseconds);
            while (Socket.State == WebSocketState.Open)
            {
                HeartbeatAcknowledged = false;
                Heartbeat();

                stopwatch.Restart();
                while (!HeartbeatAcknowledged && stopwatch.Elapsed.Seconds < 10)
                {
                    // intentionally empty
                }
                if (!HeartbeatAcknowledged)
                {
                    Logger.Warn($"[{Name}] Nothing from the venous system.");
                    break;
                }
                stopwatch.Stop();

                Thread.Sleep(HeartbeatIntervalMilliseconds);
            }
            Logger.Error($"[{Name}] Heart stopped.");
        }

        protected void NotifyAcknowledgedHeartbeat()
        {
            HeartbeatAcknowledged = true;
        }

        public virtual void Start()
        {
            Socket.Open();
        }

        public virtual void Stop()
        {
            KeepSocketOpen = false;
            Socket.Close();
            while (Socket.State != WebSocketState.Closed)
            {
                // intentionally empty
            }
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        protected abstract void Heartbeat();

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

        public void Send(int type, BsonValue data = null)
        {
            var response = new BsonDocument
            {
                { "op", type },
            };
            if (data != null)
            {
                response["d"] = data;
            }
            RateLimitWait(() =>
            {
                if (DetailedLogging)
                {
                    Logger.Debug($"[{Name}] -> {response.ToString()}");
                }
                Socket.Send(response.ToString());
            });
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(int type, Action<BsonDocument> handler)
        {
            return AddListener(type, new string[0], handler);
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(int type, string eventName, Action<BsonDocument> handler)
        {
            return AddListener(type, new[] { eventName }, handler);
        }

        public EventHandler<MessageReceivedEventArgs> AddListener(int type, string[] eventNames, Action<BsonDocument> handler)
        {
            var handlerName = $"{type}{(eventNames.Length > 0 ? $" ({string.Join(", ", eventNames)})" : string.Empty)}";

            var listener = new EventHandler<MessageReceivedEventArgs>((sender, message) =>
            {
                var doc = BsonDocument.Parse(message.Message);

                if (doc.Contains("s") && !doc["s"].IsBsonNull)
                {
                    Sequence = doc["s"].AsInt32;
                }
                if ((type == -1 || doc["op"] == type) && (!eventNames.Any() || eventNames.Contains(doc["t"].AsString)))
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            handler(!doc["d"].IsBsonDocument ? null : doc["d"].AsBsonDocument);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"[{Name}] Handler for {handlerName} failed: {e}");
                        }
                    })
                    {
                        IsBackground = true,
                        Name = $"{Name} {handlerName} Handler",
                    };
                    thread.Start();
                }
            });
            Socket.MessageReceived += listener;
            return listener;
        }

        public void RemoveListener(EventHandler<MessageReceivedEventArgs> handler)
        {
            Socket.MessageReceived -= handler;
        }
    }
}
