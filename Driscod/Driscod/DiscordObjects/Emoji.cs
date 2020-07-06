using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class Emoji : DiscordObject
    {
        public BsonArray Roles { get; private set; } // TODO

        public bool RequireColons { get; private set; }

        public string Name { get; private set; }

        public bool Managed { get; private set; }

        public bool Available { get; private set; }

        public bool Animated { get; private set; }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;
            Roles = doc["roles"].AsBsonArray;
            Name = doc["name"].AsString;
            Managed = doc["managed"].AsBoolean;
            Available = doc["available"].AsBoolean;
            Animated = doc["animated"].AsBoolean;
        }
    }
}
