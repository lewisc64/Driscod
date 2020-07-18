# Driscod
 C# Discord API library.

## Example

This example echos any message it recieves.

```cs
Bot = new Bot(TOKEN);
Bot.Start();

Bot.OnMessage += (_, message) =>
{
    if (message.Author != Bot.User)
    {
        message.Channel.SendMessage(message.Content);
    }
};
```
