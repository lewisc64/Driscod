using Driscod.Gateway;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public interface IDiscordObject
    {
        string Id { get; set; }

        Bot Bot { get; set; }
    }

    public abstract class DiscordObject : IDiscordObject
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
