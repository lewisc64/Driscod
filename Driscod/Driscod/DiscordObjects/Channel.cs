using Driscod.Audio;
using Driscod.Extensions;
using Driscod.Gateway;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.DiscordObjects
{
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

        public IEnumerable<Message> Messages
        {
            get
            {
                string before = null;
                BsonArray messages = null;

                while (messages == null || messages.Any())
                {
                    messages = Bot.SendJson(
                        HttpMethod.Get,
                        Connectivity.ChannelMessagesPathFormat,
                        new[] { Id },
                        queryParams: new Dictionary<string, string>() { { "before", before } }).AsBsonArray;

                    foreach (var doc in messages.Select(x => x.AsBsonDocument))
                    {
                        var message = new Message
                        {
                            DiscoveredOnShard = DiscoveredOnShard,
                            Bot = Bot,
                        };
                        message.UpdateFromDocument(doc);

                        yield return message;
                        before = message.Id;
                    }
                }
            }
        }

        public void SendMessage(string message)
        {
            SendMessage(message, null);
        }

        public void SendMessage(MessageEmbed embed)
        {
            SendMessage(null, embed);
        }

        public void SendMessage(string message, MessageEmbed embed)
        {
            if (message == null && embed == null)
            {
                throw new ArgumentException("Both parmeters are null. At least one must be set.");
            }

            if (UnmessagableChannelTypes.Contains(ChannelType))
            {
                throw new InvalidOperationException($"Cannot send message to channel type '{ChannelType}'.");
            }

            if (message == string.Empty)
            {
                throw new ArgumentOutOfRangeException(nameof(message), "Message must be non-empty.");
            }

            if (message?.Length > 2000)
            {
                throw new ArgumentOutOfRangeException(nameof(message), "Message must be less than or equal to 2000 characters.");
            }

            var body = new BsonDocument();

            if (message != null)
            {
                body["content"] = message;
            }

            if (embed != null)
            {
                body["embed"] = embed.ToBsonDocument();
            }

            Bot.SendJson(HttpMethod.Post, Connectivity.ChannelMessagesPathFormat, new[] { Id }, body);
        }

        public VoiceConnection ConnectVoice()
        {
            if (ChannelType != ChannelType.Voice)
            {
                throw new InvalidOperationException($"Cannot connect voice to channel of type '{ChannelType}'.");
            }

            lock (Guild.VoiceLock)
            {
                if (Guild.VoiceConnection != null && Guild.VoiceConnection.Stale)
                {
                    Guild.VoiceConnection.Disconnect();
                    Guild.VoiceConnection = null;
                }

                if (Guild.VoiceConnection != null)
                {
                    throw new InvalidOperationException("Already connected to voice for this guild.");
                }

                var callCount = 0;
                Action sendAction = async () =>
                {
                    if (++callCount >= 2)
                    {
                        await DiscoveredOnShard.Send((int)Shard.MessageType.VoiceStateUpdate, new BsonDocument
                        {
                            { "guild_id", Guild.Id },
                            { "channel_id", Id },
                            { "self_mute", false },
                            { "self_deaf", false },
                        });
                    }
                };

                BsonDocument stateData = null;
                BsonDocument serverData = null;

                Task.WhenAll(
                    Task.Run(async () =>
                    {
                        stateData = await DiscoveredOnShard.ListenForEvent<BsonDocument>(
                            (int)Shard.MessageType.Dispatch,
                            "VOICE_STATE_UPDATE",
                            listenerCreateCallback: sendAction,
                            validator: data =>
                            {
                                return data["guild_id"].AsString == Guild.Id && data["channel_id"] == Id && data["user_id"].AsString == Bot.User.Id;
                            },
                            timeout: TimeSpan.FromSeconds(10));
                    }),
                    Task.Run(async () =>
                    {
                        serverData = await DiscoveredOnShard.ListenForEvent<BsonDocument>(
                            (int)Shard.MessageType.Dispatch,
                            "VOICE_SERVER_UPDATE",
                            listenerCreateCallback: sendAction,
                            validator: data =>
                            {
                                return data["guild_id"].AsString == Guild.Id;
                            },
                            timeout: TimeSpan.FromSeconds(10));
                    })).Wait(TimeSpan.FromSeconds(10));

                var voiceGateway = new Voice(
                    DiscoveredOnShard,
                    Connectivity.FormatVoiceSocketEndpoint(serverData["endpoint"].AsString),
                    Guild.Id,
                    Bot.User.Id,
                    stateData["session_id"].AsString,
                    serverData["token"].AsString);

                Guild.VoiceConnection = new VoiceConnection(this, voiceGateway);
            }

            return Guild.VoiceConnection;
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
