﻿using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Driscod.Tracking.Objects
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
                var result = Bot.SendJson(HttpMethod.Post, @"users/{0}/channels", new[] { Bot.User.Id }, new JObject { { "recipient_id", Id } });

                Bot.CreateOrUpdateObject<Channel>(result);
                return Bot.GetObject<Channel>(result["id"].ToObject<string>());
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

        public override string ToString()
        {
            return $"{Username}:{Discriminator}";
        }

        internal override void UpdateFromDocument(JObject doc)
        {
            Id = doc["id"].ToObject<string>();
            Username = doc["username"].ToObject<string>();
            Discriminator = doc["discriminator"].ToObject<string>();
            Avatar = doc.ContainsKey("avatar") ? doc["avatar"].ToObject<string>() : null;
        }
    }
}
