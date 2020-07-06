using System;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public interface IMessageable
    {
        void SendMessage(string message);
    }
    
    public enum ChannelType
    {
        Text = 0,
        User = 1,
        Voice = 2,
    }

    public class Channel : DiscordObject, IMessageable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private string _guildId;

        public Guild Guild => Bot.GetObject<Guild>(_guildId);

        public bool IsDm => ChannelType == ChannelType.User;

        public ChannelType ChannelType { get; private set; }

        public string Topic { get; private set; }

        public int Position { get; private set; }

        public BsonArray PermissionOverwrites { get; private set; } // TODO

        public string Name { get; private set; }

        public int Bitrate { get; private set; }

        public void SendMessage(string message)
        {
            if (ChannelType == ChannelType.Voice)
            {
                throw new InvalidOperationException($"Cannot send message to channel type {ChannelType}.");
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message must be non-empty.", nameof(message));
            }

            if (message.Length > 2000)
            {
                throw new ArgumentException("Message must be less than or equal to 2000 characters.", nameof(message));
            }

            Bot.SendJson(Connectivity.ChannelMessagePathFormat, new[] { Id }, new BsonDocument
            {
                { "content", message },
            });
        }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;

            if (doc.Contains("guild_id"))
            {
                _guildId = doc["guild_id"].AsString;
            }

            if (doc.Contains("type"))
            {
                switch (doc["type"].AsInt32)
                {
                    case 0:
                        ChannelType = ChannelType.Text; break;
                    case 1:
                        ChannelType = ChannelType.User; break;
                    case 2:
                        ChannelType = ChannelType.Voice; break;
                    default:
                        Logger.Error($"Unknown channel type on channel '{Id}': {doc["type"]}");
                        break;
                }
            }

            if (doc.Contains("topic"))
            {
                Topic = doc.GetValueOrNull("topic")?.AsString ?? "";
            }

            if (doc.Contains("position"))
            {
                Position = doc["position"].AsInt32;
            }

            if (doc.Contains("permission_overwrites"))
            {
                PermissionOverwrites = doc["permission_overwrites"].AsBsonArray;
            }

            if (doc.Contains("name"))
            {
                Name = doc["name"].AsString;
            }

            if (doc.Contains("bitrate"))
            {
                Bitrate = doc["bitrate"].AsInt32;
            }
        }
    }
}
