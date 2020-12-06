using Driscod.Gateway;
using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects
{
    public interface IDiscordObject
    {
        string Id { get; set; }

        IBot Bot { get; set; }
    }

    public abstract class DiscordObject : IDiscordObject
    {
        public string Id { get; set; }

        public IBot Bot { get; set; }

        internal Shard DiscoveredOnShard { get; set; }

        protected DiscordObject()
        {
        }

        internal abstract void UpdateFromDocument(JObject doc);

        internal static T Create<T>(IBot bot, JObject doc, Shard discoveredBy = null)
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
