using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects
{
    public enum PresenceStatus
    {
        Online,
        Offline,
        Idle,
        DoNotDisturb,
    }

    public class Presence : DiscordObject
    {
        private string? _userId;

        public User User => Bot.GetObject<User>(_userId!)!;

        public PresenceStatus Status { get; private set; }

        internal override void UpdateFromDocument(JObject doc)
        {
            _userId = doc["user"]!["id"]!.ToObject<string>()!;

            if (doc.ContainsKey("status") && doc["status"]!.Type != JTokenType.Null)
            {
                switch (doc["status"]!.ToObject<string>())
                {
                    case "online":
                        Status = PresenceStatus.Online; break;
                    case "idle":
                        Status = PresenceStatus.Idle; break;
                    case "dnd":
                        Status = PresenceStatus.DoNotDisturb; break;
                    default:
                        Status = PresenceStatus.Offline; break;
                }
            }
        }
    }
}
