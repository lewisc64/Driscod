using Driscod.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Driscod.Tracking.Objects
{
    // TODO: the rest of the attributes
    public class Message : DiscordObject, IUntracked
    {
        private string _authorId;

        private string _channelId;

        private string _content;

        private List<Reaction> _reactions;

        public User Author => Bot.GetObject<User>(_authorId);

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public string Content
        {
            get
            {
                return _content;
            }

            set
            {
                _content = value;
                UpdateFromDocument(Bot.SendJson(HttpMethod.Patch, Connectivity.ChannelMessagePathFormat, new[] { Channel.Id, Id }, new JObject
                {
                    { "content", _content },
                }));
            }
        }

        public IEnumerable<MessageEmbed> Embeds { get; set; }

        public IEnumerable<Reaction> Reactions => _reactions;

        public IEnumerable<Reaction> MyReactions => Reactions.Where(x => x.BotUserReacted);

        public Message PreviousMessage => GetRelativeMessage("before");

        public Message NextMessage => GetRelativeMessage("after");

        public override string ToString()
        {
            return Content;
        }

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"].ToObject<string>();
            _authorId = doc["author"]["id"].ToObject<string>();
            _channelId = doc["channel_id"].ToObject<string>();
            _content = doc["content"].ToObject<string>();

            if (doc.ContainsKey("reactions"))
            {
                _reactions = new List<Reaction>();
                foreach (var reactionDoc in doc["reactions"].ToArray())
                {
                    var reaction = new Reaction
                    {
                        DiscoveredOnShard = DiscoveredOnShard,
                        Bot = Bot,
                    };
                    reaction.UpdateFromDocument(reactionDoc.ToObject<JObject>());
                    _reactions.Add(reaction);
                }
            }

            Embeds = doc["embeds"].Select(x => x.ToObject<MessageEmbed>());
        }

        private Message GetRelativeMessage(string paramName)
        {
            var doc = Bot.SendJson<JArray>(
                HttpMethod.Get,
                Connectivity.ChannelMessagesPathFormat,
                new[] { Channel.Id },
                queryParams: new Dictionary<string, string>() { { paramName, Id }, { "limit", "1" } })?.ToObject<JObject[]>().FirstOrDefault();

            if (doc == null)
            {
                return null;
            }

            return Create<Message>(Bot, doc, discoveredBy: DiscoveredOnShard);
        }
    }

    public class MessageEmbed
    {
        private static Dictionary<ContentType, string> ContentTypeNameMap => new Dictionary<ContentType, string>
        {
            { ContentType.Rich, "rich" },
            { ContentType.Image, "image" },
            { ContentType.Video, "video" },
            { ContentType.AnimatedGif, "gifv" },
            { ContentType.Article, "article" },
            { ContentType.Link, "link" },
        };

        [JsonProperty("type")]
        private string _typeName;

        [JsonIgnore]
        public ContentType Type
        {
            get
            {
                return ContentTypeNameMap.First(kvp => kvp.Value == _typeName).Key;
            }

            set
            {
                _typeName = ContentTypeNameMap[value];
            }
        }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("color")]
        public int Color { get; set; }

        [JsonProperty("footer")]
        public FooterInfo Footer { get; set; }

        [JsonProperty("image")]
        public ImageInfo Image { get; set; }

        [JsonProperty("thumbnail")]
        public ThumbnailInfo Thumbnail { get; set; }

        [JsonProperty("video")]
        public VideoInfo Video { get; set; }

        [JsonProperty("provider")]
        public ProviderInfo Provider { get; set; }

        [JsonProperty("author")]
        public AuthorInfo Author { get; set; }

        [JsonProperty("fields")]
        public IEnumerable<FieldInfo> Fields { get; set; }

        public class FooterInfo
        {
            [JsonProperty("text"), JsonRequired]
            public string Text { get; set; }

            [JsonProperty("icon_url")]
            public string IconUrl { get; set; }

            [JsonProperty("proxy_icon_url")]
            public string ProxyIconUrl { get; set; }
        }

        public class ImageInfo
        {
            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("proxy_url")]
            public string ProxyUrl { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }
        }

        public class ThumbnailInfo : ImageInfo
        {
        }

        public class VideoInfo
        {
            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }
        }

        public class ProviderInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }
        }

        public class AuthorInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("icon_url")]
            public string IconUrl { get; set; }

            [JsonProperty("proxy_icon_url")]
            public string ProxyIconUrl { get; set; }
        }

        public class FieldInfo
        {
            [JsonProperty("name"), JsonRequired]
            public string Name { get; set; }

            [JsonProperty("value"), JsonRequired]
            public string Value { get; set; }

            [JsonProperty("inline")]
            public bool Inline { get; set; }
        }

        public enum ContentType
        {
            Rich,
            Image,
            Video,
            AnimatedGif,
            Article,
            Link,
        }
    }
}
