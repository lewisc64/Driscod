using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public abstract class DiscordObject
    {
        public string Id { get; set; }

        public Bot Bot { get; set; }

        internal Shard DiscoveredOnShard { get; set; }

        protected DiscordObject()
        {
        }

        internal abstract void UpdateFromDocument(BsonDocument doc);
    }
}
