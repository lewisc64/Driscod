using Driscod.DiscordObjects;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;

namespace Driscod
{
    public class Bot
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public Dictionary<Tuple<string, string>, string> RateLimitPathBucketMap { get; set; } = new Dictionary<Tuple<string, string>, string>();

        public Dictionary<string, RateLimit> RateLimits { get; set; } = new Dictionary<string, RateLimit>();

        private Dictionary<Type, Dictionary<string, DiscordObject>> Objects { get; set; } = new Dictionary<Type, Dictionary<string, DiscordObject>>();

        private List<Shard> _shards;

        private readonly string _token;

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

        public bool Ready => _shards.All(x => x.Ready);

        public event EventHandler<Message> OnMessage;

        public Bot(string token)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token), "Token cannot be null.");

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
        }

        public BsonDocument SendJson(string pathFormat, string[] pathParams, BsonDocument doc)
        {
            return SendJson(pathFormat, pathParams, doc.ToString());
        }

        public BsonDocument SendJson(string pathFormat, string[] pathParams, string json)
        {
            if (pathFormat.StartsWith("/"))
            {
                throw new ArgumentException($"Path cannot start with a forward slash.", nameof(pathFormat));
            }

            BsonDocument output = null;
            var requestPath = string.Format(pathFormat, pathParams);

            Func<HttpResponseMessage> requestFunc = () =>
            {
                var response = HttpClient.PostAsync(requestPath, new StringContent(json, Encoding.UTF8, "application/json")).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    output = BsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                }
                else
                {
                    Logger.Error($"Failed to post json to '{requestPath}': {response.StatusCode}");
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

            if (!Objects.ContainsKey(type))
            {
                Objects[type] = new Dictionary<string, DiscordObject>();
            }

            var table = Objects[type];
            var id = doc["id"].AsString;

            if (!table.ContainsKey(id))
            {
                table[id] = new T();
                table[id].DiscoveredOnShard = discoveredBy;
                table[id].Bot = this;
            }

            table[id].UpdateFromDocument(doc);
        }

        private void CreateShards()
        {
            _shards = new List<Shard>();

            var shards = 1; // TODO
            for (var i = 0; i < shards; i++)
            {
                _shards.Add(new Shard(_token, i, shards));
            }
        }

        private void CreateDispatchListeners()
        {
            foreach (var shard in _shards)
            {
                shard.AddListener(
                    MessageType.Dispatch,
                    "READY",
                    data =>
                    {
                        _userId = data["user"]["id"].AsString;
                        CreateOrUpdateObject<User>(data["user"].AsBsonDocument);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    "MESSAGE_CREATE",
                    data =>
                    {
                        if (Ready)
                        {
                            var message = new Message();
                            message.DiscoveredOnShard = shard;
                            message.Bot = this;
                            message.UpdateFromDocument(data);
                            OnMessage?.Invoke(this, message);
                        }
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    new[] { "GUILD_CREATE", "GUILD_UPDATE" },
                    data =>
                    {
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    new[] { "GUILD_DELETE" },
                    data =>
                    {
                        DeleteObject<Guild>(data["guild_id"].AsString);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    new[] { "CHANNEL_CREATE", "CHANNEL_UPDATE" },
                    data =>
                    {
                        if (data.Contains("guild_id"))
                        {
                            GetObject<Guild>(data["guild_id"].AsString).UpdateChannel(data);
                        }
                        else
                        {
                            CreateOrUpdateObject<Channel>(data);
                        }
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    "CHANNEL_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).DeleteChannel(data["id"].AsString);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    "GUILD_EMOJIS_UPDATE",
                    data =>
                    {
                        data["id"] = data["guild_id"];
                        CreateOrUpdateObject<Guild>(data, discoveredBy: shard);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    new[] { "GUILD_ROLE_CREATE", "GUILD_ROLE_UPDATE" },
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).UpdateRole(data["role"].AsBsonDocument);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    "GUILD_ROLE_DELETE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).DeleteRole(data["role_id"].AsString);
                    });

                shard.AddListener(
                    MessageType.Dispatch,
                    "PRESENCE_UPDATE",
                    data =>
                    {
                        GetObject<Guild>(data["guild_id"].AsString).UpdatePresence(data);
                    });
            }
        }
    }
}
