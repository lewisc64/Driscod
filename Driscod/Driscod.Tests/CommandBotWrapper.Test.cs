using Driscod.DiscordObjects;
using Moq;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Driscod.Tests
{
    public class CommandBotWrapperTests
    {
        [TestCase("test", true, new string[0], true)]
        [TestCase("!test", true, new string[0], true)]
        [TestCase("!test", false, new string[0], true)]
        [TestCase("test", false, new string[0], false)]
        [TestCase("!test 1", false, new[] { "1" }, true)]
        [TestCase("!test 1 2 3 4 5", false, new[] { "1", "2", "3", "4", "5" }, true)]
        public async Task CommandBotWrapper_Test(string messageContent, bool isDm, string[] expectedArguments, bool shouldMatch)
        {
            var botUser = new User();
            var authorUser = new User();

            var channel = new Channel();
            channel.GetType()
                .GetProperty(nameof(channel.ChannelType), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .SetValue(channel, isDm ? ChannelType.User : ChannelType.Text);

            var botMock = new Mock<IBot>();
            botMock.Setup(x => x.User).Returns(botUser);
            botMock.Setup(x => x.GetObject<Channel>(It.IsAny<string>())).Returns(channel);
            botMock.Setup(x => x.GetObject<User>(It.IsAny<string>())).Returns(authorUser);

            var message = new Message
            {
                Bot = botMock.Object,
            };
            message.GetType()
                .GetField("_content", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(message, messageContent);

            var tcs = new TaskCompletionSource<bool>();

            var wrapper = new TestingCommandBotWrapper(botMock.Object, (message, args) =>
            {
                if (shouldMatch)
                {
                    Assert.AreEqual(expectedArguments, args);
                }
                else
                {
                    Assert.Fail("Command should not have matched.");
                }
                tcs.SetResult(true);
            });

            botMock.Raise(m => m.OnMessage += null, new object[] { botMock.Object, message });

            await Task.WhenAny(
                tcs.Task,
                Task.Delay(50));

            if (!tcs.Task.IsCompleted)
            {
                if (shouldMatch)
                {
                    Assert.Fail("Command should have matched.");
                }
                return;
            }
        }


        private class TestingCommandBotWrapper : CommandBotWrapper
        {
            private Action<Message, string[]> _commandCallback;

            public TestingCommandBotWrapper(IBot bot, Action<Message, string[]> commandCallback)
                : base(bot)
            {
                _commandCallback = commandCallback;
            }

            [Command("test")]
            public void Test(Message message, string[] args)
            {
                _commandCallback.Invoke(message, args);
            }
        }
    }
}
