using System.Collections.Generic;

namespace Driscod.Tracking.Objects
{
    public interface IMessageable
    {
        void SendMessage(MessageEmbed embed);

        void SendMessage(IMessageAttachment file);

        void SendMessage(string message, MessageEmbed embed = null, IEnumerable<IMessageAttachment> attachments = null);
    }
}
