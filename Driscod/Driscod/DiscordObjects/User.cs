using System.Linq;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class User : DiscordObject, IMessageable
    {
        public Presence Presence => Bot.Guilds.FirstOrDefault(x => x.Members.Any(y => y.Id == Id))?.Presences.FirstOrDefault(x => x.User == this);

        public string Username { get; private set; }

        public string Discriminator { get; private set; }

        public string Avatar { get; private set; }

        public void SendMessage(string message)
        {
            var result = Bot.SendJson(@"users/{0}/channels", new[] { Bot.User.Id }, new BsonDocument { { "recipient_id", Id } });

            Bot.CreateOrUpdateObject<Channel>(result);
            var channel = Bot.GetObject<Channel>(result["id"].AsString);

            channel.SendMessage(message);
        }

        internal override void UpdateFromDocument(BsonDocument doc)
        {
            Id = doc["id"].AsString;
            Username = doc["username"].AsString;
            Discriminator = doc["discriminator"].AsString;
            Avatar = doc.GetValueOrNull("avatar")?.AsString;
        }
    }
}
