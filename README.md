# Driscod
 C# Discord API library.

## Examples

### Approaches

These two approaches are identical in function.

#### CommandBotWrapper

```cs
public class TestBotWrapper : CommandBotWrapper
{
    public TestBot(Bot bot)
        : base(bot)
    {
    }

    [Command("ping")]
    public void Ping(Message message, string[] args)
    {
        message.Channel.SendMessage("pong");
    }
}

...

var bot = new Bot(TOKEN);
bot.Start();

var testBot = new TestBotWrapper(bot);
```

#### Event Handlers

```cs
var bot = new Bot(TOKEN);
bot.Start();

bot.OnMessage += (_, message) =>
{
    if (message.Author != bot.User && message.Content == "!ping")
    {
        message.Channel.SendMessage("pong");
    }
};
```

### Play Music

Joins the voice channel the user is in, and plays an audio file.

```cs
var bot = new Bot(TOKEN);
bot.Start();

Bot.OnMessage += (_, message) =>
{
    if (message.Content == "play me that tune")
    {
        var channel = message.Channel.Guild.VoiceStates.First(x => x.User == message.Author).Channel;
        using (var connection = channel.ConnectVoice())
        {
            connection.PlaySync(new AudioFile(FILE_PATH));
        }
    }
};
```

### Play Music Alt.

The connection does not have to be used immediately.

```cs
var bot = new Bot(TOKEN);
bot.Start();

Bot.OnMessage += (_, message) =>
{
    if (message.Content == "join")
    {
        var channel = message.Channel.Guild.VoiceStates.First(x => x.User == message.Author).Channel;
        channel.ConnectVoice();
    }
};

Bot.OnMessage += (_, message) =>
{
    if (message.Content == "play")
    {
        message.Channel.Guild.VoiceConnection.PlaySync(new YoutubeVideo(VIDEO_ID));
    }
};
```
