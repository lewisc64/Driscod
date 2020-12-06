using Driscod.Gateway;
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
using System.Threading;

namespace Driscod.Tracking
{
    public interface IBot
    {
        User User { get; }

        IEnumerable<Emoji> Emojis { get; }

        IEnumerable<Guild> Guilds { get; }

        IEnumerable<Channel> Channels { get; }

        IEnumerable<User> KnownUsers { get; }

        bool Ready { get; }

        event EventHandler<Message> OnMessage;

        event EventHandler<Message> OnMessageEdit;

        event EventHandler<(Channel Channel, User User)> OnTyping;

        void Start();

        void Stop();

        JObject SendJson(HttpMethod method, string pathFormat, string[] pathParams, JObject doc = null, Dictionary<string, string> queryParams = null);

        T SendJson<T>(HttpMethod method, string pathFormat, string[] pathParams, JObject doc = null, Dictionary<string, string> queryParams = null)
            where T : JContainer;

        T GetObject<T>(string id)
            where T : DiscordObject;

        IEnumerable<T> GetObjects<T>()
            where T : DiscordObject;

        void DeleteObject<T>(string id);

        void CreateOrUpdateObject<T>(JObject doc, Shard discoveredBy = null)
            where T : DiscordObject, new();
    }

    public class Bot : IBot
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private Dictionary<Tuple<string, string>, string> RateLimitPathBucketMap { get; set; } = new Dictionary<Tuple<string, string>, string>();

        private Dictionary<string, RateLimit> RateLimits { get; set; } = new Dictionary<string, RateLimit>();

        private Dictionary<Type, Dictionary<string, DiscordObject>> Objects { get; set; } = new Dictionary<Type, Dictionary<string, DiscordObject>>();

        private List<Shard> _shards;

        private readonly string _token;

        private readonly int _intents;

        private string _userId;

        private HttpClient _httpClient = null;

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

        public User User => GetObject<User>(_userId);

        public IEnumerable<Emoji> Emojis => GetObjects<Emoji>();

        public IEnumerable<Guild> Guilds => GetObjects<Guild>();

        public IEnumerable<Channel> Channels => GetObjects<Channel>();

        public IEnumerable<User> KnownUsers => GetObjects<User>().Where(x => x != User);

        public bool Ready => _shards.All(x => x.Ready);

        public event EventHandler<Message> OnMessage;

        public event EventHandler<Message> OnMessageEdit;

        public event EventHandler<(Channel Channel, User User)> OnTyping;

