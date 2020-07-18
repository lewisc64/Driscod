# Driscod
 C# Discord API library.

## Example

This example echos any message it recieves.

```cs
var bot = new Bot(TOKEN);
bot.Start();

bot.OnMessage += (_, message) =>
{
    if (message.Author != bot.User)
    {
        message.Channel.SendMessage(message.Content);
    }
};
```
