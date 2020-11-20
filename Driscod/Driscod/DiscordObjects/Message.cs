using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Driscod.DiscordObjects
{
    // TODO: the rest of the attributes
    public class Message : DiscordObject, IUntracked
    {
        private string _authorId;

        private string _channelId;

        public User Author => Bot.GetObject<User>(_authorId);

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public string Content { get; private set; }

        public IEnumerable<MessageEmbed> Embeds { get; set; }

        public Message PreviousMessage => GetRelativeMessage("before");

        public Message NextMessage => GetRelativeMessage("after");

        public override string ToString()
        {
            return Content;
        }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;
            _authorId = doc["author"]["id"].AsString;
            _channelId = doc["channel_id"].AsString;
            Content = doc["content"].AsString;

            Embeds = doc["embeds"].AsBsonArray.Select(x => BsonSerializer.Deserialize<MessageEmbed>(x.AsBsonDocument));
        }

        private Message GetRelativeMessage(string paramName)
        {
            var doc = Bot.SendJson(
                HttpMethod.Get,
                Connectivity.ChannelMessagePathFormat,
                new[] { Channel.Id },
                queryParams: new Dictionary<string, string>() { { paramName, Id }, { "limit", "1" } })?.AsBsonArray.FirstOrDefault()?.AsBsonDocument;

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

        [BsonElement("type"), BsonIgnoreIfNull]
        private string _typeName;

        [BsonIgnore]
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

        [BsonElement("title"), BsonIgnoreIfNull]
        public string Title { get; set; }

        [BsonElement("description"), BsonIgnoreIfNull]
        public string Description { get; set; }

        [BsonElement("url"), BsonIgnoreIfNull]
        public string Url { get; set; }

        [BsonElement("timestamp"), BsonIgnoreIfNull]
        public string Timestamp { get; set; }

        [BsonElement("color"), BsonIgnoreIfNull]
        public int Color { get; set; }

        [BsonElement("footer"), BsonIgnoreIfNull]
        public FooterInfo Footer { get; set; }

        [BsonElement("image"), BsonIgnoreIfNull]
        public ImageInfo Image { get; set; }

        [BsonElement("thumbnail"), BsonIgnoreIfNull]
        public ThumbnailInfo Thumbnail { get; set; }

        [BsonElement("video"), BsonIgnoreIfNull]
        public VideoInfo Video { get; set; }

        [BsonElement("provider"), BsonIgnoreIfNull]
        public ProviderInfo Provider { get; set; }

        [BsonElement("author"), BsonIgnoreIfNull]
        public AuthorInfo Author { get; set; }

        [BsonElement("fields"), BsonIgnoreIfNull]
        public IEnumerable<FieldInfo> Fields { get; set; }

        public class FooterInfo
        {
            [BsonElement("text"), BsonRequired]
            public string Text { get; set; }

            [BsonElement("icon_url"), BsonIgnoreIfNull]
            public string IconUrl { get; set; }

            [BsonElement("proxy_icon_url"), BsonIgnoreIfNull]
            public string ProxyIconUrl { get; set; }
        }

        public class ImageInfo
        {
            [BsonElement("url"), BsonIgnoreIfNull]
            public string Url { get; set; }

            [BsonElement("proxy_url"), BsonIgnoreIfNull]
            public string ProxyUrl { get; set; }

            [BsonElement("height"), BsonIgnoreIfNull]
            public int Height { get; set; }

            [BsonElement("width"), BsonIgnoreIfNull]
            public int Width { get; set; }
        }

        public class ThumbnailInfo : ImageInfo
        {
        }

        public class VideoInfo
        {
            [BsonElement("url"), BsonIgnoreIfNull]
            public string Url { get; set; }

            [BsonElement("height"), BsonIgnoreIfNull]
            public int Height { get; set; }

            [BsonElement("width"), BsonIgnoreIfNull]
            public int Width { get; set; }
        }

        public class ProviderInfo
        {
            [BsonElement("name"), BsonIgnoreIfNull]
            public string Name { get; set; }

            [BsonElement("url"), BsonIgnoreIfNull]
            public string Url { get; set; }
        }

        public class AuthorInfo
        {
            [BsonElement("name"), BsonIgnoreIfNull]
            public string Name { get; set; }

            [BsonElement("url"), BsonIgnoreIfNull]
            public string Url { get; set; }

            [BsonElement("icon_url"), BsonIgnoreIfNull]
            public string IconUrl { get; set; }

            [BsonElement("proxy_icon_url"), BsonIgnoreIfNull]
            public string ProxyIconUrl { get; set; }
        }

        public class FieldInfo
        {
            [BsonElement("name"), BsonRequired]
            public string Name { get; set; }

            [BsonElement("value"), BsonRequired]
            public string Value { get; set; }

            [BsonElement("inline"), BsonIgnoreIfNull]
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
