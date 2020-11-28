using Driscod.DiscordObjects;
using Driscod.Gateway;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public class VoiceConnection
    {
        private readonly string _channelId;

        private Voice Voice { get; set; }

        private Bot Bot { get; set; }

        private AudioStreamer AudioStreamer { get; set; }

        public bool Playing => AudioStreamer.Playing;

        public bool Stale => !Voice.Running;

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public Guild Guild => Channel.Guild;

        public EventHandler OnPlayAudio { get; set; }

        public EventHandler OnStopAudio { get; set; }

        internal VoiceConnection(Channel channel, Voice voice)
        {
            _channelId = channel.Id;

            Bot = channel.Bot;
            Voice = voice;

            Voice.OnStop += (a, b) =>
            {
                Disconnect();
            };

            if (!Voice.Running)
            {
                Voice.Start();
            }

            while (!Voice.Ready)
            {
                Thread.Sleep(200);
            }

            AudioStreamer = Voice.CreateAudioStreamer();

            AudioStreamer.OnAudioStart += (a, b) =>
            {
                OnPlayAudio?.Invoke(this, EventArgs.Empty);
            };

            AudioStreamer.OnAudioStop += (a, b) =>
            {
                OnStopAudio?.Invoke(this, EventArgs.Empty);
            };
        }

        public async Task PlayAudio(IAudioSource audioSource)
        {
            ThrowIfStale();

            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = (a, b) =>
            {
                tcs.TrySetResult(true);
            };

            AudioStreamer.OnAudioStop += handler;

            try
            {
                AudioStreamer.SendAudio(audioSource);
                await tcs.Task;
            }
            finally
            {
                AudioStreamer.OnAudioStop -= handler;
            }
        }

        public void PlayAudioSync(IAudioSource audioSource)
        {
            ThrowIfStale();
            PlayAudio(audioSource).Wait();
        }

        public void StopAudio()
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
            lock (Guild.VoiceLock)
            {
                if (Voice.Running)
                {
                    Voice.Stop().Wait();
                }
                if (Guild.VoiceConnection == this)
                {
                    Guild.VoiceConnection = null;
                }
            }
        }

        private void ThrowIfStale()
        {
            if (Stale)
            {
                throw new InvalidOperationException("Connection object is stale.");
            }
        }
    }
}
