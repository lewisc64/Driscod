using System.Collections.Generic;
using System.Threading.Tasks;

namespace Driscod.Tracking.Objects;

public interface IMessageable
{
    Task SendMessage(MessageEmbed embed);

    Task SendMessage(IMessageAttachment file);

    Task SendMessage(string? message, MessageEmbed? embed = null, IEnumerable<IMessageAttachment>? attachments = null);
}
