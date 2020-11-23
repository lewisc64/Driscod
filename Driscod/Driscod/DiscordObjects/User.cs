using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class User : DiscordObject, IMessageable
    {
        public IEnumerable<Presence> Presences => Bot.Guilds
            .Where(x => x.Members.Contains(this))
            .Select(x => x.Presences.FirstOrDefault(x => x.User == this));

        public Presence Presence => Presences.FirstOrDefault();

        public string Username { get; private set; }

        public string Discriminator { get; private set; }

        public string Avatar { get; private set; }

        public Channel DmChannel
        {
            get
            {
                var result = Bot.SendJson(HttpMethod.Post, @"users/{0}/channels", new[] { Bot.User.Id }, new BsonDocument { { "recipient_id", Id } });

                Bot.CreateOrUpdateObject<Channel>(result.AsBsonDocument);
                return Bot.GetObject<Channel>(result["id"].AsString);
            }
        }

        public void SendMessage(string message)
        {
            SendMessage(message, null);
        }

        public void SendMessage(MessageEmbed embed)
        {
            SendMessage(null, embed);
        }

        public void SendMessage(string message, MessageEmbed embed)
        {
            DmChannel.SendMessage(message: message, embed: embed);
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
