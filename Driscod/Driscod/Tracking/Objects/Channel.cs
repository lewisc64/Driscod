using Driscod.Gateway;
using Driscod.Gateway.Consts;
using Driscod.Network;
using Driscod.Tracking.Voice;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects
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

        public JToken PermissionOverwrites { get; private set; } // TODO

        public string Name { get; private set; }

        public int Bitrate { get; private set; }

        public IEnumerable<Message> Messages
        {
            get
            {
                string before = null;
                JArray messages = null;

                while (messages == null || messages.Any())
                {
                    messages = Bot.SendJson<JArray>(
                        HttpMethod.Get,
                        Connectivity.ChannelMessagesPathFormat,
                        new[] { Id },
                        queryParams: new Dictionary<string, string>() { { "before", before } });

                    foreach (var doc in messages)
                    {
                        var message = new Message
                        {
                            DiscoveredOnShard = DiscoveredOnShard,
                            Bot = Bot,
                        };
                        message.UpdateFromDocument(doc.ToObject<JObject>());

                        yield return message;
                        before = message.Id;
                    }
                }
            }
        }

        public void SendMessage(MessageEmbed embed)
        {
            SendMessage(null, embed);
        }

        public void SendMessage(IMessageAttachment file)
        {
            SendMessage(null, attachments: new[] { file });
        }

        public void SendMessage(string message, MessageEmbed embed = null, IEnumerable<IMessageAttachment> attachments = null)
        {
            if (message == null && embed == null && attachments == null)
            {
                throw new ArgumentException("All parmeters are null. At least one must be set.");
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

            var body = new JObject();

            if (message != null)
            {
                body["content"] = message;
            }

            if (embed != null)
            {
                body["embed"] = JObject.FromObject(embed);
            }

            Bot.SendJson(HttpMethod.Post, Connectivity.ChannelMessagesPathFormat, new[] { Id }, doc: body, attachments: attachments);
        }

        public VoiceConnection ConnectVoice()
        {
            if (ChannelType != ChannelType.Voice)
            {
                throw new InvalidOperationException($"Cannot connect voice to channel of type '{ChannelType}'.");
            }

            lock (Guild.VoiceLock)
            {
                string oldSessionId = null;

                if (Guild.VoiceConnection != null && Guild.VoiceConnection.Stale)
                {
                    oldSessionId = Guild.VoiceConnection.VoiceSessionId;
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
                        await DiscoveredOnShard.Send((int)Shard.MessageType.VoiceStateUpdate, new JObject
                        {
                            { "guild_id", Guild.Id },
                            { "channel_id", Id },
                            { "self_mute", false },
                            { "self_deaf", false },
                        });
                    }
                };

                JObject stateData = null;
                JObject serverData = null;

                try
                {
                    Task.WhenAll(
                        Task.Run(async () =>
                        {
                            stateData = await DiscoveredOnShard.ListenForEvent<JObject>(
                                (int)Shard.MessageType.Dispatch,
                                EventNames.VoiceStateUpdate,
                                listenerCreateCallback: sendAction,
                                validator: data =>
                                {
                                    return data["guild_id"].ToObject<string>() == Guild.Id && data["channel_id"].ToObject<string>() == Id && data["user_id"].ToObject<string>() == Bot.User.Id;
                                },
                                timeout: TimeSpan.FromSeconds(10));
                        }),
                        Task.Run(async () =>
                        {
                            serverData = await DiscoveredOnShard.ListenForEvent<JObject>(
                                (int)Shard.MessageType.Dispatch,
                                EventNames.VoiceServerUpdate,
                                listenerCreateCallback: sendAction,
                                validator: data =>
                                {
                                    return data["guild_id"].ToObject<string>() == Guild.Id;
                                },
                                timeout: TimeSpan.FromSeconds(10));
                        })).Wait(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException ex)
                {
                    Logger.Warn(ex, "Timed out while fetching voice data.");
                }

                var voiceGateway = new VoiceGateway(
                        DiscoveredOnShard,
                        Connectivity.FormatVoiceSocketEndpoint(serverData["endpoint"].ToObject<string>()),
                        Guild.Id,
                        Bot.User.Id,
                        (stateData?["session_id"]?.ToObject<string>() ?? oldSessionId) ?? throw new InvalidOperationException("Failed to get session ID."),
                        serverData?["token"]?.ToObject<string>() ?? throw new InvalidOperationException("Failed to get token."));

                Guild.VoiceConnection = new VoiceConnection(this, voiceGateway);
            }

            return Guild.VoiceConnection;
        }

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"].ToObject<string>();

            if (doc.ContainsKey("guild_id"))
            {
                _guildId = doc["guild_id"].ToObject<string>();
            }

            if (doc.ContainsKey("type"))
            {
                switch (doc["type"].ToObject<int>())
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

            if (doc.ContainsKey("topic"))
            {
                Topic = doc["topic"].ToObject<string>();
            }

            if (doc.ContainsKey("position"))
            {
                Position = doc["position"].ToObject<int>();
            }

            if (doc.ContainsKey("permission_overwrites"))
            {
                PermissionOverwrites = doc["permission_overwrites"];
            }

            if (doc.ContainsKey("name"))
            {
                Name = doc["name"].ToObject<string>();
            }

            if (doc.ContainsKey("bitrate"))
            {
                Bitrate = doc["bitrate"].ToObject<int>();
            }
        }
    }
}
