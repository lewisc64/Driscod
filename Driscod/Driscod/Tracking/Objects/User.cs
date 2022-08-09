using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects
{
    public class User : DiscordObject, IMessageable, IMentionable
    {
        public IEnumerable<Presence> Presences => Bot.Guilds
            .Where(x => x.Members.Select(x => x.User).Contains(this))
            .Select(x => x.Presences.FirstOrDefault(x => x.User == this));

        public Presence Presence => Presences.FirstOrDefault();

        public string Username { get; private set; }

        public string Discriminator { get; private set; }

        public string Avatar { get; private set; }

        public bool IsBot { get; private set; }

        public async Task<Channel> GetDmChannel()
        {
            var result = await Bot.SendJson(HttpMethod.Post, @"users/{0}/channels", new[] { Bot.User.Id }, new JObject { { "recipient_id", Id } });

            Bot.CreateOrUpdateObject<Channel>(result);
            return Bot.GetObject<Channel>(result["id"].ToObject<string>());
        }

        public async Task SendMessage(MessageEmbed embed)
        {
            await SendMessage(null, embed);
        }

        public async Task SendMessage(IMessageAttachment file)
        {
            await SendMessage(null, attachments: new[] { file });
        }

        public async Task SendMessage(string message, MessageEmbed embed = null, IEnumerable<IMessageAttachment> attachments = null)
        {
            await (await GetDmChannel()).SendMessage(message, embed: embed, attachments: attachments);
        }

        public string CreateMention()
        {
            return $"<@{Id}>";
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
            IsBot = doc.ContainsKey("bot") && doc["bot"].ToObject<bool>();
        }
    }
}
