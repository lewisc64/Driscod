using Newtonsoft.Json.Linq;

namespace Driscod.Tracking.Objects
{
    public class Emoji : DiscordObject
    {
        public JToken Roles { get; private set; } // TODO

        public bool RequireColons { get; private set; }

        public string Name { get; private set; }

        public bool Managed { get; private set; }

        public bool Available { get; private set; }

        public bool Animated { get; private set; }

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"].ToObject<string>();
            Roles = doc["roles"];
            Name = doc["name"].ToObject<string>();
            Managed = doc["managed"].ToObject<bool>();
            Available = doc["available"].ToObject<bool>();
            Animated = doc["animated"].ToObject<bool>();
        }
    }
}
