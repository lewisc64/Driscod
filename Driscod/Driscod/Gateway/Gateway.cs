using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Driscod.Gateway
{
    public abstract class Gateway
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static bool DetailedLogging { get; set; } = false;

        protected Thread _heartThread = null;

        private List<DateTime> LimitTracker { get; set; } = new List<DateTime>();

        private List<Thread> Threads { get; set; } = new List<Thread>();

        private List<Action<BsonDocument>> Listeners { get; set; } = new List<Action<BsonDocument>>();

        protected int Sequence { get; set; }

        protected WebSocket Socket { get; private set; }

        protected bool KeepSocketOpen { get; set; }

        protected virtual IEnumerable<int> RespectedCloseSocketCodes => new int[0];

        protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(1);

        protected CancellationTokenSource CancellationToken { get; set; }

        protected int HeartbeatIntervalMilliseconds { get; set; }

        public abstract string Name { get; }

        public bool Running => Socket.State == WebSocketState.Open;

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

                if (KeepSocketOpen)
                {
                    if (evnt != null && RespectedCloseSocketCodes.Contains(evnt.Code))
                    {
                        Logger.Debug($"[{Name}] Socket is marked to be kept open, but encountered respected close code '{evnt.Code}'.");
                        PurgeThreads();
                    }
                    else
                    {
                        PurgeThreads();
                        Thread.Sleep(ReconnectDelay);
                        Start();
                    }
                }
                else
                {
                    PurgeThreads();
                }
            };

            Socket.MessageReceived += new EventHandler<MessageReceivedEventArgs>((sender, message) =>
            {
                if (DetailedLogging)
                {
                    Logger.Debug($"[{Name}] <- {message.Message}");
                }

                var doc = BsonDocument.Parse(message.Message);
                
                lock (Listeners)
                {
                    foreach (var listener in Listeners)
                    {
                        try
                        {
                            listener.Invoke(doc);
                        }
                        catch
                        {
                            // intentionally empty
                        }
                    }
                }
            });
        }

        public virtual void Start()
        {
            CancellationToken = new CancellationTokenSource();
            CancellationToken.Token.Register(() =>
            {
                Logger.Debug($"[{Name}] Cancellation requested, threads should stop.");
            });

            try
            {
                Socket.Open();
            }
            catch (Exception)
            {
                Logger.Warn($"[{Name}] Attempted to open socket, but socket was not closed.");
            }
        }

        public virtual void Stop()
        {
            KeepSocketOpen = false;
            Socket.Close("Internal stop call.");
            while (Socket.State != WebSocketState.Closed || Threads.Any())
            {
                // intentionally empty
            }
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        public T WaitForEvent<T>(int type, Action listenerCreateCallback = null, Func<T, bool> validator = null, TimeSpan? timeout = null)
        {
            return WaitForEvent(type, new string[0], listenerCreateCallback: listenerCreateCallback, validator: validator, timeout: timeout);
        }

        public T WaitForEvent<T>(int type, string eventName, Action listenerCreateCallback = null, Func<T, bool> validator = null, TimeSpan? timeout = null)
        {
            return WaitForEvent(type, new[] { eventName }, listenerCreateCallback: listenerCreateCallback, validator: validator, timeout: timeout);
        }

        public T WaitForEvent<T>(int type, IEnumerable<string> eventNames, Action listenerCreateCallback = null, Func<T, bool> validator = null, TimeSpan? timeout = null)
        {
            Action<BsonDocument> handler = null;
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                T result = default(T);

                handler = AddListener<T>(type, eventNames, data =>
                {
                    if (tcs.Task.IsCompleted)
                    {
                        return;
                    }
                    if (!tcs.Task.IsCompleted && (validator == null || validator.Invoke(data)))
                    {
                        result = data;
                        tcs.SetResult(true);
                    }
                });

                listenerCreateCallback?.Invoke();

                if (timeout == null)
                {
                    tcs.Task.Wait(CancellationToken.Token);
                }
                else
                {
                    Task.WhenAny(
                        tcs.Task,
                        Task.Delay((int)timeout.Value.TotalMilliseconds, CancellationToken.Token)).Wait();
                }

                CancellationToken.Token.ThrowIfCancellationRequested();

                if (!tcs.Task.IsCompleted)
                {
                    throw new TimeoutException();
                }

                return result;
            }
            finally
            {
                if (handler != null)
                {
                    RemoveListener(handler);
                }
            }
        }

        public Action<BsonDocument> AddListener<T>(int type, Action<T> handler)
        {
            return AddListener<T>(type, new string[0], handler);
        }

        public Action<BsonDocument> AddListener<T>(int type, string eventName, Action<T> handler)
        {
            return AddListener<T>(type, new[] { eventName }, handler);
        }

        public Action<BsonDocument> AddListener<T>(int type, IEnumerable<string> eventNames, Action<T> handler)
        {
            var handlerName = $"{type}{(eventNames.Any() ? $" ({string.Join(", ", eventNames)})" : string.Empty)}";

            Action<BsonDocument> listener = doc =>
            {
                if (doc.Contains("s") && !doc["s"].IsBsonNull)
                {
                    Sequence = doc["s"].AsInt32;
                }
                if ((type == -1 || doc["op"] == type) && (!eventNames.Any() || eventNames.Contains(doc["t"].AsString)))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            if (typeof(T) == typeof(BsonDocument))
                            {
                                handler((T)(object)(doc["d"].IsBsonNull ? null : doc["d"].AsBsonDocument));
                            }
                            else
                            {
                                handler((T)BsonTypeMapper.MapToDotNetValue(doc["d"]));
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"[{Name}] Handler for {handlerName} failed: {e}");
                        }
                    });
                }
            };

            lock (Listeners)
            {
                Listeners.Add(listener);
            }

            if (DetailedLogging)
            {
                Logger.Debug($"[{Name}] Listener created for {handlerName}.");
            }

            return listener;
        }

        public void RemoveListener(Action<BsonDocument> handler)
        {
            Listeners.Remove(handler);
        }

        internal void Send(int type, BsonValue data = null)
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

        protected void ManageThread(Thread thread, string name = null)
        {
            if (!thread.IsAlive)
            {
                thread.Start();
            }
            Threads.Add(thread);
            thread.Name = name ?? $"{Name} {thread.ManagedThreadId}";
        }

        protected void StartHeart()
        {
            if (Threads.Any(x => x.Name == $"{Name} Heart"))
            {
                throw new InvalidOperationException("Heart is already running.");
            }
            ManageThread(new Thread(Heart), name: $"{Name} Heart");
        }

        protected void Heart()
        {
            Logger.Debug($"[{Name}] Heart started.");

            var nextHeartbeat = Environment.TickCount + HeartbeatIntervalMilliseconds;

            try
            {
                while (Socket.State == WebSocketState.Open && !CancellationToken.IsCancellationRequested)
                {
                    if (Environment.TickCount < nextHeartbeat)
                    {
                        Task.Delay(nextHeartbeat - Environment.TickCount, CancellationToken.Token).Wait();
                    }

                    if (CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    nextHeartbeat = Environment.TickCount + HeartbeatIntervalMilliseconds;
                    Heartbeat();
                }
            }
            catch (TaskCanceledException)
            {
                // intentionally empty.
            }
            catch (OperationCanceledException)
            {
                // intentionally empty.
            }
            catch (AggregateException ex)
            {
                if (!ex.Flatten().InnerExceptions.All(x => x is TaskCanceledException || x is OperationCanceledException))
                {
                    Logger.Error(ex, $"Exception in heart: {ex}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Exception in heart: {ex}");
            }
            finally
            {
                Logger.Debug($"[{Name}] Heart stopped.");
            }
        }

        protected abstract void Heartbeat();

        private void RateLimitWait(Action callback)
        {
            do
            {
                LimitTracker.RemoveAll(x => (DateTime.UtcNow - x).TotalSeconds > 60);
            }
            while (LimitTracker.Count >= Connectivity.GatewayEventsPerMinute);

            LimitTracker.Add(DateTime.UtcNow);
            callback();
        }

        private void PurgeThreads()
        {
            Logger.Debug($"[{Name}] Purging threads...");

            if (!CancellationToken.IsCancellationRequested)
            {
                CancellationToken.Cancel();
            }
            else
            {
                Logger.Warn($"[{Name}] Cancellation requested before thread purge call.");
            }

            foreach (var thread in Threads)
            {
                if (!thread.Join(30000))
                {
                    Logger.Error($"[{Name}] Thread '{thread.Name}' still alive after 30 seconds.");
                }
            }

            Threads.Clear();
        }
    }
}
