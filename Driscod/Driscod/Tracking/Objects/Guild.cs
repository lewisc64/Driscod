using Driscod.Tracking.Voice;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Driscod.Tracking.Objects
{
    public class Guild : DiscordObject
    {
        private readonly List<string> _emojiIds = new List<string>();

        private readonly List<string> _channelIds = new List<string>();

        private readonly List<string> _memberIds = new List<string>();

        internal object VoiceLock { get; } = new object();

        public List<Presence> Presences { get; private set; } = new List<Presence>();

        public List<Role> Roles { get; private set; } = new List<Role>();

        public List<VoiceState> VoiceStates { get; private set; } = new List<VoiceState>();

        public IEnumerable<Emoji> Emojis => _emojiIds.Select(x => Bot.GetObject<Emoji>(x));

        public IEnumerable<User> Members => _memberIds.Select(x => Bot.GetObject<User>(x));

        public IEnumerable<Channel> Channels => _channelIds.Select(x => Bot.GetObject<Channel>(x));

        public VoiceConnection VoiceConnection { get; set; }

        public bool HasActiveVoiceConnection => VoiceConnection != null && !VoiceConnection.Stale;

        public IEnumerable<Channel> TextChannels => Channels.Where(x => x.ChannelType == ChannelType.Text).OrderBy(x => x.Position);

        public IEnumerable<Channel> VoiceChannels => Channels.Where(x => x.ChannelType == ChannelType.Voice).OrderBy(x => x.Position);

        public string Name { get; private set; }

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

        internal void UpdatePresence(JObject doc)
        {
            var presence = Presences.FirstOrDefault(x => x.User.Id == doc["user"]["id"].ToObject<string>());
            if (presence == null)
            {
                presence = new Presence();
                presence.Bot = Bot;
                Presences.Add(presence);
            }
            presence.UpdateFromDocument(doc);
        }

        internal void UpdateRole(JObject doc)
        {
            var role = Roles.FirstOrDefault(x => x.Id == doc["id"].ToObject<string>());
            if (role == null)
            {
                Roles.Add(Create<Role>(Bot, doc));
            }
            else
            {
                role.UpdateFromDocument(doc);
            }
        }

        internal void UpdateVoiceState(JObject doc)
        {
            var userId = doc.ContainsKey("member") ? doc["member"]["user"]["id"].ToObject<string>() : doc["user_id"].ToObject<string>();
            var voiceState = VoiceStates.FirstOrDefault(x => x.User.Id == userId);
            if (doc["channel_id"].Type == JTokenType.Null)
            {
                VoiceStates.RemoveAll(x => x.User.Id == userId);
                return;
            }
            if (voiceState == null)
            {
                VoiceStates.Add(Create<VoiceState>(Bot, doc));
            }
            else
            {
                voiceState.UpdateFromDocument(doc);
            }
        }

        internal void DeleteRole(string roleId)
        {
            lock (Roles)
            {
                Roles.RemoveAll(x => x.Id == roleId);
            }
        }

        internal void UpdateChannel(JObject doc)
        {
            Bot.CreateOrUpdateObject<Channel>(doc, discoveredBy: DiscoveredOnShard);
            if (!_channelIds.Contains(doc["id"].ToObject<string>()))
            {
                _channelIds.Add(doc["id"].ToObject<string>());
            }
        }

        internal void DeleteChannel(string channelId)
        {
            Bot.DeleteObject<Channel>(channelId);
            _channelIds.RemoveAll(x => x == channelId);
        }

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"].ToObject<string>();

            if (doc.ContainsKey("name"))
            {
                Name = doc["name"].ToObject<string>();
            }

            if (doc.ContainsKey("members"))
            {
                _memberIds.Clear();
                foreach (var memberDoc in doc["members"].Cast<JObject>())
                {
                    _memberIds.Add(memberDoc["user"]["id"].ToObject<string>());
                    Bot.CreateOrUpdateObject<User>(memberDoc["user"].ToObject<JObject>());
                }
            }

            if (doc.ContainsKey("presences"))
            {
                lock (Presences)
                {
                    var found = new List<string>();
                    foreach (var presenceDoc in doc["presences"].Cast<JObject>())
                    {
                        found.Add(presenceDoc["user"]["id"].ToObject<string>());
                        UpdatePresence(presenceDoc);
                    }
                    Presences.RemoveAll(x => !found.Contains(x.User.Id));
                }
            }

            if (doc.ContainsKey("roles"))
            {
                lock (Roles)
                {
                    var found = new List<string>();
                    foreach (var roleDoc in doc["roles"].Cast<JObject>())
                    {
                        found.Add(roleDoc["id"].ToObject<string>());
                        UpdateRole(roleDoc);
                    }
                    Roles.RemoveAll(x => !found.Contains(x.Id));
                }
            }

            if (doc.ContainsKey("emojis"))
            {
                _emojiIds.Clear();
                foreach (var emojiDoc in doc["emojis"].Cast<JObject>())
                {
                    Bot.CreateOrUpdateObject<Emoji>(emojiDoc);
                    _emojiIds.Add(emojiDoc["id"].ToObject<string>());
                }
            }

            if (doc.ContainsKey("channels"))
            {
                _channelIds.Clear();
                foreach (var channelDoc in doc["channels"].Cast<JObject>())
                {
                    channelDoc["guild_id"] = Id;
                    UpdateChannel(channelDoc);
                }
            }

            if (doc.ContainsKey("vanity_url_code"))
            {
                VanityUrlCode = doc.ContainsKey("vanity_url_code") ? doc["vanity_url_code"].ToObject<string>() : null;
            }

            if (doc.ContainsKey("voice_states"))
            {
                foreach (var voiceStateDoc in doc["voice_states"].Cast<JObject>())
                {
                    voiceStateDoc["guild_id"] = Id;
                    UpdateVoiceState(voiceStateDoc);
                }
            }

            if (doc.ContainsKey("application_id"))
            {
                ApplicationId = doc.ContainsKey("application_id") ? doc["application_id"].ToObject<string>() : null;
            }

            if (doc.ContainsKey("member_count"))
            {
                MemberCount = doc["member_count"].ToObject<int>();
            }

            if (doc.ContainsKey("region"))
            {
                Region = doc["region"].ToObject<string>();
            }

            if (doc.ContainsKey("system_channel_flags"))
            {
                SystemChannelFlags = doc["system_channel_flags"].ToObject<int>();
            }

            if (doc.ContainsKey("unavailable"))
            {
                Unavailable = doc["unavailable"].ToObject<bool>();
            }

            if (doc.ContainsKey("icon"))
            {
                Icon = doc.ContainsKey("icon") ? doc["icon"].ToObject<string>() : null;
            }

            if (doc.ContainsKey("default_message_notifications"))
            {
                DefaultMessageNotifications = doc["default_message_notifications"].ToObject<int>();
            }
        }
    }
}
