using Driscod.DiscordObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using Driscod.Gateway;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Driscod
{
    public class Bot
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

        public HttpClient HttpClient
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

        public Bot(string token, int intents = (int)Intents.All)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token), "Token cannot be null.");
            _intents = intents;

            CreateShards();
            CreateDispatchListeners();
        }

        public void Start()
        {
            var remainingConnections = BsonDocument.Parse(HttpClient.GetAsync("gateway/bot").Result.Content.ReadAsStringAsync().Result)["session_start_limit"]["remaining"].AsInt32;
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
                Thread.Sleep(5000); // hmm...
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

        public BsonValue SendJson(HttpMethod method, string pathFormat, string[] pathParams, BsonDocument doc = null, Dictionary<string, string> queryParams = null)
        {
            if (pathFormat.StartsWith("/"))
            {
                throw new ArgumentException($"Path cannot start with a forward slash.", nameof(pathFormat));
            }

            var json = doc?.ToString();

            BsonValue output = null;
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
                    output = BsonSerializer.Deserialize<BsonValue>(response.Content.ReadAsStringAsync().Result);
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
                RateLimits[RateLimitPathBucketMap[key]].LockAndWait(requestFunc);
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
            if (Objects.ContainsKey(typeof(T)) && Objects[typeof(T)].ContainsKey(id))
            {
                return (T)Objects[typeof(T)][id];
            }
            return null;
        }

        internal void DeleteObject<T>(string id)
        {
            Objects[typeof(T)].Remove(id);
        }

        internal void CreateOrUpdateObject<T>(BsonDocument doc, Shard discoveredBy = null)
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

            var id = doc["id"].AsString;

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
                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "READY",
                    data =>
                    {
                        _userId = data["user"]["id"].AsString;
                        CreateOrUpdateObject<User>(data["user"].AsBsonDocument);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "MESSAGE_CREATE",
                    data =>
                    {
                        if (Ready)
                        {
                            OnMessage?.Invoke(this, DiscordObject.Create<Message>(this, data, discoveredBy: shard));
                        }
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "MESSAGE_UPDATE",
                    data =>
                    {
                        if (Ready)
                        {
                            OnMessageEdit?.Invoke(this, DiscordObject.Create<Message>(this, data, discoveredBy: shard));
                        }
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "TYPING_START",
                    data =>
                    {
                        if (Ready)
                        {
                            OnTyping?.Invoke(this, (GetObject<Channel>(data["channel_id"].AsString), GetObject<User>(data["user_id"].AsString)));
                        }
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_CREATE", "GUILD_UPDATE" },
                    data =>
                    {
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_DELETE" },
                    data =>
                    {
                        DeleteObject<Guild>(data["guild_id"].AsString);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "CHANNEL_CREATE", "CHANNEL_UPDATE" },
                    data =>
                    {
                        if (data.Contains("guild_id"))
                        {
                            GetObject<Guild>(data["guild_id"].AsString).UpdateChannel(data);
                        }
                        else
                        {
                            CreateOrUpdateObject<Channel>(data, discoveredBy: shard);
                        }
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "CHANNEL_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).DeleteChannel(data["id"].AsString);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "GUILD_EMOJIS_UPDATE",
                    data =>
                    {
                        data["id"] = data["guild_id"];
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    new[] { "GUILD_ROLE_CREATE", "GUILD_ROLE_UPDATE" },
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).UpdateRole(data["role"].AsBsonDocument);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "GUILD_ROLE_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).DeleteRole(data["role_id"].AsString);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "PRESENCE_UPDATE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).UpdatePresence(data);
                    });

                shard.AddListener<BsonDocument>(
                    (int)Shard.MessageType.Dispatch,
                    "VOICE_STATE_UPDATE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).UpdateVoiceState(data);
                    });
            }
        }
    }
}
