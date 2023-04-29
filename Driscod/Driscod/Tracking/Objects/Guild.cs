using Driscod.Tracking.Voice;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects;

public class Guild : DiscordObject
{
    private readonly List<string> _emojiIds = new List<string>();
    private readonly List<string> _channelIds = new List<string>();
    private readonly List<string> _roleIds = new List<string>();
    private readonly List<GuildMember> _members = new List<GuildMember>();
    private readonly List<Presence> _presences = new List<Presence>();
    private readonly List<VoiceState> _voiceStates = new List<VoiceState>();

    private readonly SemaphoreSlim _voiceSemaphore = new(1);
    private VoiceConnection? _voiceConnection = null;

    public IEnumerable<Presence> Presences => _presences.ToArray();

    public IEnumerable<VoiceState> VoiceStates => _voiceStates.ToArray();

    public IEnumerable<Emoji> Emojis => _emojiIds
        .Select(x => Bot.GetObject<Emoji>(x))
        .Where(x => x is not null)
        .Cast<Emoji>();

    public IEnumerable<GuildMember> Members => _members.ToArray();

    public IEnumerable<Channel> Channels => _channelIds
        .Select(x => Bot.GetObject<Channel>(x))
        .Where(x => x is not null)
        .Cast<Channel>();

    public IEnumerable<Role> Roles => _roleIds
        .Select(x => Bot.GetObject<Role>(x))
        .Where(x => x is not null)
        .Cast<Role>();

    public VoiceConnection? VoiceConnection
    {
        get
        {
            if (_voiceConnection is not null && !_voiceConnection.Stale && _voiceConnection.Connected)
            {
                return _voiceConnection;
            }
            return null;
        }
    }

    public IEnumerable<Channel> TextChannels => Channels.Where(x => x.ChannelType == ChannelType.Text).OrderBy(x => x.Position);

    public IEnumerable<Channel> VoiceChannels => Channels.Where(x => x.ChannelType == ChannelType.Voice).OrderBy(x => x.Position);

    public string? Name { get; private set; }

    public string? VanityUrlCode { get; private set; }

    public string? ApplicationId { get; private set; }

    public int SystemChannelFlags { get; private set; }

    public bool Unavailable { get; private set; }

    public string? Icon { get; private set; }

    public int DefaultMessageNotifications { get; private set; }

    public string? DiscoverySplash { get; private set; }

    public int PremiumSubscriptionCount { get; private set; }

    public async Task<VoiceConnection> ConnectVoice(Channel channel)
    {
        if (channel.ChannelType != ChannelType.Voice)
        {
            throw new ArgumentException($"Cannot connect voice to channels of type '{channel.ChannelType}'.", nameof(channel));
        }
        await _voiceSemaphore.WaitAsync();
        try
        {
            await DisconnectVoice();
            _voiceConnection = new VoiceConnection(channel);
            await _voiceConnection.Connect();
            return _voiceConnection;
        }
        finally
        {
            _voiceSemaphore.Release();
        }
    }

    public async Task DisconnectVoice()
    {
        if (_voiceConnection is not null && _voiceConnection.Connected)
        {
            await _voiceConnection.Disconnect();
        }
        _voiceConnection = null;
    }

    internal void UpdatePresence(JObject doc)
    {
        var presence = _presences.FirstOrDefault(x => x.User.Id == (doc["user"]?["id"]?.ToObject<string>() ?? throw new ArgumentException($"The presence update document does not contain a user ID: {doc}")));
        if (presence == null)
        {
            presence = Create<Presence>(Bot, doc, discoveredBy: DiscoveredOnShard);
            _presences.Add(presence);
        }
        else
        {
            presence.UpdateFromDocument(doc);
        }
    }

    internal void UpdateMember(JObject doc)
    {
        var member = _members.FirstOrDefault(x => x.User.Id == doc["user"]!["id"]!.ToObject<string>());
        if (member == null)
        {
            member = Create<GuildMember>(Bot, doc, discoveredBy: DiscoveredOnShard);
            _members.Add(member);
        }
        else
        {
            member.UpdateFromDocument(doc);
        }
    }

    internal void DeleteMember(string userId)
    {
        _members.RemoveAll(x => x.User.Id == userId);
        _presences.RemoveAll(x => x.User.Id == userId);
    }

    internal void UpdateVoiceState(JObject doc)
    {
        var userId = doc.ContainsKey("member") ? doc["member"]!["user"]!["id"]!.ToObject<string>() : doc["user_id"]!.ToObject<string>();
        var voiceState = VoiceStates.FirstOrDefault(x => x.User.Id == userId);
        if (!doc.ContainsKey("channel_id") || doc["channel_id"]!.Type == JTokenType.Null)
        {
            _voiceStates.RemoveAll(x => x.User.Id == userId);
            return;
        }
        if (voiceState == null)
        {
            _voiceStates.Add(Create<VoiceState>(Bot, doc, discoveredBy: DiscoveredOnShard));
        }
        else
        {
            voiceState.UpdateFromDocument(doc);
        }
    }

