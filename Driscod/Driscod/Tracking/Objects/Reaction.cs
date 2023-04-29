using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects;

public class Reaction : DiscordObject, IUntracked
{
    private string? _emojiId;

    public Emoji Emoji => Bot.GetObject<Emoji>(_emojiId!)!;

    public int Count { get; private set; }

    public bool BotUserReacted { get; private set; }

    internal override void UpdateFromDocument(JObject doc)
    {
        _emojiId = doc["emoji"]!["id"]!.ToObject<string>();
        Count = doc["count"]!.ToObject<int>();
        BotUserReacted = doc["me"]!.ToObject<bool>();
    }
}
