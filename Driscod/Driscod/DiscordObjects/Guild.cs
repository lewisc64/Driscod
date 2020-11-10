using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class Guild : DiscordObject
    {
        private readonly List<string> _emojiIds = new List<string>();

        private readonly List<string> _channelIds = new List<string>();

        private readonly List<string> _memberIds = new List<string>();

        public List<Presence> Presences { get; private set; } = new List<Presence>();

        public List<Role> Roles { get; private set; } = new List<Role>();

        public List<VoiceState> VoiceStates { get; private set; } = new List<VoiceState>();

        public IEnumerable<Emoji> Emojis => _emojiIds.Select(x => Bot.GetObject<Emoji>(x));

        public IEnumerable<User> Members => _memberIds.Select(x => Bot.GetObject<User>(x));

        public IEnumerable<Channel> Channels => _channelIds.Select(x => Bot.GetObject<Channel>(x));

        public IEnumerable<Channel> TextChannels => Channels.Where(x => x.ChannelType == ChannelType.Text).OrderBy(x => x.Position);

        public IEnumerable<Channel> VoiceChannels => Channels.Where(x => x.ChannelType == ChannelType.Voice).OrderBy(x => x.Position);

        public string VanityUrlCode { get; private set; }

        public string ApplicationId { get; private set; }

        public int MemberCount { get; private set; }

        public string Region { get; private set; }

        public int SystemChannelFlags { get; private set; }

        public bool Unavailable { get; private set; }

        public string Icon { get; private set; }

        public int DefaultMessageNotifications { get; private set; }

        public string DiscoverySplash { get; private set; }

        public int PremiumSubscriptionCount { get; private set; }

        internal void UpdatePresence(BsonDocument doc)
        {
            var presence = Presences.FirstOrDefault(x => x.User.Id == doc["user"]["id"].AsString);
            if (presence == null)
            {
                presence = new Presence();
                presence.Bot = Bot;
                Presences.Add(presence);
            }
            presence.UpdateFromDocument(doc);
        }

        internal void UpdateRole(BsonDocument doc)
        {
            var role = Roles.FirstOrDefault(x => x.Id == doc["id"].AsString);
            if (role == null)
            {
                role = new Role();
                role.Bot = Bot;
                Roles.Add(role);
            }
            role.UpdateFromDocument(doc);
        }

        internal void UpdateVoiceState(BsonDocument doc)
        {
            var userId = doc.Contains("member") ? doc["member"]["user"]["id"].AsString : doc["user_id"].AsString;
            var voiceState = VoiceStates.FirstOrDefault(x => x.User.Id == userId);
            if (doc["channel_id"].IsBsonNull)
            {
                VoiceStates.RemoveAll(x => x.User.Id == userId);
                return;
            }
            if (voiceState == null)
            {
                voiceState = new VoiceState();
                voiceState.Bot = Bot;
                VoiceStates.Add(voiceState);
            }
            voiceState.UpdateFromDocument(doc);
        }

        internal void DeleteRole(string roleId)
        {
            lock (Roles)
            {
                Roles.RemoveAll(x => x.Id == roleId);
            }
        }

        internal void UpdateChannel(BsonDocument doc)
        {
            Bot.CreateOrUpdateObject<Channel>(doc, discoveredBy: DiscoveredOnShard);
            if (!_channelIds.Contains(doc["id"].AsString))
            {
                _channelIds.Add(doc["id"].AsString);
            }
        }

        internal void DeleteChannel(string channelId)
        {
            Bot.DeleteObject<Channel>(channelId);
            _channelIds.RemoveAll(x => x == channelId);
        }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;

            if (doc.Contains("members"))
            {
                _memberIds.Clear();
                foreach (var memberDoc in doc["members"].AsBsonArray.Cast<BsonDocument>())
                {
                    _memberIds.Add(memberDoc["user"]["id"].AsString);
                    Bot.CreateOrUpdateObject<User>(memberDoc["user"].AsBsonDocument);
                }
            }

            if (doc.Contains("presences"))
            {
                lock (Presences)
                {
                    var found = new List<string>();
                    foreach (var presenceDoc in doc["presences"].AsBsonArray.Cast<BsonDocument>())
                    {
                        found.Add(presenceDoc["user"]["id"].AsString);
                        UpdatePresence(presenceDoc);
                    }
                    Presences.RemoveAll(x => !found.Contains(x.User.Id));
                }
            }

            if (doc.Contains("roles"))
            {
                lock (Roles)
                {
                    var found = new List<string>();
                    foreach (var roleDoc in doc["roles"].AsBsonArray.Cast<BsonDocument>())
                    {
                        found.Add(roleDoc["id"].AsString);
                        UpdateRole(roleDoc);
                    }
                    Roles.RemoveAll(x => !found.Contains(x.Id));
                }
            }

            if (doc.Contains("emojis"))
            {
                _emojiIds.Clear();
                foreach (var emojiDoc in doc["emojis"].AsBsonArray.Cast<BsonDocument>())
                {
                    Bot.CreateOrUpdateObject<Emoji>(emojiDoc);
                    _emojiIds.Add(emojiDoc["id"].AsString);
                }
            }

            if (doc.Contains("channels"))
            {
                _channelIds.Clear();
                foreach (var channelDoc in doc["channels"].AsBsonArray.Cast<BsonDocument>())
                {
                    channelDoc["guild_id"] = Id;
                    UpdateChannel(channelDoc);
                }
            }

            if (doc.Contains("vanity_url_code"))
            {
                VanityUrlCode = doc.GetValueOrNull("vanity_url_code")?.AsString;
            }

            if (doc.Contains("voice_states"))
            {
                foreach (var voiceStateDoc in doc["voice_states"].AsBsonArray.Cast<BsonDocument>())
                {
                    voiceStateDoc["guild_id"] = Id;
                    UpdateVoiceState(voiceStateDoc);
                }
            }

            if (doc.Contains("application_id"))
            {
                ApplicationId = doc.GetValueOrNull("application_id")?.AsString;
            }

            if (doc.Contains("member_count"))
            {
                MemberCount = doc["member_count"].AsInt32;
            }

            if (doc.Contains("region"))
            {
                Region = doc["region"].AsString;
            }

            if (doc.Contains("system_channel_flags"))
            {
                SystemChannelFlags = doc["system_channel_flags"].AsInt32;
            }

            if (doc.Contains("unavailable"))
            {
                Unavailable = doc["unavailable"].AsBoolean;
            }

            if (doc.Contains("icon"))
            {
                Icon = doc.GetValueOrNull("icon")?.AsString;
            }

            if (doc.Contains("default_message_notifications"))
            {
                DefaultMessageNotifications = doc["default_message_notifications"].AsInt32;
            }
        }
    }
}
