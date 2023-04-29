using Driscod.Network;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects;

public class GuildMember : DiscordObject
{
    private readonly List<string> _roleIds = new List<string>();
    private string? _guildId;
    private string? _userId;

    public IEnumerable<Role> Roles => Bot.GetObject<Guild>(_guildId!)!.Roles.Where(x => _roleIds.Contains(x.Id));

    public User User => Bot.GetObject<User>(_userId!)!;

    public Guild Guild => Bot.GetObject<Guild>(_guildId!)!;

    public string? Nickname { get; private set; }

    public DateTime JoinedAt { get; private set; }

    public async Task AssignRole(Role role)
    {
        if (Roles.Contains(role))
        {
            throw new ArgumentException("Guild member already has that role", nameof(role));
        }

        await Bot.SendJson(HttpMethod.Put, Connectivity.GuildMemberRolePathFormat, new[] { _guildId!, _userId!, role.Id });
    }

    public async Task RemoveRole(Role role)
    {
        if (!Roles.Contains(role))
        {
            throw new ArgumentException("Guild member does not have that role", nameof(role));
        }

        await Bot.SendJson(HttpMethod.Delete, Connectivity.GuildMemberRolePathFormat, new[] { _guildId!, _userId!, role.Id });
    }

    internal override void UpdateFromDocument(JObject doc)
    {
        _guildId = doc["guild_id"]!.ToObject<string>()!;

        _userId = doc["user"]!["id"]!.ToObject<string>()!;
        Id = _userId;

        if (doc.ContainsKey("roles"))
        {
            _roleIds.Clear();
            _roleIds.AddRange(doc["roles"]!.Cast<JValue>().Select(x => x.ToObject<string>())!);
        }

        if (doc.ContainsKey("nick"))
        {
            Nickname = doc["nick"]!.ToObject<string>();
        }

        if (doc.ContainsKey("joined_at"))
        {
            JoinedAt = doc["joined_at"]!.ToObject<DateTime>();
        }

        Bot.CreateOrUpdateObject<User>(doc["user"]!.ToObject<JObject>()!, DiscoveredOnShard);
    }
}
