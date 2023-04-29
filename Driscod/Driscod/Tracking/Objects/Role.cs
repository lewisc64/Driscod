using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Driscod.Tracking.Objects
{
    public class Role : DiscordObject
    {
        public int Position { get; private set; }

        public int Permissions { get; private set; }

        public string Name { get; private set; } = null!;

        public bool Mentionable { get; private set; }

        public bool Managed { get; private set; }

        public bool Hoist { get; private set; }

        public int Color { get; private set; }

        public Guild Guild => Bot.GetObjects<Guild>().First(x => x.Roles.Any(y => y.Id == Id));

        public IEnumerable<User> Users => Guild.Members.Where(x => x.Roles.Any(x => x.Id == Id)).Select(x => x.User);

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"]!.ToObject<string>()!;
            Position = doc["position"]!.ToObject<int>();
            Permissions = doc["permissions"]!.ToObject<int>();
            Name = doc["name"]!.ToObject<string>()!;
            Mentionable = doc["mentionable"]!.ToObject<bool>();
            Managed = doc["managed"]!.ToObject<bool>();
            Hoist = doc["hoist"]!.ToObject<bool>();
            Color = doc["color"]!.ToObject<int>();
        }
    }
}
