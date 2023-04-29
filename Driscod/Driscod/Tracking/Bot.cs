using Driscod.Extensions;
using Driscod.Gateway;
using Driscod.Gateway.Consts;
using Driscod.Network;
using Driscod.Tracking.Objects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Driscod.Tracking;

public interface IBot
{
    event EventHandler<Message>? OnMessage;
    event EventHandler<Message>? OnMessageEdit;
    event EventHandler<(Channel Channel, User User)>? OnTyping;
    event EventHandler<(Guild Guild, Channel Channel, User User, bool IsDeaf, bool IsMuted)>? OnVoiceStateChange;
    event EventHandler<(Guild Guild, User User)>? OnUserJoin;

    User User { get; }

    IEnumerable<Emoji> Emojis { get; }

    IEnumerable<Guild> Guilds { get; }

    IEnumerable<Channel> Channels { get; }

    IEnumerable<User> KnownUsers { get; }

    bool Ready { get; }

    Task Start();

    Task Stop();

    Task<JObject?> SendJson(HttpMethod method, string pathFormat, string[] pathParams, JObject? doc = null, IEnumerable<IMessageAttachment>? attachments = null, Dictionary<string, string>? queryParams = null);

    Task<T?> SendJson<T>(HttpMethod method, string pathFormat, string[] pathParams, JObject? doc = null, IEnumerable<IMessageAttachment>? attachments = null, Dictionary<string, string>? queryParams = null)
        where T : JContainer;

    T? GetObject<T>(string id)
        where T : DiscordObject;

    IEnumerable<T> GetObjects<T>()
        where T : DiscordObject;

    void DeleteObject<T>(string id);

    void CreateOrUpdateObject<T>(JObject doc, Shard discoveredBy)
        where T : DiscordObject, new();
}

