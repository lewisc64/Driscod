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

        public static T Create<T>(Bot bot, BsonDocument doc, Shard discoveredBy = null)
            where T : DiscordObject, new()
        {
            var obj = new T();
            obj.Bot = bot;
            obj.DiscoveredOnShard = discoveredBy;
            obj.UpdateFromDocument(doc);
            return obj;
        }
    }
}
