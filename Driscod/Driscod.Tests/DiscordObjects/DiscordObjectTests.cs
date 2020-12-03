using Driscod.DiscordObjects;
using Moq;
using NUnit.Framework;

namespace Driscod.Tests
{
    public class DiscordObjectTests
    {
        [Test]
        public void DiscordObject_Flow_ChannelToGuild()
        {
            var mock = new Mock<IBot>();

            var guild = new Guild
            {
                Id = "target",
            };

            mock.Setup(x => x.GetObject<Guild>(It.IsAny<string>())).Returns(guild);

            var bot = mock.Object;

            var channel = new Channel
            {
                Bot = bot,
            };
            channel.GetType()
                .GetField("_guildId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(channel, "target");

            Assert.AreEqual("target", channel.Guild.Id);
        }

        [Test]
        public void DiscordObject_Flow_Message()
        {
            var mock = new Mock<IBot>();

            var channel = new Channel
            {
                Id = "target_channel",
            };

            var author = new User
            {
                Id = "target_user",
            };

            mock.Setup(x => x.GetObject<Channel>(It.IsAny<string>())).Returns(channel);
            mock.Setup(x => x.GetObject<User>(It.IsAny<string>())).Returns(author);

            var bot = mock.Object;

            var message = new Message
            {
                Bot = bot,
            };
            message.GetType()
                .GetField("_channelId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(message, "target_channel");
            message.GetType()
                .GetField("_authorId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(message, "target_user");

            Assert.AreEqual("target_channel", message.Channel.Id);
            Assert.AreEqual("target_user", message.Author.Id);
        }
    }
}
