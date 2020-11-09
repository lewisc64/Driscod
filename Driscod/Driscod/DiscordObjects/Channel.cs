using System;
using System.Linq;
using System.Threading.Tasks;
using Driscod.Gateway;
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
        Category = 4,
    }

    public class Channel : DiscordObject, IMessageable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static readonly ChannelType[] UnmessagableChannelTypes = new[] { ChannelType.Voice, ChannelType.Category };

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
            if (UnmessagableChannelTypes.Contains(ChannelType))
            {
                throw new InvalidOperationException($"Cannot send message to channel type '{ChannelType}'.");
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

        public Voice ConnectVoice()
        {
            if (ChannelType != ChannelType.Voice)
            {
                throw new InvalidOperationException($"Cannot connect voice to channel of type '{ChannelType}'.");
            }

            Action sendAction = () =>
            {
                DiscoveredOnShard.Send((int)Shard.MessageType.VoiceStateUpdate, new BsonDocument
                {
                    { "guild_id", Guild.Id },
                    { "channel_id", Id },
                    { "self_mute", false },
                    { "self_deaf", false },
                });
            };

            BsonDocument stateData = null;
            BsonDocument serverData = null;

            Task.WhenAll(
                Task.Run(() =>
                {
                    stateData = DiscoveredOnShard.WaitForEvent<BsonDocument>(
                        (int)Shard.MessageType.Dispatch,
                        "VOICE_STATE_UPDATE",
                        listenerCreateCallback: sendAction,
                        validator: data =>
                        {
                            return data["guild_id"].AsString == Guild.Id && data["channel_id"] == Id && data["user_id"].AsString == Bot.User.Id;
                        },
                        timeout: TimeSpan.FromSeconds(10));
                }),
                Task.Run(() =>
                {
                    serverData = DiscoveredOnShard.WaitForEvent<BsonDocument>(
                        (int)Shard.MessageType.Dispatch,
                        "VOICE_SERVER_UPDATE",
                        listenerCreateCallback: sendAction,
                        validator: data =>
                        {
                            return data["guild_id"].AsString == Guild.Id;
                        },
                        timeout: TimeSpan.FromSeconds(10));
                })).Wait();

            var voice = new Voice(Connectivity.FormatVoiceSocketEndpoint(serverData["endpoint"].AsString), Guild.Id, Bot.User.Id, stateData["session_id"].AsString, serverData["token"].AsString);
            voice.Start();

            return voice;
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
                    case 4:
                        ChannelType = ChannelType.Category; break;
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
