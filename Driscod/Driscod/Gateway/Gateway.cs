﻿using Driscod.Network;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace Driscod.Gateway;

public abstract class Gateway
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static bool DetailedLogging { get; set; } = false;

    private readonly object _listenerLock = new();
    private readonly List<DateTime> _limitTracker = new();
    private readonly List<Task> _tasks = new();
    private readonly List<Action<JObject>> _listeners = new();

    protected Gateway(string url)
    {
        Socket = new WebSocket(url);

        Socket.Opened += (a, b) =>
        {
            if (DetailedLogging)
            {
                Logger.Info($"[{Name}] Socket opened.");
            }
        };

        Socket.Closed += async (_, e) =>
        {
            var evnt = e as ClosedEventArgs;
            if (DetailedLogging)
            {
                Logger.Info($"[{Name}] Socket closed. Code: {evnt?.Code}, Reason: {evnt?.Reason}");
            }

            if (KeepSocketOpen)
            {
                if (evnt != null && RespectedCloseSocketCodes.Contains(evnt.Code))
                {
                    if (DetailedLogging)
                    {
                        Logger.Debug($"[{Name}] Socket is marked to be kept open, but encountered respected close code '{evnt.Code}'.");
                    }
                    await ClearTasks();
                }
                else
                {
                    await ClearTasks();
                    Thread.Sleep(ReconnectDelay);
                    await Start();
                }
            }
            else
            {
                await ClearTasks();
            }
        };

        Socket.MessageReceived += new EventHandler<MessageReceivedEventArgs>((sender, message) =>
        {
            if (DetailedLogging)
            {
                Logger.Debug($"[{Name}] <- {message.Message}");
            }

            var doc = JObject.Parse(message.Message);

            lock (_listenerLock)
            {
                foreach (var listener in _listeners)
                {
                    Task.Run(() =>
                    {
                        listener.Invoke(doc);
                    });
                }
            }
        });
    }

    protected int Sequence { get; set; }

    protected WebSocket Socket { get; private set; }

    protected bool KeepSocketOpen { get; set; }

    protected virtual IEnumerable<int> RespectedCloseSocketCodes => new int[0];

    protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(1);

    protected CancellationTokenSource CancellationTokenSource { get; set; } = new();

    protected int HeartbeatIntervalMilliseconds { get; set; }

    public abstract string Name { get; }

    public bool Running => Socket.State == WebSocketState.Open;

    public virtual Task Start()
    {
        CancellationTokenSource = new CancellationTokenSource();
        CancellationTokenSource.Token.Register(() =>
        {
            if (DetailedLogging)
            {
                Logger.Debug($"[{Name}] Cancellation requested, threads should stop.");
            }
        });

        try
        {
            Socket.Open();
        }
        catch (Exception)
        {
            Logger.Warn($"[{Name}] Attempted to open socket, but socket was not closed.");
        }

        return Task.CompletedTask;
    }

    public virtual async Task Stop()
    {
        KeepSocketOpen = false;
        if (Socket.State != WebSocketState.Closed)
        {
            Socket.Close("Internal stop call.");
        }
        while (Socket.State != WebSocketState.Closed || _tasks.Any())
        {
            await Task.Delay(200);
        }
    }

    public async Task Restart()
    {
        await Stop();
        await Start();
    }

    public async Task<T?> ListenForEvent<T>(int type, Action? listenerCreateCallback = null, Func<T?, bool>? validator = null, TimeSpan? timeout = null)
    {
        return await ListenForEvent(type, new string[0], listenerCreateCallback: listenerCreateCallback, validator: validator, timeout: timeout);
    }

    public async Task<T?> ListenForEvent<T>(int type, string eventName, Action? listenerCreateCallback = null, Func<T?, bool>? validator = null, TimeSpan? timeout = null)
    {
        return await ListenForEvent(type, new[] { eventName }, listenerCreateCallback: listenerCreateCallback, validator: validator, timeout: timeout);
    }

    public async Task<T?> ListenForEvent<T>(int type, IEnumerable<string> eventNames, Action? listenerCreateCallback = null, Func<T?, bool>? validator = null, TimeSpan? timeout = null)
    {
        Action<JObject>? handler = null;
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            T? result = default(T);

            handler = AddListener<T?>(type, eventNames, data =>
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
                await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromMinutes(15), CancellationTokenSource.Token));
            }
            else
            {
                await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(timeout.Value, CancellationTokenSource.Token));
            }

            CancellationTokenSource.Token.ThrowIfCancellationRequested();

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

    public Action<JObject?> AddListener<T>(int type, Action<T?> handler)
    {
        return AddListener(type, new string[0], handler);
    }

    public Action<JObject?> AddListener<T>(int type, string eventName, Action<T?> handler)
    {
        return AddListener(type, new[] { eventName }, handler);
    }

    public Action<JObject?> AddListener<T>(int type, IEnumerable<string> eventNames, Action<T?> handler)
    {
        var handlerName = $"{type}{(eventNames.Any() ? $" ({string.Join(", ", eventNames)})" : string.Empty)}";

        Action<JObject?> listener = doc =>
        {
            if (doc!.ContainsKey("s") && doc["s"]!.Type != JTokenType.Null)
            {
                Sequence = doc["s"]!.ToObject<int>();
            }
            if ((type == -1 || doc["op"]!.ToObject<int>() == type) && (!eventNames.Any() || eventNames.Contains(doc["t"]!.ToObject<string>())))
            {
                Task.Run(() =>
                {
                    try
                    {
                        if (doc["d"]!.Type == JTokenType.Null)
                        {
                            handler(default);
                        }
                        else
                        {
                            handler(doc["d"]!.ToObject<T?>());
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"[{Name}] Handler for {handlerName} failed: {e}");
                    }
                });
            }
        };

        lock (_listenerLock)
        {
            _listeners.Add(listener);
        }

        if (DetailedLogging)
        {
            Logger.Debug($"[{Name}] Listener created for {handlerName}.");
        }

        return listener;
    }

    public void RemoveListener(Action<JObject> handler)
    {
        lock (_listenerLock)
        {
            _listeners.Remove(handler);
        }
    }

    internal async Task Send<T>(int type, T? data = default)
    {
        var response = new JObject
        {
            { "op", type },
        };
        if (data != null)
        {
            response["d"] = JToken.FromObject(data);
        }
        await RateLimitWait().ContinueWith(_ =>
        {
            if (DetailedLogging)
            {
                Logger.Debug($"[{Name}] -> {response.ToString(Newtonsoft.Json.Formatting.None)}");
            }
            Socket.Send(response.ToString());
        });
    }

    protected void ManageTask(Task task)
    {
        if (!CancellationTokenSource.IsCancellationRequested)
        {
            _tasks.Add(task);
        }
        else
        {
            Logger.Warn($"[{Name}] Attempted to manage task while cancellation is requested.");
        }
    }

    protected void StartHeart()
    {
        ManageTask(Heart());
    }

    protected async Task Heart()
    {
        if (DetailedLogging)
        {
            Logger.Debug($"[{Name}] Heart started.");
        }

        var nextHeartbeat = Environment.TickCount + HeartbeatIntervalMilliseconds;

        try
        {
            while (Socket.State == WebSocketState.Open && !CancellationTokenSource.IsCancellationRequested)
            {
                if (Environment.TickCount < nextHeartbeat)
                {
                    await Task.Delay(nextHeartbeat - Environment.TickCount, CancellationTokenSource.Token);
                }

                if (CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }

                nextHeartbeat = Environment.TickCount + HeartbeatIntervalMilliseconds;
                await Heartbeat();
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
            if (DetailedLogging)
            {
                Logger.Debug($"[{Name}] Heart stopped.");
            }
        }
    }

    protected abstract Task Heartbeat();

    private async Task RateLimitWait()
    {
        await Task.Run(() =>
        {
            do
            {
                _limitTracker.RemoveAll(x => (DateTime.UtcNow - x).TotalSeconds > 60);
            }
            while (_limitTracker.Count >= Connectivity.GatewayEventsPerMinute);

            _limitTracker.Add(DateTime.UtcNow);
        });
    }

    private async Task ClearTasks()
    {
        if (DetailedLogging)
        {
            Logger.Debug($"[{Name}] Clearing tasks...");
        }

        if (!CancellationTokenSource.IsCancellationRequested)
        {
            CancellationTokenSource.Cancel();
        }
        else
        {
            Logger.Warn($"[{Name}] Cancellation requested before task clear call.");
        }

        foreach (var task in _tasks)
        {
            await Task.WhenAny(
                task,
                Task.Delay(TimeSpan.FromSeconds(30)));
            if (!task.IsCompleted)
            {
                Logger.Error($"[{Name}] Task '{task.Id}' still running after 30 seconds.");
            }
        }

        _tasks.Clear();
    }
}
