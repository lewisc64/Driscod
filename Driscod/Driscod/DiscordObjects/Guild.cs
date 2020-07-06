using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class Guild : DiscordObject
    {
        private readonly List<string> _emojiIds = new List<string>();

        private readonly List<string> _channelIds = new List<string>();

        public List<Presence> Presences { get; private set; } = new List<Presence>();

        public List<Role> Roles { get; private set; } = new List<Role>();

        public IEnumerable<Emoji> Emojis => _emojiIds.Select(x => Bot.GetObject<Emoji>(x));

        public IEnumerable<Channel> Channels => _channelIds.Select(x => Bot.GetObject<Channel>(x));

        public IEnumerable<Channel> TextChannels => Channels.Where(x => x.ChannelType == ChannelType.Text).OrderBy(x => x.Position);

        public IEnumerable<Channel> VoiceChannels => Channels.Where(x => x.ChannelType == ChannelType.Voice).OrderBy(x => x.Position);

        public string VanityUrlCode { get; private set; }

        public BsonArray VoiceStates { get; private set; } // TODO

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
            var presence = Presences.FirstOrDefault(x => x.UserId == doc["user"]["id"].AsString);
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

        internal void DeleteRole(string roleId)
        {
            lock (Roles)
            {
                Roles.RemoveAll(x => x.Id == roleId);
            }
        }

        internal void UpdateChannel(BsonDocument doc)
        {
            Bot.CreateOrUpdateObject<Channel>(doc);
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
                foreach (var memberDoc in doc["members"].AsBsonArray.Cast<BsonDocument>())
                {
                    // TODO
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
                    Presences.RemoveAll(x => !found.Contains(x.UserId));
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
                VoiceStates = doc["voice_states"].AsBsonArray;
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
