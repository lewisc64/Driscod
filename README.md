# Driscod
![CI](https://github.com/lewisc64/Driscod/workflows/CI/badge.svg)

C# Discord API wrapper.

## Examples

### Event Handlers

```cs
using Driscod.Tracking;

...

var bot = new Bot(TOKEN, Intents.All);
await bot.Start();

bot.OnMessage += async (_, message) =>
{
    if (message.Author != bot.User && message.Content == "!ping")
    {
        await message.Channel.SendMessage("pong");
    }
};
```

### Play Music

Joins the voice channel the user is in, and plays an audio file.

```cs
var bot = new Bot(TOKEN, Intents.All);
await bot.Start();

Bot.OnMessage += async (_, message) =>
{
    if (message.Content == "play me that tune")
    {
        var channel = message.Channel.Guild.VoiceStates.First(x => x.User == message.Author).Channel;
        using (var connection = channel.ConnectVoice())
        {
            await connection.PlayAudio(new AudioFile(FILE_PATH));
        }
    }
};
```

### Play Music Alt.

The connection does not have to be used immediately.

```cs
var bot = new Bot(TOKEN, Intents.All);
await bot.Start();

Bot.OnMessage += (_, message) =>
{
    if (message.Content == "join")
    {
        var channel = message.Channel.Guild.VoiceStates.First(x => x.User == message.Author).Channel;
        channel.ConnectVoice();
    }
};

Bot.OnMessage += async (_, message) =>
{
    if (message.Content == "play")
    {
        message.Channel.Guild.VoiceConnection.PlayAudio(new YoutubeVideo(VIDEO_ID));
    }
};
```
