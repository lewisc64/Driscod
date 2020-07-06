using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class Role : DiscordObject
    {
        public int Position { get; private set; }

        public int Permissions { get; private set; }

        public string Name { get; private set; }

        public bool Mentionable { get; private set; }

        public bool Managed { get; private set; }

        public bool Hoist { get; private set; }

        public int Color { get; private set; }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;
            Position = doc["position"].AsInt32;
            Permissions = doc["permissions"].AsInt32;
            Name = doc["name"].AsString;
            Mentionable = doc["mentionable"].AsBoolean;
            Managed = doc["managed"].AsBoolean;
            Hoist = doc["hoist"].AsBoolean;
            Color = doc["color"].AsInt32;
        }
    }
}
