namespace Driscod.DiscordObjects
{
    public interface IMessageable
    {
        void SendMessage(string message);

        void SendMessage(MessageEmbed embed);

        void SendMessage(string message, MessageEmbed embed);
    }
}
