using System;
using MongoDB.Bson;

namespace Driscod.DiscordObjects
{
    public class User : DiscordObject, IMessageable
    {
        public string Username { get; private set; }

        public string Discriminator { get; private set; }

        public string Avatar { get; private set; }

        public void SendMessage(string message)
        {
            throw new NotImplementedException();
        }

        internal override void UpdateFromDocument(BsonDocument document)
        {
            Id = document["id"].AsString;
            Username = document["username"].AsString;
            Discriminator = document["discriminator"].AsString;
            Avatar = document.GetValueOrNull("avatar")?.AsString;
        }
    }
}