public class Bot : IBot
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Dictionary<Tuple<string, string?>, string> _rateLimitPathBucketMap = new();
    private readonly Dictionary<string, RateLimit> _rateLimits = new();
    private readonly Dictionary<Type, Dictionary<string, DiscordObject>> _objects = new();
    private readonly string _token;
    private readonly int _intents;
    private List<Shard> _shards = new();
    private string? _userId = null;
    private HttpClient? _httpClient = null;

    public event EventHandler<Message>? OnMessage;
    public event EventHandler<Message>? OnMessageEdit;
    public event EventHandler<(Channel Channel, User User)>? OnTyping;
    public event EventHandler<(Guild Guild, Channel Channel, User User, bool IsDeaf, bool IsMuted)>? OnVoiceStateChange;
    public event EventHandler<(Guild Guild, User User)>? OnUserJoin;

    public Bot(string token, Intents intents)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token), "Token cannot be null.");
        _intents = (int)intents;

        CreateShards();
        CreateDispatchListeners();
    }

    private HttpClient HttpClient
    {
        get
        {
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
                _httpClient.BaseAddress = new Uri(Connectivity.HttpApiEndpoint);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bot {_token}");
                _httpClient.DefaultRequestHeaders.Add("X-RateLimit-Precision", "millisecond");
            }
            return _httpClient;
        }
    }

    public User User => GetObject<User>(_userId!)!;

    public IEnumerable<Emoji> Emojis => GetObjects<Emoji>();

    public IEnumerable<Guild> Guilds => GetObjects<Guild>();

    public IEnumerable<Channel> Channels => GetObjects<Channel>();

    public IEnumerable<User> KnownUsers => GetObjects<User>().Where(x => x != User);

    public bool Ready => _shards.All(x => x.Ready);

    public async Task Start()
    {
        var remainingConnections = await FetchRemainingConnections();
        if (remainingConnections < _shards.Count)
        {
            throw new InvalidOperationException("Bot cannot start, session creation limit met.");
        }
        else if (remainingConnections < 50)
        {
            Logger.Warn($"{remainingConnections} session creations remain.");
        }

        foreach (var shard in _shards)
        {
            await shard.Start();
            await Task.Delay(2000); // hmm...
        }
        while (!Ready)
        {
            await Task.Delay(10);
        }
    }

    public async Task Stop()
    {
        foreach (var shard in _shards)
        {
            await shard.Stop();
        }
        _objects.Clear();
        _rateLimitPathBucketMap.Clear();
        _rateLimits.Clear();
    }

    public async Task<JObject?> SendJson(HttpMethod method, string pathFormat, string[] pathParams, JObject? doc = null, IEnumerable<IMessageAttachment>? attachments = null, Dictionary<string, string>? queryParams = null)
    {
        return await SendJson<JObject>(method, pathFormat, pathParams, doc: doc, attachments: attachments, queryParams: queryParams);
    }

    public async Task<T?> SendJson<T>(HttpMethod method, string pathFormat, string[] pathParams, JObject? doc = null, IEnumerable<IMessageAttachment>? attachments = null, Dictionary<string, string>? queryParams = null)
        where T : JContainer
    {
        if (pathFormat.StartsWith("/"))
        {
            throw new ArgumentException($"Path cannot start with a forward slash.", nameof(pathFormat));
        }

        var json = doc?.ToString(Formatting.None);

        T? output = default;
        var requestPath = string.Format(pathFormat, pathParams);

        if (queryParams != null)
        {
            requestPath += $"?{string.Join("&", queryParams.Where(kvp => kvp.Value != null).Select(kvp => $"{kvp.Key}={kvp.Value}"))}";
        }

        Func<Task<HttpResponseMessage>> requestFunc = async () =>
        {
            var requestMessage = new HttpRequestMessage(method, requestPath);

            if (attachments != null && attachments.Any())
            {
                requestMessage.Content = new MultipartFormDataContent()
                    .AddJsonPayload(json)
                    .AddAttachments(attachments);
            }
            else
            {
                if (json != null)
                {
                    requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }
            }

            Logger.Debug($"{method} to '{requestPath}': {json}");
            var response = await HttpClient.SendAsync(requestMessage);

            if (response.IsSuccessStatusCode)
            {
                output = JsonConvert.DeserializeObject<T>(response.Content.ReadAsStringAsync().Result);
            }
            else if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                Logger.Error($"Failed to send to '{requestPath}': {response.StatusCode}");
            }
            return response;
        };

        // only take first (major) param into consideration for rate limits.
        var key = new Tuple<string, string?>(pathFormat, pathParams?.FirstOrDefault());

        if (_rateLimitPathBucketMap.ContainsKey(key))
        {
            await _rateLimits[_rateLimitPathBucketMap[key]].PerformRequest(requestFunc);
        }
        else
        {
            var response = await requestFunc();
            if (response.Headers.Contains("X-RateLimit-Bucket"))
            {
                var bucketId = response.Headers.First(x => x.Key.ToLower() == "X-RateLimit-Bucket".ToLower()).Value.First();
                _rateLimitPathBucketMap[key] = bucketId;
                if (!_rateLimits.ContainsKey(bucketId))
                {
                    _rateLimits[bucketId] = new RateLimit(bucketId);
                }
            }
            response.EnsureSuccessStatusCode();
        }

        return output;
    }

    public IEnumerable<T> GetObjects<T>()
        where T : DiscordObject
    {
        return _objects.ContainsKey(typeof(T)) ? _objects[typeof(T)].Values.Cast<T>() : new T[0];
    }

    public T? GetObject<T>(string id)
        where T : DiscordObject
    {
        if (id != null && _objects.ContainsKey(typeof(T)) && _objects[typeof(T)].ContainsKey(id))
        {
            return (T)_objects[typeof(T)][id];
        }
        return null;
    }

    public void DeleteObject<T>(string id)
    {
        _objects[typeof(T)].Remove(id);
    }

    public void CreateOrUpdateObject<T>(JObject doc, Shard discoveredBy)
        where T : DiscordObject, new()
    {
        if (!doc.ContainsKey("id") || doc["id"]!.Type == JTokenType.Null)
        {
            throw new ArgumentException($"Object document does not contain an ID: {doc}", nameof(doc));
        }

        var type = typeof(T);

        if (typeof(IUntracked).IsAssignableFrom(type))
        {
            throw new ArgumentException($"DiscordObject '{type.Name}' should not be tracked.");
        }

        Dictionary<string, DiscordObject> table;

        lock (_objects)
        {
            if (!_objects.ContainsKey(type))
            {
                _objects[type] = new Dictionary<string, DiscordObject>();
            }

            table = _objects[type];
        }

        var id = doc!["id"]!.ToObject<string>()!;

        lock (table)
        {
            if (table.ContainsKey(id))
            {
                table[id].UpdateFromDocument(doc);
            }
            else
            {
                table[id] = DiscordObject.Create<T>(this, doc, discoveredBy);
            }
        }
    }

    private void CreateShards()
    {
        _shards = new List<Shard>();

        var shards = 1; // TODO: detect how many shards are required.
        for (var i = 0; i < shards; i++)
        {
            _shards.Add(new Shard(_token, i, shards, intents: _intents));
        }
    }

    private void SetShardBotNames()
    {
        foreach (var shard in _shards)
        {
            shard.BotName = $"{User.Username}#{User.Discriminator}";
        }
    }

    private void CreateDispatchListeners()
    {
        foreach (var shard in _shards)
        {
            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.Ready,
                data =>
                {
                    _userId = data!["user"]!["id"]!.ToObject<string>();
                    CreateOrUpdateObject<User>(data["user"]!.ToObject<JObject>()!, discoveredBy: shard);
                    SetShardBotNames();
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.MessageCreate,
                data =>
                {
                    if (Ready)
                    {
                        OnMessage?.Invoke(this, DiscordObject.Create<Message>(this, data!, discoveredBy: shard));
                    }
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.MessageUpdate,
                data =>
                {
                    if (Ready)
                    {
                        OnMessageEdit?.Invoke(this, DiscordObject.Create<Message>(this, data!, discoveredBy: shard));
                    }
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.TypingStart,
                data =>
                {
                    if (Ready)
                    {
                        OnTyping?.Invoke(this, (GetObject<Channel>(data!["channel_id"]!.ToObject<string>()!)!, GetObject<User>(data["user_id"]!.ToObject<string>()!))!);
                    }
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.GuildCreate, EventNames.GuildUpdate },
                data =>
                {
                    CreateOrUpdateObject<Guild>(data!, discoveredBy: shard);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.GuildDelete },
                data =>
                {
                    DeleteObject<Guild>(data!["guild_id"]!.ToObject<string>()!);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.ChannelCreate, EventNames.ChannelUpdate },
                data =>
                {
                    if (data!.ContainsKey("guild_id"))
                    {
                        GetObject<Guild>(data["guild_id"]!.ToObject<string>()!)!.UpdateChannel(data);
                    }
                    else
                    {
                        CreateOrUpdateObject<Channel>(data, discoveredBy: shard);
                    }
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.ChannelDelete,
                data =>
                {
                    GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!)!.DeleteChannel(data["id"]!.ToObject<string>()!);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.GuildEmojisUpdate,
                data =>
                {
                    data!["id"] = data["guild_id"];
                    CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.GuildRoleCreate, EventNames.GuildRoleUpdate },
                data =>
                {
                    GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!)!.UpdateRole(data["role"]!.ToObject<JObject>()!);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.GuildRoleDelete,
                data =>
                {
                    GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!)!.DeleteRole(data["role_id"]!.ToObject<string>()!);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.PresenceUpdate,
                data =>
                {
                    GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!)!.UpdatePresence(data);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.VoiceStateUpdate,
                data =>
                {
                    var userId = data!.ContainsKey("member") ? data["member"]!["user"]!["id"]!.ToObject<string>()! : data["user_id"]!.ToObject<string>()!;
                    var deaf = data["self_deaf"]!.ToObject<bool>() || data["deaf"]!.ToObject<bool>();
                    var mute = data["self_mute"]!.ToObject<bool>() || data["mute"]!.ToObject<bool>();

                    GetObject<Guild>(data["guild_id"]!.ToObject<string>()!)!.UpdateVoiceState(data);

                    OnVoiceStateChange?.Invoke(this, (GetObject<Guild>(data["guild_id"]!.ToObject<string>()!)!, GetObject<Channel>(data["channel_id"]!.ToObject<string>()!)!, GetObject<User>(userId), deaf, mute)!);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.GuildMemberAdd },
                data =>
                {
                    var guild = GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!);
                    if (guild != null)
                    {
                        guild.UpdateMember(data);
                        OnUserJoin?.Invoke(this, (guild, GetObject<User>(data["user"]!["id"]!.ToObject<string>()!)!));
                    }
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                new[] { EventNames.GuildMemberUpdate },
                data =>
                {
                    var guild = GetObject<Guild>(data!["guild_id"]!.ToObject<string>()!);
                    guild?.UpdateMember(data);
                });

            shard.AddListener<JObject>(
                (int)Shard.MessageType.Dispatch,
                EventNames.GuildMemberRemove,
                data =>
                {
                    var userId = data!["user"]!["id"]!.ToObject<string>()!;
                    GetObject<Guild>(data["guild_id"]!.ToObject<string>()!)!.DeleteMember(userId);

                    if (!Guilds.Any(x => x.Members.Any(y => y.User.Id == userId)))
                    {
                        Logger.Warn($"Deleted user '{userId}'");
                        DeleteObject<User>(userId);
                    }
                });
        }
    }

    private async Task<int> FetchRemainingConnections()
    {
        var response = await HttpClient.GetAsync("gateway/bot");
        var content = await response.Content.ReadAsStringAsync();
        return JObject.Parse(content)["session_start_limit"]!["remaining"]!.ToObject<int>();
    }
}
