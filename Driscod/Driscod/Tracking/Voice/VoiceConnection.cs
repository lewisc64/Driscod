using Driscod.Audio;
using Driscod.Audio.AudioSource;
using Driscod.Gateway;
using Driscod.Gateway.Consts;
using Driscod.Network;
using Driscod.Tracking.Objects;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tracking.Voice
{
    public class VoiceConnection : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string _channelId;
        private readonly IBot _bot;
        private VoiceGateway? _voiceGateway = null;
        private AudioStreamer? _audioStreamer = null;

        public EventHandler? OnPlayAudio { get; set; }
        public EventHandler? OnStopAudio { get; set; }

        internal VoiceConnection(Channel channel)
        {
            _channelId = channel.Id;
            _bot = channel.Bot;
        }

        public bool Playing => _audioStreamer?.TransmittingAudio ?? false;

        public bool Connected { get; private set; } = false;

        public bool Stale => !(_voiceGateway?.Running ?? true);

        public Channel Channel => _bot.GetObject<Channel>(_channelId) ?? throw new InvalidOperationException("The channel no longer exists.");

        public Guild Guild => Channel.Guild ?? throw new InvalidOperationException("The channel is not part of a guild.");

        public async Task Connect()
        {
            if (Connected)
            {
                throw new InvalidOperationException("Already connected.");
            }
            await CreateVoiceGateway();
            _audioStreamer = _voiceGateway!.CreateAudioStreamer();
            _audioStreamer.OnAudioStart += (a, b) =>
            {
                OnPlayAudio?.Invoke(this, EventArgs.Empty);
            };
            _audioStreamer.OnAudioStop += (a, b) =>
            {
                OnStopAudio?.Invoke(this, EventArgs.Empty);
            };
            Connected = true;
        }

        public async Task Disconnect()
        {
            ThrowIfNotConnected();

            await _voiceGateway!.Stop();
            Connected = false;
        }

        public async Task PlayAudio(IAudioSource audioSource, CancellationToken cancellationToken = default)
        {
            ThrowIfStale();
            ThrowIfNotConnected();

            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = (a, b) =>
            {
                tcs.TrySetResult(true);
            };

            _audioStreamer!.OnAudioStop += handler;

            var inControl = false;

            try
            {
                cancellationToken.Register(() =>
                {
                    if (inControl)
                    {
                        _audioStreamer.ClearAudio();
                    }
                });
                inControl = true;
                await _audioStreamer.SendAudio(audioSource, cancellationToken: cancellationToken);
                await _audioStreamer.QueueSilence();
                await tcs.Task;
            }
            finally
            {
                inControl = false;
                _audioStreamer.OnAudioStop -= handler;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect().Wait();
            }
        }

        private async Task CreateVoiceGateway()
        {
            var callCount = 0;
            Action sendAction = async () =>
            {
                if (++callCount >= 2)
                {
                    await Channel.DiscoveredOnShard.Send((int)Shard.MessageType.VoiceStateUpdate, new JObject
                    {
                        { "guild_id", Channel.Guild!.Id },
                        { "channel_id", Channel.Id },
                        { "self_mute", false },
                        { "self_deaf", false },
                    });
                }
            };

            JObject? stateData = null;
            JObject? serverData = null;

            try
            {
                Task.WhenAll(
                    Task.Run(async () =>
                    {
                        stateData = await Channel.DiscoveredOnShard.ListenForEvent<JObject>(
                            (int)Shard.MessageType.Dispatch,
                            EventNames.VoiceStateUpdate,
                            listenerCreateCallback: sendAction,
                            validator: data =>
                            {
                                return data!["guild_id"]?.ToObject<string>() == Guild.Id && data["channel_id"]?.ToObject<string>() == Channel.Id && data["user_id"]?.ToObject<string>() == _bot.User.Id;
                            },
                            timeout: TimeSpan.FromSeconds(10));
                    }),
                    Task.Run(async () =>
                    {
                        serverData = await Channel.DiscoveredOnShard.ListenForEvent<JObject>(
                            (int)Shard.MessageType.Dispatch,
                            EventNames.VoiceServerUpdate,
                            listenerCreateCallback: sendAction,
                            validator: data =>
                            {
                                return data!["guild_id"]?.ToObject<string>() == Guild.Id;
                            },
                            timeout: TimeSpan.FromSeconds(10));
                    })).Wait(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException ex)
            {
                Logger.Warn(ex, "Timed out while fetching voice data.");
            }

            _voiceGateway = new VoiceGateway(
                    Channel.DiscoveredOnShard,
                    Connectivity.FormatVoiceSocketEndpoint(serverData?["endpoint"]?.ToObject<string>() ?? throw new InvalidOperationException("Failed to get voice socket endpoint.")),
                    Guild.Id,
                    _bot.User.Id,
                    (stateData?["session_id"]?.ToObject<string>()) ?? throw new InvalidOperationException("Failed to get session ID."),
                    serverData?["token"]?.ToObject<string>() ?? throw new InvalidOperationException("Failed to get token."));

            await _voiceGateway.Start();
            await _voiceGateway.WaitForReady();
        }

        private void ThrowIfStale()
        {
            if (Stale)
            {
                throw new InvalidOperationException("Connection object is stale.");
            }
        }

        private void ThrowIfNotConnected()
        {
            if (!Connected)
            {
                throw new InvalidOperationException("Not connected.");
            }
        }
    }
}
