using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects
{
    public class VoiceState : DiscordObject
    {
        private string _guildId;

        private string _channelId;

        private string _userId;

        public Guild Guild => Bot.GetObject<Guild>(_guildId);

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public User User => Bot.GetObject<User>(_userId);

        internal override void UpdateFromDocument(JObject doc)
        {
            _guildId = doc["guild_id"].ToObject<string>();
            _channelId = doc["channel_id"].ToObject<string>();

            if (doc.ContainsKey("member"))
            {
                _userId = doc["member"]["user"]["id"].ToObject<string>();
            }
            else
            {
                _userId = doc["user_id"].ToObject<string>();
            }
        }
    }
}
