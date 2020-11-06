using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class VoiceState : DiscordObject
    {
        private string _guildId;

        private string _channelId;

        private string _userId;

        public Guild Guild => Bot.GetObject<Guild>(_guildId);

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public User User => Bot.GetObject<User>(_userId);

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            _guildId = doc["guild_id"].AsString;
            _channelId = doc["channel_id"].AsString;

            if (doc.Contains("member"))
            {
                _userId = doc["member"]["user"]["id"].AsString;
            }
            else
            {
                _userId = doc["user_id"].AsString;
            }
        }
    }
}