        public Bot(string token, Intents intents)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token), "Token cannot be null.");
            _intents = (int)intents;

            CreateShards();
            CreateDispatchListeners();
        }

        public void Start()
        {
            var remainingConnections = JObject.Parse(HttpClient.GetAsync("gateway/bot").Result.Content.ReadAsStringAsync().Result)["session_start_limit"]["remaining"].ToObject<int>();
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
                shard.Start();
                Thread.Sleep(2000); // hmm...
            }
            while (!Ready)
            {
                // intentionally empty
            }
        }

        public void Stop()
        {
            foreach (var shard in _shards)
            {
                shard.Stop();
            }
            Objects.Clear();
            RateLimitPathBucketMap.Clear();
            RateLimits.Clear();
        }

        public JObject SendJson(HttpMethod method, string pathFormat, string[] pathParams, JObject doc = null, Dictionary<string, string> queryParams = null)
        {
            return SendJson<JObject>(method, pathFormat, pathParams, doc, queryParams);
        }

        public T SendJson<T>(HttpMethod method, string pathFormat, string[] pathParams, JObject doc = null, Dictionary<string, string> queryParams = null)
            where T : JContainer
        {
            if (pathFormat.StartsWith("/"))
            {
                throw new ArgumentException($"Path cannot start with a forward slash.", nameof(pathFormat));
            }

            var json = doc?.ToString(Formatting.None);

            T output = default;
            var requestPath = string.Format(pathFormat, pathParams);

            if (queryParams != null)
            {
                requestPath += $"?{string.Join("&", queryParams.Where(kvp => kvp.Value != null).Select(kvp => $"{kvp.Key}={kvp.Value}"))}";
            }

            Func<HttpResponseMessage> requestFunc = () =>
            {
                var requestMessage = new HttpRequestMessage(method, requestPath);

                if (json != null)
                {
                    requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var response = HttpClient.SendAsync(requestMessage).Result;

                Logger.Debug($"{method} to '{requestPath}': {json}");

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    output = JsonConvert.DeserializeObject<T>(response.Content.ReadAsStringAsync().Result);
                }
                else
                {
                    Logger.Error($"Failed to send to '{requestPath}': {response.StatusCode}");
                }
                return response;
            };

            // only take first (major) param into consideration for rate limits.
            var key = new Tuple<string, string>(pathFormat, pathParams.FirstOrDefault());

            if (RateLimitPathBucketMap.ContainsKey(key))
            {
                RateLimits[RateLimitPathBucketMap[key]].PerformRequest(requestFunc).Wait();
            }
            else
            {
                var response = requestFunc();
                if (response.Headers.Contains("X-RateLimit-Bucket"))
                {
                    var bucketId = response.Headers.First(x => x.Key.ToLower() == "X-RateLimit-Bucket".ToLower()).Value.First();
                    RateLimitPathBucketMap[key] = bucketId;
                    if (!RateLimits.ContainsKey(bucketId))
                    {
                        RateLimits[bucketId] = new RateLimit(bucketId);
                    }
                }
            }

            return output;
        }

        public IEnumerable<T> GetObjects<T>()
            where T : DiscordObject
        {
            return Objects.ContainsKey(typeof(T)) ? Objects[typeof(T)].Values.Cast<T>() : new T[0];
        }

        public T GetObject<T>(string id)
            where T : DiscordObject
        {
            if (id != null && Objects.ContainsKey(typeof(T)) && Objects[typeof(T)].ContainsKey(id))
            {
                return (T)Objects[typeof(T)][id];
            }
            return null;
        }

        public void DeleteObject<T>(string id)
        {
            Objects[typeof(T)].Remove(id);
        }

        public void CreateOrUpdateObject<T>(JObject doc, Shard discoveredBy = null)
            where T : DiscordObject, new()
        {
            var type = typeof(T);

            if (typeof(IUntracked).IsAssignableFrom(type))
            {
                throw new ArgumentException($"DiscordObject '{type.Name}' should not be tracked.");
            }

            Dictionary<string, DiscordObject> table;

            lock (Objects)
            {
                if (!Objects.ContainsKey(type))
                {
                    Objects[type] = new Dictionary<string, DiscordObject>();
                }

                table = Objects[type];
            }

            var id = doc["id"].ToObject<string>();

            lock (table)
            {
                if (table.ContainsKey(id))
                {
                    table[id].UpdateFromDocument(doc);
                }
                else
                {
                    table[id] = DiscordObject.Create<T>(this, doc, discoveredBy: discoveredBy);
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

        private void CreateDispatchListeners()
        {
            foreach (var shard in _shards)
            {
                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "READY",
                    data =>
                    {
                        _userId = data["user"]["id"].ToObject<string>();
                        CreateOrUpdateObject<User>(data["user"].ToObject<JObject>());
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "MESSAGE_CREATE",
                    data =>
                    {
                        if (Ready)
                        {
                            OnMessage?.Invoke(this, DiscordObject.Create<Message>(this, data, discoveredBy: shard));
                        }
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "MESSAGE_UPDATE",
                    data =>
                    {
                        if (Ready)
                        {
                            OnMessageEdit?.Invoke(this, DiscordObject.Create<Message>(this, data, discoveredBy: shard));
                        }
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "TYPING_START",
                    data =>
                    {
                        if (Ready)
                        {
                            OnTyping?.Invoke(this, (GetObject<Channel>(data["channel_id"].ToObject<string>()), GetObject<User>(data["user_id"].ToObject<string>())));
                        }
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_CREATE", "GUILD_UPDATE" },
                    data =>
                    {
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_DELETE" },
                    data =>
                    {
                        DeleteObject<Guild>(data["guild_id"].ToObject<string>());
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "CHANNEL_CREATE", "CHANNEL_UPDATE" },
                    data =>
                    {
                        if (data.ContainsKey("guild_id"))
                        {
                            GetObject<Guild>(data["guild_id"].ToObject<string>()).UpdateChannel(data);
                        }
                        else
                        {
                            CreateOrUpdateObject<Channel>(data, discoveredBy: shard);
                        }
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "CHANNEL_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].ToObject<string>()).DeleteChannel(data["id"].ToObject<string>());
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "GUILD_EMOJIS_UPDATE",
                    data =>
                    {
                        data["id"] = data["guild_id"];
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_ROLE_CREATE", "GUILD_ROLE_UPDATE" },
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].ToObject<string>()).UpdateRole(data["role"].ToObject<JObject>());
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "GUILD_ROLE_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].ToObject<string>()).DeleteRole(data["role_id"].ToObject<string>());
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "PRESENCE_UPDATE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].ToObject<string>()).UpdatePresence(data);
                    });

                shard.AddListener<JObject>(
                    (int)Shard.MessageType.Dispatch,
                    "VOICE_STATE_UPDATE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].ToObject<string>()).UpdateVoiceState(data);
                    });
            }
        }
    }
}
