using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    // TODO: the rest of the attributes
    public class Message : DiscordObject
    {
        private string _authorId;

        private string _channelId;

        public User Author => Bot.GetObject<User>(_authorId);

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public string Content { get; private set; }

        internal override void UpdateFromDocument(BsonDocument document)
        {
            _authorId = document["author"]["id"].AsString;
            _channelId = document["channel_id"].AsString;
            Content = document["content"].AsString;
        }
    }
}
