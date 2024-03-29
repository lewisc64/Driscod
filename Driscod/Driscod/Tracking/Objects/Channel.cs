﻿using Driscod.Network;
using Driscod.Tracking.Voice;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects;

public enum ChannelType
{
    Text = 0,
    User = 1,
    Voice = 2,
    Category = 4,
}

public class Channel : DiscordObject, IMessageable, IMentionable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly ChannelType[] UnmessagableChannelTypes = new[] { ChannelType.Category };

    private string? _guildId;

    public Guild? Guild => _guildId is null ? null : Bot.GetObject<Guild>(_guildId);

    public bool IsDm => ChannelType == ChannelType.User;

    public ChannelType ChannelType { get; private set; }

    public string? Topic { get; private set; }

    public int Position { get; private set; }

    public JToken? PermissionOverwrites { get; private set; } // TODO

    public string? Name { get; private set; }

    public int Bitrate { get; private set; }

    public async IAsyncEnumerable<Message> GetMessages()
    {
        string? before = null;
        JArray? messages = null;

        while (messages == null || messages.Any())
        {
            messages = await Bot.SendJson<JArray>(
                HttpMethod.Get,
                Connectivity.ChannelMessagesPathFormat,
                new[] { Id },
                queryParams: before is null ? null : new Dictionary<string, string>() { { "before", before } });

            if (messages is null)
            {
                throw new InvalidOperationException("Received invalid messages response.");
            }

            foreach (var doc in messages)
            {
                var message = Create<Message>(Bot, doc!.ToObject<JObject>()!, DiscoveredOnShard);
                yield return message;
                before = message.Id;
            }
        }
    }

    public async Task SendMessage(MessageEmbed embed)
    {
        await SendMessage(null, embed);
    }

    public async Task SendMessage(IMessageAttachment file)
    {
        await SendMessage(null, attachments: new[] { file });
    }

    public async Task SendMessage(string? message, MessageEmbed? embed = null, IEnumerable<IMessageAttachment>? attachments = null)
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

        await Bot.SendJson(HttpMethod.Post, Connectivity.ChannelMessagesPathFormat, new[] { Id }, doc: body, attachments: attachments);
    }

    public async Task<VoiceConnection> ConnectVoice()
    {
        if (Guild is null)
        {
            throw new InvalidOperationException("Cannot connect voice to a channel that isn't in a guild.");
        }
        return await Guild.ConnectVoice(this);
    }

    public string CreateMention()
    {
        if (ChannelType != ChannelType.Text)
        {
            throw new InvalidOperationException("Only text channels can be mentioned.");
        }
        return $"<#{Id}>";
    }

    internal override void UpdateFromDocument(JObject doc)
    {
        Id = doc!["id"]!.ToObject<string>()!;

        if (doc.ContainsKey("guild_id"))
        {
            _guildId = doc!["guild_id"]!.ToObject<string>()!;
        }

        if (doc.ContainsKey("type"))
        {
            switch (doc!["type"]!.ToObject<int>())
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
            Topic = doc["topic"]!.ToObject<string>();
        }

        if (doc.ContainsKey("position"))
        {
            Position = doc["position"]!.ToObject<int>();
        }

        if (doc.ContainsKey("permission_overwrites"))
        {
            PermissionOverwrites = doc["permission_overwrites"];
        }

        if (doc.ContainsKey("name"))
        {
            Name = doc["name"]!.ToObject<string>();
        }

        if (doc.ContainsKey("bitrate"))
        {
            Bitrate = doc["bitrate"]!.ToObject<int>();
        }
    }
}