    internal void UpdateChannel(JObject doc)
    {
        Bot.CreateOrUpdateObject<Channel>(doc, discoveredBy: DiscoveredOnShard);
        if (!_channelIds.Contains(doc["id"]!.ToObject<string>()!))
        {
            _channelIds.Add(doc["id"]!.ToObject<string>()!);
        }
    }

    internal void DeleteChannel(string channelId)
    {
        Bot.DeleteObject<Channel>(channelId);
        _channelIds.RemoveAll(x => x == channelId);
    }

    internal void UpdateRole(JObject doc)
    {
        Bot.CreateOrUpdateObject<Role>(doc, discoveredBy: DiscoveredOnShard);
        if (!_roleIds.Contains(doc["id"]!.ToObject<string>()!))
        {
            _roleIds.Add(doc["id"]!.ToObject<string>()!);
        }
    }

    internal void DeleteRole(string roleId)
    {
        Bot.DeleteObject<Role>(roleId);
        _roleIds.RemoveAll(x => x == roleId);
    }

    internal override void UpdateFromDocument(JObject doc)
    {
        Id = doc["id"]!.ToObject<string>()!;

        if (doc.ContainsKey("name"))
        {
            Name = doc["name"]!.ToObject<string>()!;
        }

        if (doc.ContainsKey("members"))
        {
            var found = new List<string>();
            foreach (var memberDoc in doc["members"]!.Cast<JObject>())
            {
                memberDoc["guild_id"] = Id;
                found.Add(memberDoc["user"]!["id"]!.ToObject<string>()!);
                UpdateMember(memberDoc);
                Bot.CreateOrUpdateObject<User>(memberDoc["user"]!.ToObject<JObject>()!, discoveredBy: DiscoveredOnShard);
            }
            _members.RemoveAll(x => !found.Contains(x.User.Id));
        }

        if (doc.ContainsKey("presences"))
        {
            var found = new List<string>();
            foreach (var presenceDoc in doc["presences"]!.Cast<JObject>())
            {
                found.Add(presenceDoc["user"]!["id"]!.ToObject<string>()!);
                UpdatePresence(presenceDoc);
            }
            _presences.RemoveAll(x => !found.Contains(x.User.Id));
        }

        if (doc.ContainsKey("emojis"))
        {
            _emojiIds.Clear();
            foreach (var emojiDoc in doc["emojis"]!.Cast<JObject>())
            {
                Bot.CreateOrUpdateObject<Emoji>(emojiDoc, DiscoveredOnShard);
                _emojiIds.Add(emojiDoc["id"]!.ToObject<string>()!);
            }
        }

        if (doc.ContainsKey("channels"))
        {
            _channelIds.Clear();
            foreach (var channelDoc in doc["channels"]!.Cast<JObject>())
            {
                channelDoc["guild_id"] = Id;
                UpdateChannel(channelDoc);
            }
        }

        if (doc.ContainsKey("roles"))
        {
            _roleIds.Clear();
            foreach (var roleDoc in doc["roles"]!.Cast<JObject>())
            {
                UpdateRole(roleDoc);
            }
        }

        if (doc.ContainsKey("vanity_url_code"))
        {
            VanityUrlCode = doc.ContainsKey("vanity_url_code") ? doc["vanity_url_code"]!.ToObject<string>() : null;
        }

        if (doc.ContainsKey("voice_states"))
        {
            foreach (var voiceStateDoc in doc["voice_states"]!.Cast<JObject>())
            {
                voiceStateDoc["guild_id"] = Id;
                UpdateVoiceState(voiceStateDoc);
            }
        }

        if (doc.ContainsKey("application_id"))
        {
            ApplicationId = doc.ContainsKey("application_id") ? doc["application_id"]!.ToObject<string>() : null;
        }

        if (doc.ContainsKey("system_channel_flags"))
        {
            SystemChannelFlags = doc["system_channel_flags"]!.ToObject<int>();
        }

        if (doc.ContainsKey("unavailable"))
        {
            Unavailable = doc["unavailable"]!.ToObject<bool>();
        }

        if (doc.ContainsKey("icon"))
        {
            Icon = doc.ContainsKey("icon") ? doc["icon"]!.ToObject<string>() : null;
        }

        if (doc.ContainsKey("default_message_notifications"))
        {
            DefaultMessageNotifications = doc["default_message_notifications"]!.ToObject<int>();
        }
    }
}
