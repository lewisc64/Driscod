using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Driscod
{
    public abstract class CommandBotWrapper
    {
        protected IBot Bot { get; private set; }

        public string CommandPrefix { get; set; } = "!";

        public bool IgnoreSelf { get; set; } = true;

        public bool IgnorePrefixInDms { get; set; } = true;

        protected CommandBotWrapper(IBot bot)
        {
            Bot = bot;

            SetupListeners();
        }

        private void SetupListeners()
        {
            Bot.OnMessage += (_, message) =>
            {
                if (IgnoreSelf && message.Author == Bot.User)
                {
                    return;
                }

                var methods = GetType()
                    .GetMethods();

                foreach (var method in methods.Where(x => x.CustomAttributes.Any(x => x.AttributeType == typeof(OnMessageAttribute))))
                {
                    method.Invoke(this, new[] { message });
                }

                foreach (var method in methods.Where(x => x.CustomAttributes.Any(x => x.AttributeType == typeof(CommandAttribute))))
                {
                    var triggers = method.CustomAttributes
                        .Select(x => (string)x.ConstructorArguments.First().Value)
                        .OrderByDescending(x => x.Length);

                    foreach (var trigger in triggers)
                    {
                        var ignorePrefix = IgnorePrefixInDms && message.Channel.IsDm;
                        var regex = @$"^(?:{Regex.Escape(CommandPrefix)}){(ignorePrefix ? "?" : string.Empty)}{Regex.Escape(trigger)}(?:\s+|$)";
                        if (Regex.IsMatch(message.Content, regex))
                        {
                            var commandArgs = Regex.Split(message.Content, @"\s+")
                                .Skip(1)
                                .ToArray();

                            method.Invoke(this, new object[] { message, commandArgs });
                            break;
                        }
                    }
                }
            };
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class OnMessageAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
        public class CommandAttribute : Attribute
        {
            public string TriggerName { get; set; }

            public CommandAttribute(string triggerName)
            {
                TriggerName = triggerName;
            }
        }
    }
}
