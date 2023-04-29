using Driscod.Gateway;
using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects;

public interface IDiscordObject
{
    string Id { get; }

    IBot Bot { get; }
}

public abstract class DiscordObject : IDiscordObject
{
    protected DiscordObject()
    {
    }

    public string Id { get; protected set; } = null!;

    public IBot Bot { get; private set; } = null!;

    internal Shard DiscoveredOnShard { get; private set; } = null!;

    internal abstract void UpdateFromDocument(JObject doc);

    internal static T Create<T>(IBot bot, JObject doc, Shard discoveredBy)
        where T : DiscordObject, new()
    {
        var obj = new T();
        obj.Bot = bot;
        obj.DiscoveredOnShard = discoveredBy;
        obj.UpdateFromDocument(doc);
        return obj;
    }
}
